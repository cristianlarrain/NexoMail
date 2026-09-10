using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;

namespace NexoMail.Api;

public static class MailRuleEndpoints
{
    public static void Map(RouteGroupBuilder mail)
    {
        mail.MapPost("/rules/trash", async (
            IHttpClientFactory httpClientFactory,
            NexoMailDbContext database,
            ITokenProtector tokenProtector,
            IOptions<GmailOptions> gmailOptions,
            IUserContext userContext,
            TrashRuleRequest request,
            CancellationToken ct) =>
        {
            var query = request.Query?.Trim() ?? string.Empty;
            if (query.Length is < 1 or > 500)
                return Results.BadRequest(new { error = "Escribe qué correos debe detectar la regla." });

            var account = await database.MailAccounts.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == request.AccountId && value.UserId == userContext.UserId && value.IsActive, ct);
            if (account is null) return Results.NotFound(new { error = "La cuenta de correo no está disponible." });
            if (account.Provider != MailProviderType.Gmail)
                return Results.BadRequest(new { error = "Las reglas automáticas están disponibles actualmente para cuentas Gmail." });

            try
            {
                var service = new GmailRuleService(httpClientFactory, database, tokenProtector, gmailOptions);
                var result = await service.CreateTrashRuleAsync(account.Id, query, ct);
                return Results.Ok(new
                {
                    filterId = result.FilterId,
                    query = result.Query,
                    created = result.Created,
                    accountId = account.Id,
                    account = account.EmailAddress,
                    action = "trash"
                });
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (HttpRequestException exception)
            {
                return Results.Problem(
                    $"No fue posible crear la regla en Gmail ({exception.StatusCode?.ToString() ?? "sin código"}).",
                    statusCode: 502);
            }
        });
    }
}

public sealed record TrashRuleRequest(Guid AccountId, string Query);
