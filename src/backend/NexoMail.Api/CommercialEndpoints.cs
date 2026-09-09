using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Api;

public static class CommercialEndpoints
{
    public static void MapNexoMailCommercial(this RouteGroupBuilder api)
    {
        var commercial = api.MapGroup("/commercial").RequireAuthorization();

        commercial.MapGet("/subscription", async (NexoMailDbContext database, IUserContext userContext, CancellationToken ct) =>
        {
            var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userContext.UserId, ct);
            if (user is null) return Results.NotFound();

            var activePlans = await database.CommercialPlans.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .ToArrayAsync(ct);
            var current = await database.CommercialPlans.AsNoTracking().SingleOrDefaultAsync(x => x.Code == user.PlanCode, ct)
                ?? activePlans.FirstOrDefault(x => x.Code == CommercialPlanCatalog.Freemium)
                ?? activePlans.FirstOrDefault();
            if (current is null) return Results.Problem("No existen planes comerciales configurados.", statusCode: 500);

            var connectedAccounts = await database.MailAccounts.AsNoTracking()
                .CountAsync(x => x.UserId == userContext.UserId && x.IsActive, ct);
            var remaining = current.MaxAccounts.HasValue
                ? Math.Max(0, current.MaxAccounts.Value - connectedAccounts)
                : (int?)null;
            var overLimit = current.MaxAccounts.HasValue && connectedAccounts > current.MaxAccounts.Value;
            var canAddAccount = !current.MaxAccounts.HasValue || connectedAccounts < current.MaxAccounts.Value;

            return Results.Ok(new CommercialSubscriptionSnapshot(
                ToDto(current),
                connectedAccounts,
                remaining,
                canAddAccount,
                overLimit,
                activePlans.Select(ToDto).ToArray()));
        });

        commercial.MapGet("/admin/status", async (NexoMailDbContext database, IUserContext userContext, CancellationToken ct) =>
        {
            var isAdministrator = await database.Users.AsNoTracking()
                .AnyAsync(x => x.Id == userContext.UserId && x.IsActive && x.IsAdministrator, ct);
            return Results.Ok(new { isAdministrator });
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
        entity.IsFeatured = request.IsFeatured;
        entity.IsCorporate = request.IsCorporate;
        entity.IsWhiteLabel = request.IsWhiteLabel;
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static IReadOnlyList<string> NormalizeFeatures(IReadOnlyList<string>? features) =>
        (features ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

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
        plan.IsFeatured,
        plan.IsCorporate,
        plan.IsWhiteLabel,
        plan.IsActive,
        plan.SortOrder,
        assignedUsers,
        plan.Code != CommercialPlanCatalog.Freemium && assignedUsers == 0);
}

public sealed record CommercialPlanDto(
    string Code,
    string Name,
    string Price,
    string Cadence,
    int? MaxAccounts,
    string Description,
    IReadOnlyList<string> Features,
    bool IsFeatured,
    bool IsCorporate,
    bool IsWhiteLabel,
    bool IsActive);

public sealed record CommercialSubscriptionSnapshot(
    CommercialPlanDto CurrentPlan,
    int ConnectedAccounts,
    int? RemainingAccounts,
    bool CanAddAccount,
    bool OverLimit,
    IReadOnlyList<CommercialPlanDto> Plans);

public sealed record CommercialAdminPlanDto(
    string Code,
    string Name,
    string Price,
    string Cadence,
    int? MaxAccounts,
    string Description,
    IReadOnlyList<string> Features,
    bool IsFeatured,
    bool IsCorporate,
    bool IsWhiteLabel,
    bool IsActive,
    int SortOrder,
    int AssignedUsers,
    bool CanDelete);

public sealed record CommercialPlanWriteRequest(
    string? Code,
    string Name,
    string Price,
    string Cadence,
    int? MaxAccounts,
    string Description,
    IReadOnlyList<string>? Features,
    bool IsFeatured,
    bool IsCorporate,
    bool IsWhiteLabel,
    bool IsActive,
    int SortOrder);
