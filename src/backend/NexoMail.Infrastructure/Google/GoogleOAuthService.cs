using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Google;

public sealed class GoogleOAuthService(
    IHttpClientFactory httpClientFactory,
    IOptions<GmailOptions> options,
    NexoMailDbContext database,
    ITokenProtector tokenProtector)
{
    private static readonly ConcurrentDictionary<string, byte> PendingStates = new();
    private readonly GmailOptions _options = options.Value;

    public string BeginAuthorization()
    {
        EnsureConfigured();
        var state = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        PendingStates.TryAdd(state, 0);
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email https://www.googleapis.com/auth/gmail.modify https://www.googleapis.com/auth/gmail.send https://www.googleapis.com/auth/contacts.readonly https://www.googleapis.com/auth/contacts.other.readonly",
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state
        };
        return "https://accounts.google.com/o/oauth2/v2/auth?" + string.Join("&", query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }

    public async Task CompleteAuthorizationAsync(string code, string state, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (!PendingStates.TryRemove(state, out _)) throw new InvalidOperationException("La solicitud de conexión ya expiró. Inténtalo nuevamente.");

        var tokenClient = httpClientFactory.CreateClient();
        using var response = await tokenClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code, ["client_id"] = _options.ClientId, ["client_secret"] = _options.ClientSecret,
            ["redirect_uri"] = _options.RedirectUri, ["grant_type"] = "authorization_code"
        }), cancellationToken);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Google no entregó un token válido.");
        if (string.IsNullOrWhiteSpace(token.RefreshToken)) throw new InvalidOperationException("Google no entregó un refresh token. Revoca el acceso anterior e inténtalo otra vez.");

        var profileClient = httpClientFactory.CreateClient("Gmail");
        profileClient.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
        using var profileResponse = await profileClient.GetAsync("users/me/profile", cancellationToken);
        profileResponse.EnsureSuccessStatusCode();
        using var profileDocument = JsonDocument.Parse(await profileResponse.Content.ReadAsStreamAsync(cancellationToken));
        var email = profileDocument.RootElement.GetProperty("emailAddress").GetString()
            ?? throw new InvalidOperationException("No fue posible determinar la dirección Gmail.");

        if (!await database.Users.AnyAsync(x => x.Id == LocalUser.Id, cancellationToken))
        {
            database.Users.Add(new UserEntity
            {
                Id = LocalUser.Id,
                DisplayName = "Usuario local",
                Email = "local@nexomail.invalid",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        var account = await database.MailAccounts.SingleOrDefaultAsync(x => x.EmailAddress == email && x.Provider == MailProviderType.Gmail, cancellationToken);
        if (account is null)
        {
            account = new MailAccountEntity { Id = Guid.NewGuid(), UserId = LocalUser.Id, Provider = MailProviderType.Gmail, EmailAddress = email, DisplayName = "Gmail", Color = "#c6524b", CreatedAt = DateTimeOffset.UtcNow };
            database.MailAccounts.Add(account);
        }
        var credential = await database.OAuthCredentials.SingleOrDefaultAsync(x => x.MailAccountId == account.Id, cancellationToken);
        if (credential is null)
        {
            credential = new OAuthCredentialEntity { Id = Guid.NewGuid(), MailAccountId = account.Id };
            database.OAuthCredentials.Add(credential);
        }
        credential.EncryptedRefreshToken = tokenProtector.Protect(token.RefreshToken);
        credential.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
        credential.UpdatedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
    }

    public string SuccessRedirect() => _options.FrontendUrl + "?connected=google";
    public string FailureRedirect(string reason) => _options.FrontendUrl + "?error=" + Uri.EscapeDataString(reason);
    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new InvalidOperationException("Faltan las credenciales Google en User Secrets.");
    }

    private sealed record GoogleTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

public static class LocalUser { public static readonly Guid Id = Guid.Parse("a15ac2a9-ca17-4b60-878a-9d42b9a0d001"); }
