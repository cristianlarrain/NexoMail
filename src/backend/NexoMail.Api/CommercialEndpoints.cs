using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Api;

public static class CommercialEndpoints
{
    public static void MapNexoMailCommercial(this RouteGroupBuilder api)
    {
        api.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var entitlement = CommercialFeaturePolicy.RequiredEntitlement(http.Request.Method, http.Request.Path.Value);

            if (entitlement is not null && http.User.Identity?.IsAuthenticated == true)
            {
                var database = http.RequestServices.GetRequiredService<NexoMailDbContext>();
                var userContext = http.RequestServices.GetRequiredService<IUserContext>();
                if (!await CommercialAccessStore.HasEntitlementAsync(database, userContext.UserId, entitlement, http.RequestAborted))
                {
                    return Results.Json(new
                    {
                        error = "Esta función no está incluida en su plan actual.",
                        code = "plan_feature_required",
                        entitlement
                    }, statusCode: StatusCodes.Status403Forbidden);
                }
            }

            return await next(context);
        });

        api.MapPost("/commercial/webhooks/mercadopago", async (
            HttpContext http,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            NexoMailDbContext database,
            CancellationToken ct) =>
        {
            var dataId = http.Request.Query["data.id"].FirstOrDefault() ?? http.Request.Query["data_id"].FirstOrDefault();
            var type = http.Request.Query["type"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(dataId))
            {
                try
                {
                    using var body = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
                    if (body.RootElement.TryGetProperty("type", out var typeValue) && string.IsNullOrWhiteSpace(type)) type = typeValue.GetString();
                    if (body.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var idValue)) dataId = idValue.ToString();
                }
                catch (JsonException) { }
            }
            if (string.IsNullOrWhiteSpace(dataId)) return Results.BadRequest(new { error = "La notificación no contiene un identificador de recurso." });

            var secret = BillingSetting(configuration, "WebhookSecret", "MERCADOPAGO_WEBHOOK_SECRET");
            var accessToken = BillingSetting(configuration, "AccessToken", "MERCADOPAGO_ACCESS_TOKEN");
            if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(accessToken))
                return Results.Problem("Mercado Pago no está configurado para procesar webhooks.", statusCode: 503);

            var xSignature = http.Request.Headers["x-signature"].ToString();
            var xRequestId = http.Request.Headers["x-request-id"].ToString();
            if (!MercadoPagoBilling.ValidateWebhookSignature(xSignature, xRequestId, dataId, secret))
                return Results.Unauthorized();

            if (!string.Equals(type, "subscription_preapproval", StringComparison.OrdinalIgnoreCase))
                return Results.Ok(new { received = true, ignored = true });

            var providerSubscription = await MercadoPagoBilling.GetSubscriptionAsync(httpClientFactory, accessToken, dataId, ct);
            if (!MercadoPagoBilling.TryReadExternalReference(providerSubscription.ExternalReference, out var userId, out var planCode))
                return Results.BadRequest(new { error = "La suscripción no contiene una referencia NexoMail válida." });

            var status = MercadoPagoBilling.MapStatus(providerSubscription.Status);
            await CommercialSubscriptionMutations.ApplyProviderStateAsync(
                database,
                userId,
                planCode,
                status,
                "mercadopago",
                providerSubscription.Id,
                null,
                null,
                ct);

            return Results.Ok(new { received = true, status });
        });

        var commercial = api.MapGroup("/commercial").RequireAuthorization();

        commercial.MapGet("/subscription", async (NexoMailDbContext database, IUserContext userContext, CancellationToken ct) =>
        {
            var access = await CommercialAccessStore.GetAsync(database, userContext.UserId, ct);
            if (access is null) return Results.NotFound();

            var activePlans = await database.CommercialPlans.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .ToArrayAsync(ct);
            var current = access.AssignedPlan;
            var effective = access.EffectivePlan;

            var connectedAccounts = await database.MailAccounts.AsNoTracking()
                .CountAsync(x => x.UserId == userContext.UserId && x.IsActive, ct);
            var remaining = effective.MaxAccounts.HasValue
                ? Math.Max(0, effective.MaxAccounts.Value - connectedAccounts)
                : (int?)null;
            var overLimit = effective.MaxAccounts.HasValue && connectedAccounts > effective.MaxAccounts.Value;
            var canAddAccount = !effective.MaxAccounts.HasValue || connectedAccounts < effective.MaxAccounts.Value;

            return Results.Ok(new CommercialSubscriptionSnapshot(
                ToDto(current),
                connectedAccounts,
                remaining,
                canAddAccount,
                overLimit,
                activePlans.Select(ToDto).ToArray(),
                ToSubscriptionDto(access.Subscription),
                access.Entitlements,
                access.PaidAccessActive,
                effective.Code));
        });

        commercial.MapGet("/entitlements", () => Results.Ok(CommercialEntitlements.Definitions));

        commercial.MapGet("/billing/status", (IConfiguration configuration) =>
        {
            var accessToken = BillingSetting(configuration, "AccessToken", "MERCADOPAGO_ACCESS_TOKEN");
            var webhookSecret = BillingSetting(configuration, "WebhookSecret", "MERCADOPAGO_WEBHOOK_SECRET");
            return Results.Ok(new
            {
                provider = "mercadopago",
                configured = !string.IsNullOrWhiteSpace(accessToken),
                webhookConfigured = !string.IsNullOrWhiteSpace(webhookSecret),
                recurring = true,
                currency = "CLP"
            });
        });

        commercial.MapPost("/checkout", async (
            CommercialCheckoutRequest request,
            NexoMailDbContext database,
            IUserContext userContext,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            var planCode = NormalizeCode(request.PlanCode);
            var plan = await database.CommercialPlans.AsNoTracking().SingleOrDefaultAsync(x => x.Code == planCode && x.IsActive, ct);
            if (plan is null) return Results.NotFound(new { error = "El plan solicitado no está disponible." });
            if (plan.Code == CommercialPlanCatalog.Freemium)
                return Results.BadRequest(new { error = "Freemium no requiere una suscripción de pago." });
            if (plan.IsCorporate || plan.IsWhiteLabel)
                return Results.BadRequest(new { error = "Los planes Corporativo y White Label requieren contratación administrada." });
            if (!TryParseClpAmount(plan.Price, out var amount))
                return Results.BadRequest(new { error = "El plan no tiene un precio CLP válido para cobro automático." });

            var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userContext.UserId && x.IsActive, ct);
            if (user is null) return Results.NotFound();

            var accessToken = BillingSetting(configuration, "AccessToken", "MERCADOPAGO_ACCESS_TOKEN");
            if (string.IsNullOrWhiteSpace(accessToken))
                return Results.Problem("La contratación online todavía no está configurada en este entorno.", statusCode: 503);

            var backUrl = configuration["Billing:MercadoPago:BackUrl"];
            if (string.IsNullOrWhiteSpace(backUrl)) backUrl = "http://localhost:5173/settings/plan?billing=return";

            try
            {
                var checkout = await MercadoPagoBilling.CreateSubscriptionAsync(
                    httpClientFactory,
                    accessToken,
                    user.Id,
                    plan.Code,
                    plan.Name,
                    amount,
                    user.Email,
                    backUrl,
                    ct);
                await CommercialSubscriptionMutations.SetPendingCheckoutAsync(database, user.Id, plan.Code, "mercadopago", checkout.SubscriptionId, ct);
                return Results.Ok(new CommercialCheckoutResponse("mercadopago", checkout.SubscriptionId, checkout.CheckoutUrl, checkout.Status));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message, statusCode: 502);
            }
        });

        commercial.MapGet("/admin/status", async (NexoMailDbContext database, IUserContext userContext, CancellationToken ct) =>
        {
            var isAdministrator = await database.Users.AsNoTracking()
                .AnyAsync(x => x.Id == userContext.UserId && x.IsActive && x.IsAdministrator, ct);
            return Results.Ok(new { isAdministrator });
        });

        commercial.MapGet("/admin/users", async (NexoMailDbContext database, IUserContext userContext, CancellationToken ct) =>
        {
            if (!await IsAdministratorAsync(database, userContext.UserId, ct)) return Results.Forbid();

            var users = await database.Users.AsNoTracking()
                .OrderBy(x => x.DisplayName)
                .ThenBy(x => x.Email)
                .ToArrayAsync(ct);
            var accountCounts = await database.MailAccounts.AsNoTracking()
                .Where(x => x.IsActive)
                .GroupBy(x => x.UserId)
                .Select(group => new { UserId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);
            var plans = await database.CommercialPlans.AsNoTracking().ToArrayAsync(ct);
            var plansByCode = plans.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

            var result = new List<CommercialAdminUserDto>(users.Length);
            foreach (var user in users)
            {
                var assignedPlan = plansByCode.GetValueOrDefault(user.PlanCode)
                    ?? plansByCode.GetValueOrDefault(CommercialPlanCatalog.Freemium);
                CommercialAccessSnapshot? access = null;
                if (user.IsActive) access = await CommercialAccessStore.GetAsync(database, user.Id, ct);
                result.Add(ToAdminUserDto(user, assignedPlan, access, accountCounts.GetValueOrDefault(user.Id)));
            }

            return Results.Ok(result);
        });

        commercial.MapPatch("/admin/users/{userId:guid}/plan", async (
            Guid userId,
            CommercialUserPlanAssignmentRequest request,
            NexoMailDbContext database,
            IUserContext userContext,
            CancellationToken ct) =>
        {
            if (!await IsAdministratorAsync(database, userContext.UserId, ct)) return Results.Forbid();
            var planCode = NormalizeCode(request.PlanCode);
            var plan = await database.CommercialPlans.AsNoTracking().SingleOrDefaultAsync(x => x.Code == planCode && x.IsActive, ct);
            if (plan is null) return Results.BadRequest(new { error = "El plan seleccionado no existe o está inactivo." });
            var user = await database.Users.SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct);
            if (user is null) return Results.NotFound(new { error = "El usuario no existe o está inactivo." });

            await CommercialSubscriptionMutations.AssignPlanManuallyAsync(database, user.Id, plan.Code, ct);
            var access = await CommercialAccessStore.GetAsync(database, user.Id, ct);
            var connectedAccounts = await database.MailAccounts.AsNoTracking().CountAsync(x => x.UserId == user.Id && x.IsActive, ct);
            return Results.Ok(ToAdminUserDto(user, plan, access, connectedAccounts));
        });

        commercial.MapGet("/admin/plans", async (NexoMailDbContext database, IUserContext userContext, CancellationToken ct) =>
        {
            if (!await IsAdministratorAsync(database, userContext.UserId, ct)) return Results.Forbid();
            var plans = await database.CommercialPlans.AsNoTracking()
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .ToArrayAsync(ct);
            var assignments = await database.Users.AsNoTracking()
                .GroupBy(x => x.PlanCode)
                .Select(group => new { Code = group.Key, Count = group.Count() })
                .ToDictionaryAsync(x => x.Code, x => x.Count, StringComparer.OrdinalIgnoreCase, ct);
            return Results.Ok(plans.Select(plan => ToAdminDto(plan, assignments.GetValueOrDefault(plan.Code))).ToArray());
        });

        commercial.MapPost("/admin/plans", async (CommercialPlanWriteRequest request, NexoMailDbContext database, IUserContext userContext, CancellationToken ct) =>
        {
            if (!await IsAdministratorAsync(database, userContext.UserId, ct)) return Results.Forbid();
            var code = NormalizeCode(request.Code);
            if (Validate(request, code, isNew: true) is { } validation) return Results.BadRequest(new { error = validation });
            if (await database.CommercialPlans.AnyAsync(x => x.Code == code, ct))
                return Results.Conflict(new { error = "Ya existe un tipo de cuenta con ese código." });

            var entity = new CommercialPlanEntity { Code = code };
            Apply(entity, request);
            database.CommercialPlans.Add(entity);
            await database.SaveChangesAsync(ct);
            return Results.Created($"/api/commercial/admin/plans/{Uri.EscapeDataString(code)}", ToAdminDto(entity, 0));
        });

        commercial.MapPatch("/admin/plans/{code}", async (string code, CommercialPlanWriteRequest request, NexoMailDbContext database, IUserContext userContext, CancellationToken ct) =>
        {
            if (!await IsAdministratorAsync(database, userContext.UserId, ct)) return Results.Forbid();
            var normalizedCode = NormalizeCode(code);
            if (Validate(request, normalizedCode, isNew: false) is { } validation) return Results.BadRequest(new { error = validation });
            var entity = await database.CommercialPlans.SingleOrDefaultAsync(x => x.Code == normalizedCode, ct);
            if (entity is null) return Results.NotFound();

            if (normalizedCode == CommercialPlanCatalog.Freemium && !request.IsActive)
                return Results.BadRequest(new { error = "Freemium es el plan base de nuevos usuarios y no puede desactivarse." });

            Apply(entity, request);
            await database.SaveChangesAsync(ct);
            var assignedUsers = await database.Users.AsNoTracking().CountAsync(x => x.PlanCode == normalizedCode, ct);
            return Results.Ok(ToAdminDto(entity, assignedUsers));
        });

        commercial.MapDelete("/admin/plans/{code}", async (string code, NexoMailDbContext database, IUserContext userContext, CancellationToken ct) =>
        {
            if (!await IsAdministratorAsync(database, userContext.UserId, ct)) return Results.Forbid();
            var normalizedCode = NormalizeCode(code);
            if (normalizedCode == CommercialPlanCatalog.Freemium)
                return Results.BadRequest(new { error = "Freemium es el plan base de NexoMail y no puede eliminarse." });

            var entity = await database.CommercialPlans.SingleOrDefaultAsync(x => x.Code == normalizedCode, ct);
            if (entity is null) return Results.NotFound();
            var assignedUsers = await database.Users.AsNoTracking().CountAsync(x => x.PlanCode == normalizedCode, ct);
            if (assignedUsers > 0)
                return Results.Conflict(new { error = $"No puede eliminar este tipo de cuenta porque tiene {assignedUsers} usuario{(assignedUsers == 1 ? "" : "s")} asignado{(assignedUsers == 1 ? "" : "s")}. Reasigne esos usuarios o desactive el plan." });

            database.CommercialPlans.Remove(entity);
            await database.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static string BillingSetting(IConfiguration configuration, string key, string environmentVariable)
        => configuration[$"Billing:MercadoPago:{key}"] ?? configuration[environmentVariable] ?? string.Empty;

    private static bool TryParseClpAmount(string price, out decimal amount)
    {
        var digits = new string(price.Where(char.IsDigit).ToArray());
        return decimal.TryParse(digits, out amount) && amount > 0;
    }

    private static async Task<bool> IsAdministratorAsync(NexoMailDbContext database, Guid userId, CancellationToken ct) =>
        await database.Users.AsNoTracking().AnyAsync(x => x.Id == userId && x.IsActive && x.IsAdministrator, ct);

    private static string NormalizeCode(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_');

    private static string? Validate(CommercialPlanWriteRequest request, string code, bool isNew)
    {
        if (isNew && (code.Length is < 2 or > 32 || code.Any(character => !(char.IsLetterOrDigit(character) || character is '_' or '-'))))
            return "El código debe tener entre 2 y 32 caracteres y usar sólo letras, números, guion o guion bajo.";
        if (request.Name.Trim().Length is < 2 or > 80) return "El nombre debe tener entre 2 y 80 caracteres.";
        if (request.Price.Trim().Length is < 1 or > 40) return "El precio debe tener entre 1 y 40 caracteres.";
        if (request.Cadence.Trim().Length is < 1 or > 80) return "La periodicidad debe tener entre 1 y 80 caracteres.";
        if (request.Description.Trim().Length is < 2 or > 600) return "La descripción debe tener entre 2 y 600 caracteres.";
        if (request.MaxAccounts is < 1) return "El máximo de cuentas debe ser mayor que cero o quedar sin límite.";
        if (request.SortOrder is < 0 or > 10000) return "El orden debe estar entre 0 y 10000.";
        var features = NormalizeFeatures(request.Features);
        if (features.Count == 0) return "Agregue al menos una característica al plan.";
        if (features.Count > 30) return "El plan admite hasta 30 características.";
        var entitlements = NormalizeEntitlements(request.Entitlements);
        if (entitlements.Count == 0) return "Seleccione al menos una función habilitada para el plan.";
        if ((request.Entitlements ?? []).Any(value => !string.IsNullOrWhiteSpace(value) && !CommercialEntitlements.IsKnown(value.Trim())))
            return "El plan contiene una función no reconocida por NexoMail.";
        return null;
    }

    private static void Apply(CommercialPlanEntity entity, CommercialPlanWriteRequest request)
    {
        entity.Name = request.Name.Trim();
        entity.Price = request.Price.Trim();
        entity.Cadence = request.Cadence.Trim();
        entity.MaxAccounts = request.MaxAccounts;
        entity.Description = request.Description.Trim();
        entity.FeaturesJson = JsonSerializer.Serialize(NormalizeFeatures(request.Features));
        entity.EntitlementsJson = JsonSerializer.Serialize(NormalizeEntitlements(request.Entitlements));
        entity.IsFeatured = request.IsFeatured;
        entity.IsCorporate = request.IsCorporate;
        entity.IsWhiteLabel = request.IsWhiteLabel;
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static IReadOnlyList<string> NormalizeFeatures(IReadOnlyList<string>? features) =>
        (features ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static IReadOnlyList<string> NormalizeEntitlements(IReadOnlyList<string>? entitlements) =>
        (entitlements ?? [])
            .Select(x => x.Trim())
            .Where(x => x.Length > 0 && CommercialEntitlements.IsKnown(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> ReadFeatures(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static CommercialPlanDto ToDto(CommercialPlanEntity plan) => new(
        plan.Code,
        plan.Name,
        plan.Price,
        plan.Cadence,
        plan.MaxAccounts,
        plan.Description,
        ReadFeatures(plan.FeaturesJson),
        CommercialAccessStore.EntitlementsFor(plan),
        plan.IsFeatured,
        plan.IsCorporate,
        plan.IsWhiteLabel,
        plan.IsActive);

    private static CommercialAdminPlanDto ToAdminDto(CommercialPlanEntity plan, int assignedUsers) => new(
        plan.Code,
        plan.Name,
        plan.Price,
        plan.Cadence,
        plan.MaxAccounts,
        plan.Description,
        ReadFeatures(plan.FeaturesJson),
        CommercialAccessStore.EntitlementsFor(plan),
        plan.IsFeatured,
        plan.IsCorporate,
        plan.IsWhiteLabel,
        plan.IsActive,
        plan.SortOrder,
        assignedUsers,
        plan.Code != CommercialPlanCatalog.Freemium && assignedUsers == 0);

    private static CommercialAdminUserDto ToAdminUserDto(
        UserEntity user,
        CommercialPlanEntity? assignedPlan,
        CommercialAccessSnapshot? access,
        int connectedAccounts)
    {
        var assignedCode = assignedPlan?.Code ?? user.PlanCode;
        var assignedName = assignedPlan?.Name ?? user.PlanCode;
        var effectiveCode = access?.EffectivePlan.Code ?? assignedCode;
        var effectiveName = access?.EffectivePlan.Name ?? assignedName;
        return new CommercialAdminUserDto(
            user.Id,
            user.DisplayName,
            user.Email,
            user.IsActive,
            user.IsAdministrator,
            assignedCode,
            assignedName,
            effectiveCode,
            effectiveName,
            connectedAccounts,
            access is null ? null : ToSubscriptionDto(access.Subscription),
            user.CreatedAt,
            user.LastLoginAt);
    }

    private static CommercialSubscriptionStateDto ToSubscriptionDto(CommercialSubscriptionState state) => new(
        state.Status,
        state.Provider,
        state.ProviderCustomerId,
        state.ProviderSubscriptionId,
        state.CurrentPeriodStart,
        state.CurrentPeriodEnd,
        state.TrialEndsAt,
        state.CancelAtPeriodEnd,
        state.CanceledAt,
        state.PaymentDueAt,
        state.UpdatedAt);
}

public sealed record CommercialPlanDto(
    string Code,
    string Name,
    string Price,
    string Cadence,
    int? MaxAccounts,
    string Description,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> Entitlements,
    bool IsFeatured,
    bool IsCorporate,
    bool IsWhiteLabel,
    bool IsActive);

public sealed record CommercialSubscriptionStateDto(
    string Status,
    string? Provider,
    string? ProviderCustomerId,
    string? ProviderSubscriptionId,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    DateTimeOffset? TrialEndsAt,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CanceledAt,
    DateTimeOffset? PaymentDueAt,
    DateTimeOffset UpdatedAt);

public sealed record CommercialSubscriptionSnapshot(
    CommercialPlanDto CurrentPlan,
    int ConnectedAccounts,
    int? RemainingAccounts,
    bool CanAddAccount,
    bool OverLimit,
    IReadOnlyList<CommercialPlanDto> Plans,
    CommercialSubscriptionStateDto Subscription,
    IReadOnlyList<string> Entitlements,
    bool PaidAccessActive,
    string EffectivePlanCode);

public sealed record CommercialCheckoutRequest(string PlanCode);
public sealed record CommercialCheckoutResponse(string Provider, string SubscriptionId, string CheckoutUrl, string Status);

public sealed record CommercialAdminPlanDto(
    string Code,
    string Name,
    string Price,
    string Cadence,
    int? MaxAccounts,
    string Description,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> Entitlements,
    bool IsFeatured,
    bool IsCorporate,
    bool IsWhiteLabel,
    bool IsActive,
    int SortOrder,
    int AssignedUsers,
    bool CanDelete);

public sealed record CommercialAdminUserDto(
    Guid Id,
    string DisplayName,
    string Email,
    bool IsActive,
    bool IsAdministrator,
    string PlanCode,
    string PlanName,
    string EffectivePlanCode,
    string EffectivePlanName,
    int ConnectedAccounts,
    CommercialSubscriptionStateDto? Subscription,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record CommercialUserPlanAssignmentRequest(string PlanCode);

public sealed record CommercialPlanWriteRequest(
    string? Code,
    string Name,
    string Price,
    string Cadence,
    int? MaxAccounts,
    string Description,
    IReadOnlyList<string>? Features,
    IReadOnlyList<string>? Entitlements,
    bool IsFeatured,
    bool IsCorporate,
    bool IsWhiteLabel,
    bool IsActive,
    int SortOrder);