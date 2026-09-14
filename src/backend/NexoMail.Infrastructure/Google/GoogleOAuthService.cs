using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    IUserContext userContext,
    GmailMetadataIndexService? metadataIndexService = null,
    ILogger<GoogleOAuthService>? logger = null)
{
    private readonly GmailOptions _options = options.Value;
    private readonly IDataProtector _stateProtector = dataProtectionProvider.CreateProtector("NexoMail.GoogleOAuth.State.v1");

    public async Task EnsureCanConnectAnotherAccountAsync(CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        var access = await NexoMail.Infrastructure.CommercialAccessStore.GetAsync(database, userId, cancellationToken)
            ?? throw new InvalidOperationException("No fue posible determinar el plan de la cuenta.");
        var plan = access.EffectivePlan;
        if (!plan.MaxAccounts.HasValue) return;

        var connectedAccounts = await database.MailAccounts.AsNoTracking()
            .CountAsync(x => x.UserId == userId && x.IsActive, cancellationToken);
        if (connectedAccounts >= plan.MaxAccounts.Value)
            throw new InvalidOperationException($"Su plan efectivo {plan.Name} permite hasta {plan.MaxAccounts.Value} cuentas de correo. Cambie de plan o regularice su suscripción para conectar una cuenta adicional.");
    }

    public string BeginAuthorization()
    {
        EnsureConfigured();
        return BuildAuthorizationUrl(CreateState(userContext.UserId, null));
    }

    public async Task<string> BeginReauthorizationAsync(Guid accountId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var userId = userContext.UserId;
        var accountExists = await database.MailAccounts.AsNoTracking().AnyAsync(
            x => x.Id == accountId && x.UserId == userId && x.Provider == MailProviderType.Gmail,
            cancellationToken);
        if (!accountExists)
            throw new InvalidOperationException("La cuenta Gmail que deseas reconectar no existe o no corresponde al usuario actual.");

        return BuildAuthorizationUrl(CreateState(userId, accountId));
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
        HttpResponseMessage response;
        try
        {
            response = await tokenClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code, ["client_id"] = _options.ClientId, ["client_secret"] = _options.ClientSecret,
                ["redirect_uri"] = _options.RedirectUri, ["grant_type"] = "authorization_code"
            }), cancellationToken);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Google OAuth: tiempo de espera agotado al conectar con oauth2.googleapis.com:443.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException($"Google OAuth: fallo de red al conectar con oauth2.googleapis.com:443 ({DescribeNetworkFailure(exception)}).", exception);
        }
        using var tokenResponse = response;
        if (!response.IsSuccessStatusCode)
        {
            var providerError = await ReadProviderErrorAsync(response, cancellationToken);
            throw new InvalidOperationException($"Google rechazó el intercambio OAuth: HTTP {(int)response.StatusCode} ({providerError}).");
        }
        var token = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Google no entregó un token válido.");
        if (string.IsNullOrWhiteSpace(token.RefreshToken)) throw new InvalidOperationException("Google no entregó un refresh token. Revoca el acceso anterior e inténtalo otra vez.");

        var profileClient = httpClientFactory.CreateClient("Gmail");
        profileClient.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
        HttpResponseMessage profileResponse;
        try
        {
            profileResponse = await profileClient.GetAsync("users/me/profile", cancellationToken);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Gmail API: tiempo de espera agotado al conectar con gmail.googleapis.com:443.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException($"Gmail API: fallo de red ({DescribeNetworkFailure(exception)}).", exception);
        }
        using var gmailProfileResponse = profileResponse;
        if (!profileResponse.IsSuccessStatusCode)
        {
            var providerError = await ReadProviderErrorAsync(profileResponse, cancellationToken);
            throw new InvalidOperationException($"Gmail API rechazó la consulta del perfil: HTTP {(int)profileResponse.StatusCode} ({providerError}).");
        }
        using var profileDocument = JsonDocument.Parse(await profileResponse.Content.ReadAsStreamAsync(cancellationToken));
        var email = profileDocument.RootElement.GetProperty("emailAddress").GetString()
            ?? throw new InvalidOperationException("No fue posible determinar la dirección Gmail.");

        var userId = userContext.UserId;
        MailAccountEntity account;
        if (stateData.AccountId is Guid reconnectAccountId)
        {
            account = await database.MailAccounts.SingleOrDefaultAsync(
                x => x.Id == reconnectAccountId && x.UserId == userId && x.Provider == MailProviderType.Gmail,
                cancellationToken)
                ?? throw new InvalidOperationException("La cuenta Gmail que deseas reconectar ya no existe o no corresponde al usuario actual.");

            if (!string.Equals(account.EmailAddress, email, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Google autorizó {email}, pero debes seleccionar la misma cuenta que estás reconectando ({account.EmailAddress}).");

            account.IsActive = true;
        }
        else
        {
            var existingAccount = await database.MailAccounts.SingleOrDefaultAsync(
                x => x.UserId == userId && x.EmailAddress == email && x.Provider == MailProviderType.Gmail,
                cancellationToken);
            if (existingAccount is null)
            {
                await EnsureCanConnectAnotherAccountAsync(cancellationToken);
                var usedColors = await database.MailAccounts.AsNoTracking()
                    .Where(x => x.UserId == userId && x.IsActive)
                    .Select(x => x.Color)
                    .ToArrayAsync(cancellationToken);
                account = new MailAccountEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Provider = MailProviderType.Gmail,
                    EmailAddress = email,
                    DisplayName = "Gmail",
                    Color = AccountColorSelector.Select(usedColors),
                    CreatedAt = DateTimeOffset.UtcNow,
                    IsActive = true
                };
                database.MailAccounts.Add(account);
            }
            else
            {
                account = existingAccount;
                account.IsActive = true;
            }
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

        var indexState = await database.MailIndexStates.SingleOrDefaultAsync(x => x.AccountId == account.Id, cancellationToken);
        if (indexState is not null)
        {
            indexState.LastSyncErrorCode = null;
            indexState.LastSyncAttemptAt = DateTimeOffset.UtcNow;
        }

        await database.SaveChangesAsync(cancellationToken);

        if (metadataIndexService is not null)
        {
            try
            {
                await metadataIndexService.SyncAsync(null, null, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger?.LogWarning(
                    exception,
                    "La autorización Google de la cuenta {AccountId} se completó, pero no fue posible actualizar inmediatamente el índice Gmail.",
                    account.Id);
            }
        }
    }

    public string SuccessRedirect() => _options.FrontendUrl + "?connected=google";
    public string FailureRedirect(string reason) => _options.FrontendUrl + "?error=" + Uri.EscapeDataString(reason);

    private string BuildAuthorizationUrl(string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email https://www.googleapis.com/auth/gmail.modify https://www.googleapis.com/auth/gmail.send https://www.googleapis.com/auth/gmail.settings.basic https://www.googleapis.com/auth/contacts.readonly https://www.googleapis.com/auth/contacts.other.readonly",
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state
        };
        return "https://accounts.google.com/o/oauth2/v2/auth?" + string.Join("&", query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }

    private string CreateState(Guid userId, Guid? accountId)
    {
        var payload = new GoogleOAuthState(userId, DateTimeOffset.UtcNow, Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)), accountId);
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

    private static string DescribeNetworkFailure(Exception exception)
    {
        var root = exception.GetBaseException();
        return root switch
        {
            System.Net.Sockets.SocketException socketException => $"SocketError={socketException.SocketErrorCode}",
            System.Security.Authentication.AuthenticationException => "TLS",
            _ => root.GetType().Name
        };
    }

    private static async Task<string> ReadProviderErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                    return NormalizeProviderText(error.GetString());
                if (error.ValueKind == JsonValueKind.Object)
                {
                    if (error.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String)
                        return NormalizeProviderText(status.GetString());
                    if (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                        return NormalizeProviderText(message.GetString());
                }
            }
        }
        catch (JsonException)
        {
        }

        return response.ReasonPhrase ?? "respuesta sin detalle";
    }

    private static string NormalizeProviderText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "respuesta sin detalle";
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 180 ? normalized : normalized[..180];
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new InvalidOperationException("Faltan las credenciales Google en la configuración segura del servidor.");
    }

    private sealed record GoogleOAuthState(Guid UserId, DateTimeOffset IssuedAt, string Nonce, Guid? AccountId = null);
    private sealed record GoogleTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
