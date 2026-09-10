using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;

namespace NexoMail.Api;

public static class MailRuleEndpoints
{
    private static int _mapped;

    public static void Map(RouteGroupBuilder mail)
    {
        // Minimal APIs can be re-executed during hot reload. Register these routes only once
        // per process so the router never ends up with two identical /rules/trash endpoints.
        if (System.Threading.Interlocked.Exchange(ref _mapped, 1) == 1) return;

        mail.MapGet("/rules/trash", async (
            IHttpClientFactory httpClientFactory,
            NexoMailDbContext database,
            ITokenProtector tokenProtector,
            IOptions<GmailOptions> gmailOptions,
            IUserContext userContext,
            IHostEnvironment environment,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            Guid accountId,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("NexoMail.MailRules");
            httpContext.Response.Headers["X-NexoMail-Rules"] = "2026-09-10.4";

            try
            {
                var userId = userContext.UserId;
                var account = await database.MailAccounts.AsNoTracking()
                    .SingleOrDefaultAsync(value => value.Id == accountId && value.UserId == userId && value.IsActive, ct);

                if (account is null)
                {
                    httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                    await httpContext.Response.WriteAsJsonAsync(new { error = "La cuenta de correo no está disponible." }, ct);
                    return;
                }

                if (account.Provider != MailProviderType.Gmail)
                {
                    httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await httpContext.Response.WriteAsJsonAsync(new { error = "Las reglas automáticas están disponibles actualmente para cuentas Gmail." }, ct);
                    return;
                }

                var service = new GmailRuleService(httpClientFactory, database, tokenProtector, gmailOptions);
                var rules = await service.ListTrashRulesAsync(account.Id, ct);
                var payload = rules.Select(rule => new
                {
                    filterId = rule.FilterId,
                    query = rule.Query,
                    accountId = account.Id,
                    account = account.EmailAddress,
                    action = "trash"
                }).ToArray();

                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                await httpContext.Response.WriteAsJsonAsync(payload, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (InvalidOperationException exception)
            {
                logger.LogWarning(exception, "No fue posible consultar reglas Gmail por un problema de autorización o configuración.");
                if (httpContext.Response.HasStarted) throw;
                httpContext.Response.Clear();
                httpContext.Response.Headers["X-NexoMail-Rules"] = "2026-09-10.4";
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsJsonAsync(new { error = exception.Message }, ct);
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning(exception, "Gmail rechazó o interrumpió la consulta de reglas.");
                if (httpContext.Response.HasStarted) throw;
                httpContext.Response.Clear();
                httpContext.Response.Headers["X-NexoMail-Rules"] = "2026-09-10.4";
                httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
                await httpContext.Response.WriteAsJsonAsync(new
                {
                    error = $"No fue posible consultar las reglas en Gmail ({exception.StatusCode?.ToString() ?? "sin código"}): {exception.Message}"
                }, ct);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Error no controlado al consultar reglas Gmail para la cuenta {AccountId}.", accountId);
                if (httpContext.Response.HasStarted) throw;
                httpContext.Response.Clear();
                httpContext.Response.Headers["X-NexoMail-Rules"] = "2026-09-10.4";
                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                var error = environment.IsDevelopment()
                    ? $"Error interno al consultar reglas ({exception.GetType().Name}): {exception.Message}"
                    : $"No fue posible consultar las reglas de Gmail ({exception.GetType().Name}).";
                await httpContext.Response.WriteAsJsonAsync(new { error }, ct);
            }
        });

        mail.MapPost("/rules/trash", async (
            IHttpClientFactory httpClientFactory,
            NexoMailDbContext database,
            ITokenProtector tokenProtector,
            IOptions<GmailOptions> gmailOptions,
            IUserContext userContext,
            IHostEnvironment environment,
            ILoggerFactory loggerFactory,
            TrashRuleRequest request,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("NexoMail.MailRules");
            try
            {
                var query = request.Query?.Trim() ?? string.Empty;
                if (query.Length is < 1 or > 500)
                    return Results.BadRequest(new { error = "Escribe qué correos debe detectar la regla." });

                var userId = userContext.UserId;
                var account = await database.MailAccounts.AsNoTracking()
                    .SingleOrDefaultAsync(value => value.Id == request.AccountId && value.UserId == userId && value.IsActive, ct);
                if (account is null) return Results.NotFound(new { error = "La cuenta de correo no está disponible." });
                if (account.Provider != MailProviderType.Gmail)
                    return Results.BadRequest(new { error = "Las reglas automáticas están disponibles actualmente para cuentas Gmail." });

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
                logger.LogWarning(exception, "No fue posible crear una regla Gmail por un problema de autorización o configuración.");
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning(exception, "Gmail rechazó o interrumpió la creación de una regla.");
                return Results.Problem(
                    $"No fue posible crear la regla en Gmail ({exception.StatusCode?.ToString() ?? "sin código"}).",
                    statusCode: 502);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Error no controlado al crear una regla Gmail para la cuenta {AccountId}.", request.AccountId);
                var detail = environment.IsDevelopment()
                    ? $"Error interno al crear la regla ({exception.GetType().Name}): {exception.Message}"
                    : "No fue posible crear la regla de Gmail.";
                return Results.Problem(detail, statusCode: 500);
            }
        });

        mail.MapDelete("/rules/trash/{accountId:guid}/{filterId}", async (
            IHttpClientFactory httpClientFactory,
            NexoMailDbContext database,
            ITokenProtector tokenProtector,
            IOptions<GmailOptions> gmailOptions,
            IUserContext userContext,
            IHostEnvironment environment,
            ILoggerFactory loggerFactory,
            Guid accountId,
            string filterId,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("NexoMail.MailRules");
            try
            {
                var userId = userContext.UserId;
                var account = await database.MailAccounts.AsNoTracking()
                    .SingleOrDefaultAsync(value => value.Id == accountId && value.UserId == userId && value.IsActive, ct);
                if (account is null) return Results.NotFound(new { error = "La cuenta de correo no está disponible." });
                if (account.Provider != MailProviderType.Gmail)
                    return Results.BadRequest(new { error = "Las reglas automáticas están disponibles actualmente para cuentas Gmail." });

                var service = new GmailRuleService(httpClientFactory, database, tokenProtector, gmailOptions);
                var removed = await service.RemoveRuleAsync(account.Id, filterId, ct);
                return removed
                    ? Results.Ok(new { removed = true, filterId })
                    : Results.NotFound(new { error = "La regla ya no existe en Gmail." });
            }
            catch (InvalidOperationException exception)
            {
                logger.LogWarning(exception, "No fue posible eliminar una regla Gmail por un problema de autorización o configuración.");
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning(exception, "Gmail rechazó o interrumpió la eliminación de una regla.");
                return Results.Problem(
                    $"No fue posible quitar la regla en Gmail ({exception.StatusCode?.ToString() ?? "sin código"}).",
                    statusCode: 502);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Error no controlado al eliminar una regla Gmail para la cuenta {AccountId}.", accountId);
                var detail = environment.IsDevelopment()
                    ? $"Error interno al eliminar la regla ({exception.GetType().Name}): {exception.Message}"
                    : "No fue posible eliminar la regla de Gmail.";
                return Results.Problem(detail, statusCode: 500);
            }
        });
    }
}

public sealed record TrashRuleRequest(Guid AccountId, string Query);
