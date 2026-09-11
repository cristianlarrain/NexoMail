using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using NexoMail.Application;
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
        _ = httpClientFactory;
        _ = database;
        _ = tokenProtector;
        _ = connectionPolicy;

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

    private string CreateState(Guid userId)
    {
        var payload = new MicrosoftOAuthState(
            userId,
            DateTimeOffset.UtcNow,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
        return _stateProtector.Protect(JsonSerializer.Serialize(payload));
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
}
