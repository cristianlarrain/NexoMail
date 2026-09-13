using System.Net.Http.Json;
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
    IOptions<Microsoft365Options> options,
    NexoMailDbContext database,
    ITokenProtector tokenProtector,
    IDataProtectionProvider dataProtectionProvider,
    IUserContext userContext)
{
    public const string Scope = "openid profile email offline_access User.Read Mail.ReadWrite Mail.Send";
    private readonly Microsoft365Options _options = options.Value;
    private readonly IDataProtector _stateProtector = dataProtectionProvider.CreateProtector("NexoMail.MicrosoftOAuth.State.v1");

    public async Task EnsureCanConnectAnotherAccountAsync(CancellationToken ct)
    {
        var access = await CommercialAccessStore.GetAsync(database, userContext.UserId, ct)
            ?? throw new InvalidOperationException("No fue posible determinar el plan de la cuenta.");
        if (!access.EffectivePlan.MaxAccounts.HasValue) return;
        var connected = await database.MailAccounts.AsNoTracking()
            .CountAsync(x => x.UserId == userContext.UserId && x.IsActive, ct);
        if (connected >= access.EffectivePlan.MaxAccounts.Value)
            throw new InvalidOperationException($"Su plan efectivo {access.EffectivePlan.Name} permite hasta {access.EffectivePlan.MaxAccounts.Value} cuentas de correo.");
    }

    public string BeginAuthorization()
    {
        EnsureConfigured();
        var tenant = NormalizeTenant(_options.AuthorityTenant);
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = _options.RedirectUri,
            ["response_mode"] = "query",
            ["scope"] = Scope,
            ["state"] = CreateState(userContext.UserId),
            ["prompt"] = "select_account"
        };
        return $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize?" +
            string.Join("&", query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }

    public async Task CompleteAuthorizationAsync(string code, string state, CancellationToken ct)
    {
        EnsureConfigured();
        var stateData = ReadState(state);
        if (stateData.UserId != userContext.UserId)
            throw new InvalidOperationException("La autorización de Microsoft no corresponde al usuario que inició sesión.");
        if (DateTimeOffset.UtcNow - stateData.IssuedAt > TimeSpan.FromMinutes(10))
            throw new InvalidOperationException("La solicitud de conexión a Microsoft expiró. Iníciala nuevamente.");

        var tenant = NormalizeTenant(_options.AuthorityTenant);
        var tokenClient = httpClientFactory.CreateClient();
        using var tokenResponse = await tokenClient.PostAsync(
            $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = _options.RedirectUri,
                ["grant_type"] = "authorization_code",
                ["scope"] = Scope
            }), ct);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            var body = await tokenResponse.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(MicrosoftOAuthErrors.ToUserMessage(body));
        }
        var token = await tokenResponse.Content.ReadFromJsonAsync<MicrosoftTokenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Microsoft no entregó un token válido.");
        if (string.IsNullOrWhiteSpace(token.RefreshToken))
            throw new InvalidOperationException("Microsoft no entregó un token de renovación. Inicia nuevamente la conexión.");

        var graph = httpClientFactory.CreateClient();
        graph.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
        graph.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
        using var profileResponse = await graph.GetAsync("me?$select=mail,userPrincipalName,displayName", ct);
        if (!profileResponse.IsSuccessStatusCode)
            throw new InvalidOperationException("Microsoft autorizó la cuenta, pero NexoMail no pudo leer su perfil. Revisa los permisos concedidos.");
        using var profile = JsonDocument.Parse(await profileResponse.Content.ReadAsStreamAsync(ct));
        var root = profile.RootElement;
        var email = root.TryGetProperty("mail", out var mail) && !string.IsNullOrWhiteSpace(mail.GetString())
            ? mail.GetString()!
            : root.TryGetProperty("userPrincipalName", out var upn) ? upn.GetString() : null;
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("No fue posible determinar la dirección de correo de Microsoft 365.");
        var displayName = root.TryGetProperty("displayName", out var name) ? name.GetString() : null;

        var userId = userContext.UserId;
        var conflicting = await database.MailAccounts.SingleOrDefaultAsync(
            x => x.UserId == userId && x.EmailAddress == email && x.Provider != MailProviderType.MicrosoftGraph, ct);
        if (conflicting is not null)
            throw new InvalidOperationException("Esta dirección ya está conectada con otro tipo de proveedor en NexoMail.");

        var account = await database.MailAccounts.SingleOrDefaultAsync(
            x => x.UserId == userId && x.EmailAddress == email && x.Provider == MailProviderType.MicrosoftGraph, ct);
        if (account is null)
        {
            await EnsureCanConnectAnotherAccountAsync(ct);
            account = new MailAccountEntity
            {
                Id = Guid.NewGuid(), UserId = userId, Provider = MailProviderType.MicrosoftGraph,
                EmailAddress = email, DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Microsoft 365" : displayName!,
                Color = "#2563eb", CreatedAt = DateTimeOffset.UtcNow, IsActive = true
            };
            database.MailAccounts.Add(account);
        }
        else
        {
            account.IsActive = true;
            if (!string.IsNullOrWhiteSpace(displayName) && account.DisplayName == "Microsoft 365") account.DisplayName = displayName!;
        }

        var credential = await database.OAuthCredentials.SingleOrDefaultAsync(x => x.MailAccountId == account.Id, ct);
        if (credential is null)
        {
            credential = new OAuthCredentialEntity { Id = Guid.NewGuid(), MailAccountId = account.Id };
            database.OAuthCredentials.Add(credential);
        }
        credential.EncryptedRefreshToken = tokenProtector.Protect(token.RefreshToken);
        credential.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn));
        credential.UpdatedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(ct);
    }

    public string SuccessRedirect() => _options.FrontendUrl + "?connected=microsoft";
    public string FailureRedirect(string reason) => _options.FrontendUrl + "?error=" + Uri.EscapeDataString(reason);

    private string CreateState(Guid userId)
    {
        var payload = new MicrosoftOAuthState(userId, DateTimeOffset.UtcNow, Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)));
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
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new InvalidOperationException("Faltan las credenciales de Microsoft 365 en la configuración segura del servidor.");
    }

    internal static string NormalizeTenant(string? tenant) => string.IsNullOrWhiteSpace(tenant) ? "organizations" : tenant.Trim();

    private sealed record MicrosoftOAuthState(Guid UserId, DateTimeOffset IssuedAt, string Nonce);
    private sealed record MicrosoftTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

public static class MicrosoftOAuthErrors
{
    public static string ToUserMessage(string? providerMessage)
    {
        var value = providerMessage ?? string.Empty;
        if (value.Contains("AADSTS65001", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("consent", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("admin", StringComparison.OrdinalIgnoreCase))
            return "Tu organización Microsoft 365 requiere autorización del administrador para conectar NexoMail. Solicita la aprobación institucional y vuelve a intentarlo.";
        if (value.Contains("AADSTS50020", StringComparison.OrdinalIgnoreCase))
            return "Esta marcha blanca admite cuentas profesionales, educativas o institucionales de Microsoft 365. Las cuentas Microsoft personales se habilitarán más adelante.";
        return "Microsoft 365 no pudo completar la autorización. Revisa los permisos de la cuenta e inténtalo nuevamente.";
    }
}
