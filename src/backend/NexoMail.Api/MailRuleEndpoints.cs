using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Api;

public static class MailRuleEndpoints
{
    private static int _mapped;

    public static void Map(RouteGroupBuilder mail)
    {
        if (System.Threading.Interlocked.Exchange(ref _mapped, 1) == 1) return;

        mail.MapGet("/rules", async (
            IEnumerable<IMailRuleProvider> providers,
            NexoMailDbContext database,
            IUserContext userContext,
            Guid accountId,
            CancellationToken ct) =>
        {
            try
            {
                var context = await ResolveAsync(providers, database, userContext, accountId, ct);
                var rules = await context.Provider.ListAsync(accountId, ct);
                return Results.Ok(rules.Select(rule => ToPayload(rule, accountId, context.EmailAddress)));
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (HttpRequestException exception)
            {
                return Results.Problem($"El proveedor de correo rechazó la consulta de reglas ({exception.StatusCode?.ToString() ?? "sin código"}).", statusCode: 502);
            }
        });

        mail.MapGet("/rules/destinations", async (
            IEnumerable<IMailRuleProvider> providers,
            NexoMailDbContext database,
            IUserContext userContext,
            Guid accountId,
            CancellationToken ct) =>
        {
            try
            {
                var context = await ResolveAsync(providers, database, userContext, accountId, ct);
                return Results.Ok(await context.Provider.GetDestinationsAsync(accountId, ct));
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (HttpRequestException exception)
            {
                return Results.Problem($"El proveedor de correo rechazó la consulta de carpetas para reglas ({exception.StatusCode?.ToString() ?? "sin código"}).", statusCode: 502);
            }
        });

        mail.MapPost("/rules", async (
            IEnumerable<IMailRuleProvider> providers,
            NexoMailDbContext database,
            IUserContext userContext,
            RuleRequest request,
            CancellationToken ct) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Trim().Length > 500)
                    return Results.BadRequest(new { error = "Escribe qué correos debe detectar la regla." });
                if (!TryAction(request.Action, out var action))
                    return Results.BadRequest(new { error = "La acción solicitada para la regla no es válida." });

                var context = await ResolveAsync(providers, database, userContext, request.AccountId, ct);
                var created = await context.Provider.CreateAsync(
                    new MailRuleCreateRequest(
                        request.AccountId,
                        request.Query.Trim(),
                        action,
                        request.DestinationId,
                        request.DestinationName),
                    ct);
                return Results.Ok(new
                {
                    created = created.Created,
                    rule = ToPayload(created.Rule, request.AccountId, context.EmailAddress)
                });
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (HttpRequestException exception)
            {
                return Results.Problem($"El proveedor de correo rechazó la creación de la regla ({exception.StatusCode?.ToString() ?? "sin código"}).", statusCode: 502);
            }
        });

        mail.MapDelete("/rules/{accountId:guid}/{ruleId}", async (
            IEnumerable<IMailRuleProvider> providers,
            NexoMailDbContext database,
            IUserContext userContext,
            Guid accountId,
            string ruleId,
            CancellationToken ct) =>
        {
            try
            {
                var context = await ResolveAsync(providers, database, userContext, accountId, ct);
                var removed = await context.Provider.RemoveAsync(accountId, ruleId, ct);
                return removed
                    ? Results.Ok(new { removed = true, ruleId })
                    : Results.NotFound(new { error = "La regla ya no existe en el proveedor de correo." });
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (HttpRequestException exception)
            {
                return Results.Problem($"El proveedor de correo rechazó la eliminación de la regla ({exception.StatusCode?.ToString() ?? "sin código"}).", statusCode: 502);
            }
        });
    }

    private static async Task<RuleProviderContext> ResolveAsync(
        IEnumerable<IMailRuleProvider> providers,
        NexoMailDbContext database,
        IUserContext userContext,
        Guid accountId,
        CancellationToken ct)
    {
        var userId = userContext.UserId;
        var account = await database.MailAccounts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == accountId && value.UserId == userId && value.IsActive, ct)
            ?? throw new InvalidOperationException("La cuenta de correo no está disponible.");

        var provider = providers.FirstOrDefault(value => value.ProviderType == account.Provider)
            ?? throw new InvalidOperationException($"El proveedor {account.Provider} todavía no admite reglas automáticas en NexoMail.");
        return new RuleProviderContext(provider, account.EmailAddress);
    }

    private static bool TryAction(string? value, out MailRuleActionType action)
    {
        action = value?.Trim().ToLowerInvariant() switch
        {
            "trash" => MailRuleActionType.Trash,
            "archive" => MailRuleActionType.Archive,
            "markread" or "mark_read" or "read" => MailRuleActionType.MarkRead,
            "movetofolder" or "move_to_folder" or "move" => MailRuleActionType.MoveToFolder,
            _ => (MailRuleActionType)(-1)
        };
        return Enum.IsDefined(action);
    }

    private static object ToPayload(MailRuleDefinition rule, Guid accountId, string emailAddress) => new
    {
        ruleId = rule.RuleId,
        query = rule.Query,
        accountId,
        account = emailAddress,
        action = ActionCode(rule.Action),
        destinationId = rule.DestinationId,
        destinationName = rule.DestinationName
    };

    private static string ActionCode(MailRuleActionType action) => action switch
    {
        MailRuleActionType.Trash => "trash",
        MailRuleActionType.Archive => "archive",
        MailRuleActionType.MarkRead => "markRead",
        MailRuleActionType.MoveToFolder => "moveToFolder",
        _ => "unknown"
    };

    private sealed record RuleProviderContext(IMailRuleProvider Provider, string EmailAddress);
}

public sealed record RuleRequest(
    Guid AccountId,
    string Query,
    string Action,
    string? DestinationId,
    string? DestinationName);
