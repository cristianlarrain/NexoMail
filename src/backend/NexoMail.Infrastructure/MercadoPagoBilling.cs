using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NexoMail.Infrastructure;

public sealed record MercadoPagoCheckoutResult(string SubscriptionId, string CheckoutUrl, string Status);
public sealed record MercadoPagoSubscriptionSnapshot(string Id, string Status, string? ExternalReference, DateTimeOffset? LastModified);

public static class MercadoPagoBilling
{
    private const string BaseUrl = "https://api.mercadopago.com/";

    public static async Task<MercadoPagoCheckoutResult> CreateSubscriptionAsync(
        IHttpClientFactory httpClientFactory,
        string accessToken,
        Guid userId,
        string planCode,
        string planName,
        decimal amount,
        string payerEmail,
        string backUrl,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) throw new InvalidOperationException("Mercado Pago no está configurado.");
        if (amount <= 0) throw new InvalidOperationException("El plan no tiene un monto válido para cobro recurrente.");

        var client = CreateClient(httpClientFactory, accessToken);
        var request = new
        {
            reason = $"NexoMail {planName}",
            external_reference = $"{userId:N}:{planCode}",
            payer_email = payerEmail,
            auto_recurring = new
            {
                frequency = 1,
                frequency_type = "months",
                transaction_amount = amount,
                currency_id = "CLP"
            },
            back_url = backUrl,
            status = "pending"
        };

        using var response = await client.PostAsJsonAsync("preapproval", request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Mercado Pago rechazó la creación de la suscripción ({(int)response.StatusCode}).");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var id = root.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
        var initPoint = root.TryGetProperty("init_point", out var initValue) ? initValue.GetString() : null;
        var status = root.TryGetProperty("status", out var statusValue) ? statusValue.GetString() : "pending";
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(initPoint))
            throw new InvalidOperationException("Mercado Pago no entregó un enlace válido para completar la suscripción.");

        return new MercadoPagoCheckoutResult(id, initPoint, status ?? "pending");
    }

    public static async Task<MercadoPagoSubscriptionSnapshot> GetSubscriptionAsync(
        IHttpClientFactory httpClientFactory,
        string accessToken,
        string subscriptionId,
        CancellationToken ct)
    {
        var client = CreateClient(httpClientFactory, accessToken);
        using var response = await client.GetAsync($"preapproval/{Uri.EscapeDataString(subscriptionId)}", ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"No fue posible consultar la suscripción en Mercado Pago ({(int)response.StatusCode}).");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var id = root.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
        var status = root.TryGetProperty("status", out var statusValue) ? statusValue.GetString() : null;
        var externalReference = root.TryGetProperty("external_reference", out var externalValue) ? externalValue.GetString() : null;
        DateTimeOffset? lastModified = null;
        if (root.TryGetProperty("last_modified", out var modifiedValue) && modifiedValue.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(modifiedValue.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            lastModified = parsed;

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(status))
            throw new InvalidOperationException("Mercado Pago devolvió una suscripción incompleta.");
        return new MercadoPagoSubscriptionSnapshot(id, status, externalReference, lastModified);
    }

    public static async Task<MercadoPagoSubscriptionSnapshot> CancelSubscriptionAsync(
        IHttpClientFactory httpClientFactory,
        string accessToken,
        string subscriptionId,
        CancellationToken ct)
    {
        var client = CreateClient(httpClientFactory, accessToken);
        using var response = await client.PutAsJsonAsync($"preapproval/{Uri.EscapeDataString(subscriptionId)}", new { status = "canceled" }, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Mercado Pago no pudo cancelar la suscripción ({(int)response.StatusCode}).");
        return await GetSubscriptionAsync(httpClientFactory, accessToken, subscriptionId, ct);
    }

    public static bool ValidateWebhookSignature(string xSignature, string xRequestId, string dataId, string secret)
    {
        if (string.IsNullOrWhiteSpace(xSignature) || string.IsNullOrWhiteSpace(xRequestId) || string.IsNullOrWhiteSpace(dataId) || string.IsNullOrWhiteSpace(secret))
            return false;

        string? ts = null;
        string? v1 = null;
        foreach (var part in xSignature.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0) continue;
            var key = part[..separator].Trim();
            var value = part[(separator + 1)..].Trim();
            if (key == "ts") ts = value;
            else if (key == "v1") v1 = value;
        }
        if (string.IsNullOrWhiteSpace(ts) || string.IsNullOrWhiteSpace(v1)) return false;

        var normalizedDataId = dataId.Any(char.IsLetter) ? dataId.ToLowerInvariant() : dataId;
        var manifest = $"id:{normalizedDataId};request-id:{xRequestId};ts:{ts};";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var calculated = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
        var supplied = v1.ToLowerInvariant();
        if (calculated.Length != supplied.Length) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(calculated), Encoding.ASCII.GetBytes(supplied));
    }

    public static string MapStatus(string providerStatus) => providerStatus.Trim().ToLowerInvariant() switch
    {
        "authorized" => NexoMail.Domain.CommercialSubscriptionStatuses.Active,
        "pending" => NexoMail.Domain.CommercialSubscriptionStatuses.Pending,
        "paused" => NexoMail.Domain.CommercialSubscriptionStatuses.PastDue,
        "cancelled" or "canceled" => NexoMail.Domain.CommercialSubscriptionStatuses.Canceled,
        _ => NexoMail.Domain.CommercialSubscriptionStatuses.PastDue
    };

    public static bool TryReadExternalReference(string? value, out Guid userId, out string planCode)
    {
        userId = Guid.Empty;
        planCode = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out userId) || string.IsNullOrWhiteSpace(parts[1])) return false;
        planCode = parts[1];
        return true;
    }

    private static HttpClient CreateClient(IHttpClientFactory httpClientFactory, string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) throw new InvalidOperationException("Mercado Pago no está configurado.");
        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(BaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        return client;
    }
}
