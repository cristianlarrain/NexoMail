using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Google;

public sealed class GoogleOAuthService(
    IHttpClientFactory httpClientFactory,
    IOptions<GmailOptions> options,
    NexoMailDbContext database,
    ITokenProtector tokenProtector,
    IDataProtectionProvider dataProtectionProvider,
    IUserContext userContext)
{
    private readonly GmailOptions _options = options.Value;
    private readonly IDataProtector _stateProtector = dataProtectionProvider.CreateProtector("NexoMail.GoogleOAuth.State.v1");

    public string BeginAuthorization()
    {
        EnsureConfigured();
        var state = CreateState(userContext.UserId);
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
        var stateData = ReadState(state);
        if (stateData.UserId != userContext.UserId)
            throw new InvalidOperationException("La autorización de Google no corresponde al usuario que inició sesión.");
        if (DateTimeOffset.UtcNow - stateData.IssuedAt > TimeSpan.FromMinutes(10))
            throw new InvalidOperationException("La solicitud de conexión a Google expiró. Iníciala nuevamente.");

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

        var userId = userContext.UserId;
        var account = await database.MailAccounts.SingleOrDefaultAsync(
            x => x.UserId == userId && x.EmailAddress == email && x.Provider == MailProviderType.Gmail,
            cancellationToken);
        if (account is null)
        {
            account = new MailAccountEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Provider = MailProviderType.Gmail,
                EmailAddress = email,
                DisplayName = "Gmail",
                Color = "#c6524b",
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = true
            };
            database.MailAccounts.Add(account);
        }
        else
        {
            account.IsActive = true;
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

    private string CreateState(Guid userId)
    {
        var payload = new GoogleOAuthState(userId, DateTimeOffset.UtcNow, Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)));
        return _stateProtector.Protect(JsonSerializer.Serialize(payload));
    }

    private GoogleOAuthState ReadState(string state)
    {
        try
        {
            return JsonSerializer.Deserialize<GoogleOAuthState>(_stateProtector.Unprotect(state))
                ?? throw new InvalidOperationException();
        }
        catch
        {
            throw new InvalidOperationException("La solicitud de conexión a Google no es válida o ya expiró.");
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new InvalidOperationException("Faltan las credenciales Google en la configuración segura del servidor.");
    }

    private sealed record GoogleOAuthState(Guid UserId, DateTimeOffset IssuedAt, string Nonce);
    private sealed record GoogleTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
