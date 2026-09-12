using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;

namespace NexoMail.Infrastructure.Microsoft;

public sealed class MicrosoftGraphClientFactory(
    IHttpClientFactory httpClientFactory,
    NexoMailDbContext database,
    ITokenProtector tokenProtector,
    IOptions<Microsoft365Options> options)
{
    public async Task<HttpClient> CreateAsync(Guid accountId, CancellationToken ct)
    {
        var credential = await database.OAuthCredentials.AsNoTracking()
            .Where(x => x.MailAccountId == accountId)
            .Join(database.MailAccounts.AsNoTracking().Where(x => x.IsActive && x.Provider == MailProviderType.MicrosoftGraph),
                credential => credential.MailAccountId,
                account => account.Id,
                (credential, _) => credential)
            .SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No existe una credencial OAuth válida para esta cuenta Microsoft 365.");

        var configured = options.Value;
        if (string.IsNullOrWhiteSpace(configured.ClientId) || string.IsNullOrWhiteSpace(configured.ClientSecret))
            throw new InvalidOperationException("Microsoft 365 no está configurado en el servidor.");

        var refreshToken = tokenProtector.Unprotect(credential.EncryptedRefreshToken);
        var tenant = MicrosoftOAuthService.NormalizeTenant(configured.AuthorityTenant);
        var tokenClient = httpClientFactory.CreateClient();
        using var response = await tokenClient.PostAsync(
            $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = configured.ClientId,
                ["client_secret"] = configured.ClientSecret,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token",
                ["scope"] = MicrosoftOAuthService.Scope
            }), ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(MicrosoftOAuthErrors.ToUserMessage(body));
        }
        using var tokenDocument = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var accessToken = tokenDocument.RootElement.TryGetProperty("access_token", out var token) ? token.GetString() : null;
        if (string.IsNullOrWhiteSpace(accessToken)) throw new InvalidOperationException("Microsoft no entregó un token de acceso válido.");

        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
