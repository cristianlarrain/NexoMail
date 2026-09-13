using NexoMail.Application;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Api;

public static class BillingEndpoints
{
    public static void MapNexoMailBilling(this RouteGroupBuilder api)
    {
        var billing = api.MapGroup("/commercial/billing").RequireAuthorization();

        billing.MapPost("/sync", async (
            NexoMailDbContext database,
            IUserContext userContext,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            var access = await CommercialAccessStore.GetAsync(database, userContext.UserId, ct);
            if (access is null) return Results.NotFound();
            if (!string.Equals(access.Subscription.Provider, "mercadopago", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(access.Subscription.ProviderSubscriptionId))
                return Results.BadRequest(new { error = "No existe una suscripción de Mercado Pago pendiente de sincronización." });

            var accessToken = MercadoPagoAccessToken(configuration);
            if (string.IsNullOrWhiteSpace(accessToken))
                return Results.Problem("Mercado Pago no está configurado en este entorno.", statusCode: 503);

            try
            {
                var provider = await MercadoPagoBilling.GetSubscriptionAsync(
                    httpClientFactory,
                    accessToken,
                    access.Subscription.ProviderSubscriptionId,
                    ct);
                if (!MercadoPagoBilling.TryReadExternalReference(provider.ExternalReference, out var providerUserId, out var planCode) ||
                    providerUserId != userContext.UserId)
                    return Results.BadRequest(new { error = "La suscripción de Mercado Pago no corresponde a esta cuenta NexoMail." });

                var status = MercadoPagoBilling.MapStatus(provider.Status);
                await CommercialSubscriptionMutations.ApplyProviderStateAsync(
                    database,
                    userContext.UserId,
                    planCode,
                    status,
                    "mercadopago",
                    provider.Id,
                    null,
                    null,
                    ct);
                return Results.Ok(new { status });
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message, statusCode: 502);
            }
        });

        billing.MapPost("/cancel", async (
            NexoMailDbContext database,
            IUserContext userContext,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            var access = await CommercialAccessStore.GetAsync(database, userContext.UserId, ct);
            if (access is null) return Results.NotFound();
            if (!string.Equals(access.Subscription.Provider, "mercadopago", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(access.Subscription.ProviderSubscriptionId))
                return Results.BadRequest(new { error = "No existe una suscripción de Mercado Pago que pueda cancelarse." });

            var accessToken = MercadoPagoAccessToken(configuration);
            if (string.IsNullOrWhiteSpace(accessToken))
                return Results.Problem("Mercado Pago no está configurado en este entorno.", statusCode: 503);

            try
            {
                var provider = await MercadoPagoBilling.CancelSubscriptionAsync(
                    httpClientFactory,
                    accessToken,
                    access.Subscription.ProviderSubscriptionId,
                    ct);
                if (!MercadoPagoBilling.TryReadExternalReference(provider.ExternalReference, out var providerUserId, out var planCode) ||
                    providerUserId != userContext.UserId)
                    return Results.BadRequest(new { error = "La suscripción de Mercado Pago no corresponde a esta cuenta NexoMail." });

                var status = MercadoPagoBilling.MapStatus(provider.Status);
                await CommercialSubscriptionMutations.ApplyProviderStateAsync(
                    database,
                    userContext.UserId,
                    planCode,
                    status,
                    "mercadopago",
                    provider.Id,
                    null,
                    null,
                    ct);
                return Results.Ok(new { status });
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message, statusCode: 502);
            }
        });
    }

    private static string MercadoPagoAccessToken(IConfiguration configuration) =>
        configuration["Billing:MercadoPago:AccessToken"] ?? configuration["MERCADOPAGO_ACCESS_TOKEN"] ?? string.Empty;
}
