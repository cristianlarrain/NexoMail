using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Google;

public sealed class GmailRuleService(
    IHttpClientFactory httpClientFactory,
    NexoMailDbContext database,
    ITokenProtector tokenProtector,
    IOptions<GmailOptions> options)
{
    public async Task<GmailTrashRuleResult> CreateTrashRuleAsync(Guid accountId, string query, CancellationToken cancellationToken)
    {
        var normalizedQuery = query.Trim();
        if (normalizedQuery.Length is < 1 or > 500)
            throw new InvalidOperationException("La condición de la regla debe tener entre 1 y 500 caracteres.");

        var client = await CreateClientAsync(accountId, cancellationToken);

        using (var listResponse = await client.GetAsync("users/me/settings/filters", cancellationToken))
        {
            await EnsureRulePermissionAsync(listResponse, cancellationToken);
            using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStreamAsync(cancellationToken));
            if (list.RootElement.TryGetProperty("filter", out var filters))
            {
                foreach (var filter in filters.EnumerateArray())
                {
                    var criteriaQuery = filter.TryGetProperty("criteria", out var criteria)
                        && criteria.TryGetProperty("query", out var queryElement)
                        ? queryElement.GetString()
                        : null;
                    var trashes = filter.TryGetProperty("action", out var action)
                        && action.TryGetProperty("addLabelIds", out var labels)
                        && labels.EnumerateArray().Any(label => string.Equals(label.GetString(), "TRASH", StringComparison.OrdinalIgnoreCase));
                    if (!trashes || !string.Equals(criteriaQuery?.Trim(), normalizedQuery, StringComparison.OrdinalIgnoreCase)) continue;

                    var existingId = filter.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                    return new GmailTrashRuleResult(existingId ?? string.Empty, normalizedQuery, false);
                }
            }
        }

        using var response = await client.PostAsJsonAsync("users/me/settings/filters", new
        {
            criteria = new { query = normalizedQuery },
            action = new { addLabelIds = new[] { "TRASH" } }
        }, cancellationToken);
        await EnsureRulePermissionAsync(response, cancellationToken);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var id = document.RootElement.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
        return new GmailTrashRuleResult(id, normalizedQuery, true);
    }

    private async Task<HttpClient> CreateClientAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var credential = await database.OAuthCredentials.AsNoTracking()
            .SingleOrDefaultAsync(value => value.MailAccountId == accountId, cancellationToken)
            ?? throw new InvalidOperationException("No existe una credencial OAuth para esta cuenta.");

        var settings = options.Value;
        var refreshToken = tokenProtector.Unprotect(credential.EncryptedRefreshToken);
        var tokenClient = httpClientFactory.CreateClient();
        using var tokenResponse = await tokenClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId,
            ["client_secret"] = settings.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        }), cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();

        using var tokenDocument = JsonDocument.Parse(await tokenResponse.Content.ReadAsStreamAsync(cancellationToken));
        var accessToken = tokenDocument.RootElement.TryGetProperty("access_token", out var tokenElement) ? tokenElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Google no entregó un token de acceso válido.");

        var client = httpClientFactory.CreateClient("Gmail");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static async Task EnsureRulePermissionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("Esta cuenta necesita autorizar el permiso para administrar reglas de Gmail. Vuelve a conectar la cuenta desde Configurar y repite la instrucción.");
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(string.IsNullOrWhiteSpace(detail)
            ? $"Gmail rechazó la creación de la regla ({(int)response.StatusCode})."
            : $"Gmail rechazó la creación de la regla ({(int)response.StatusCode}).", null, response.StatusCode);
    }
}

public sealed record GmailTrashRuleResult(string FilterId, string Query, bool Created);
