using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;

namespace NexoMail.Infrastructure.Microsoft;

public sealed class MicrosoftOAuthService(
    IHttpClientFactory httpClientFactory,
    IOptions<MicrosoftGraphOptions> options,
    NexoMailDbContext database,
    ITokenProtector tokenProtector,
    IDataProtectionProvider dataProtectionProvider,
    IUserContext userContext,
    MailAccountConnectionPolicy connectionPolicy)
{
    public const string AuthorizeEndpoint = "https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize";
    public const string TokenEndpoint = "https://login.microsoftonline.com/organizations/oauth2/v2.0/token";
    public const string GraphMeEndpoint = "https://graph.microsoft.com/v1.0/me?$select=id,displayName,mail,userPrincipalName";
    public const string Phase1Scopes = "openid profile email offline_access User.Read Mail.ReadWrite";

    private readonly MicrosoftGraphOptions _options = options.Value;
    private readonly IDataProtector _stateProtector = dataProtectionProvider.CreateProtector("NexoMail.MicrosoftOAuth.State.v1");

    public string BeginAuthorization()
    {
        EnsureConfigured();
        var state = CreateState(userContext.UserId);
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = Phase1Scopes,
            ["state"] = state
        };

        return AuthorizeEndpoint + "?" + string.Join("&", query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }

    public async Task CompleteAuthorizationAsync(string code, string state, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var stateData = ReadState(state);

        if (stateData.UserId != userContext.UserId)
            throw new InvalidOperationException("La autorización de Microsoft no corresponde al usuario que inició sesión.");

        if (DateTimeOffset.UtcNow - stateData.IssuedAt > TimeSpan.FromMinutes(10))
            throw new InvalidOperationException("La solicitud de conexión a Microsoft expiró. Iníciala nuevamente.");

        var tokenClient = httpClientFactory.CreateClient();
        using var tokenResponse = await tokenClient.PostAsync(
            TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["redirect_uri"] = _options.RedirectUri,
                ["grant_type"] = "authorization_code",
                ["scope"] = Phase1Scopes
            }),
            cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();

        var token = await tokenResponse.Content.ReadFromJsonAsync<MicrosoftTokenResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Microsoft no entregó un token válido.");
        if (string.IsNullOrWhiteSpace(token.AccessToken))
            throw new InvalidOperationException("Microsoft no entregó un access token válido.");
        if (string.IsNullOrWhiteSpace(token.RefreshToken))
            throw new InvalidOperationException("Microsoft no entregó un refresh token. Inicia nuevamente la conexión.");

        var graphClient = httpClientFactory.CreateClient("MicrosoftGraph");
        graphClient.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
        using var profileResponse = await graphClient.GetAsync(GraphMeEndpoint, cancellationToken);
        profileResponse.EnsureSuccessStatusCode();

        var profile = await profileResponse.Content.ReadFromJsonAsync<MicrosoftProfileResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Microsoft Graph no entregó un perfil válido.");
        var email = !string.IsNullOrWhiteSpace(profile.Mail)
            ? profile.Mail.Trim()
            : profile.UserPrincipalName?.Trim();
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("No fue posible determinar la dirección de correo de Microsoft 365.");

        var userId = userContext.UserId;
        var account = await database.MailAccounts.SingleOrDefaultAsync(
            x => x.UserId == userId && x.EmailAddress == email,
            cancellationToken);

        if (account is not null && account.Provider != MailProviderType.MicrosoftGraph)
            throw new InvalidOperationException("Esta dirección ya está conectada en NexoMail mediante otro proveedor.");

        if (account is null)
        {
            await connectionPolicy.EnsureCanConnectAnotherAccountAsync(cancellationToken);
            account = new MailAccountEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Provider = MailProviderType.MicrosoftGraph,
                EmailAddress = email,
                DisplayName = "Microsoft 365",
                Color = "#0078d4",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            database.MailAccounts.Add(account);
        }
        else
        {
            account.IsActive = true;
            account.DisplayName = "Microsoft 365";
            account.Color = "#0078d4";
        }

        var credential = await database.OAuthCredentials.SingleOrDefaultAsync(
            x => x.MailAccountId == account.Id,
            cancellationToken);
        if (credential is null)
        {
            credential = new OAuthCredentialEntity
            {
                Id = Guid.NewGuid(),
                MailAccountId = account.Id
            };
            database.OAuthCredentials.Add(credential);
        }

        credential.EncryptedRefreshToken = tokenProtector.Protect(token.RefreshToken);
        credential.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
        credential.UpdatedAt = DateTimeOffset.UtcNow;

        await database.SaveChangesAsync(cancellationToken);
    }

    private string CreateState(Guid userId)
    {
        var payload = new MicrosoftOAuthState(
            userId,
            DateTimeOffset.UtcNow,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
        return _stateProtector.Protect(JsonSerializer.Serialize(payload));
    }

    private MicrosoftOAuthState ReadState(string state)
    {
        try
        {
            return JsonSerializer.Deserialize<MicrosoftOAuthState>(_stateProtector.Unprotect(state))
                ?? throw new InvalidOperationException();
        }
        catch
        {
            throw new InvalidOperationException("La solicitud de conexión a Microsoft no es válida o ya expiró.");
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) ||
            string.IsNullOrWhiteSpace(_options.ClientSecret) ||
            string.IsNullOrWhiteSpace(_options.RedirectUri))
        {
            throw new InvalidOperationException("Faltan las credenciales Microsoft en la configuración segura del servidor.");
        }
    }

    private sealed record MicrosoftOAuthState(Guid UserId, DateTimeOffset IssuedAt, string Nonce);

    private sealed record MicrosoftTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record MicrosoftProfileResponse(
        [property: JsonPropertyName("mail")] string? Mail,
        [property: JsonPropertyName("userPrincipalName")] string? UserPrincipalName);
}
