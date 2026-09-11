using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;

namespace NexoMail.Infrastructure.Microsoft;

public sealed class MicrosoftGraphTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<MicrosoftGraphOptions> options,
    NexoMailDbContext database,
    ITokenProtector tokenProtector)
{
    private readonly MicrosoftGraphOptions _options = options.Value;
    private readonly ConcurrentDictionary<Guid, CachedAccessToken> _cache = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    public async Task<string> GetAccessTokenAsync(Guid mailAccountId, CancellationToken cancellationToken)
    {
        var credential = await LoadCredentialAsync(mailAccountId, cancellationToken);
        if (TryGetCached(mailAccountId, credential.UpdatedAt, out var cachedToken))
            return cachedToken;

        var gate = _gates.GetOrAdd(mailAccountId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            credential = await LoadCredentialAsync(mailAccountId, cancellationToken);
            if (TryGetCached(mailAccountId, credential.UpdatedAt, out cachedToken))
                return cachedToken;

            EnsureConfigured();
            var refreshToken = tokenProtector.Unprotect(credential.EncryptedRefreshToken);
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new InvalidOperationException("La cuenta Microsoft 365 no tiene una credencial de renovación válida.");

            var client = httpClientFactory.CreateClient();
            using var response = await client.PostAsync(
                MicrosoftOAuthService.TokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _options.ClientId,
                    ["client_secret"] = _options.ClientSecret,
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                    ["scope"] = MicrosoftOAuthService.Phase1Scopes
                }),
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var refreshed = await response.Content.ReadFromJsonAsync<MicrosoftTokenResponse>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Microsoft no entregó un token válido.");
            if (string.IsNullOrWhiteSpace(refreshed.AccessToken))
                throw new InvalidOperationException("Microsoft no entregó un access token válido.");

            var now = DateTimeOffset.UtcNow;
            var trackedCredential = await database.OAuthCredentials.SingleAsync(
                x => x.MailAccountId == mailAccountId,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
                trackedCredential.EncryptedRefreshToken = tokenProtector.Protect(refreshed.RefreshToken);
            trackedCredential.ExpiresAt = now.AddSeconds(Math.Max(refreshed.ExpiresIn, 60));
            trackedCredential.UpdatedAt = now;
            await database.SaveChangesAsync(cancellationToken);

            var cached = new CachedAccessToken(
                refreshed.AccessToken,
                trackedCredential.ExpiresAt.Value,
                trackedCredential.UpdatedAt);
            _cache[mailAccountId] = cached;
            return cached.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<OAuthCredentialEntity> LoadCredentialAsync(Guid mailAccountId, CancellationToken cancellationToken)
    {
        var account = await database.MailAccounts.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == mailAccountId,
            cancellationToken);
        if (account is null || account.Provider != MailProviderType.MicrosoftGraph || !account.IsActive)
            throw new InvalidOperationException("La cuenta Microsoft 365 no está disponible.");

        return await database.OAuthCredentials.AsNoTracking().SingleOrDefaultAsync(
                x => x.MailAccountId == mailAccountId,
                cancellationToken)
            ?? throw new InvalidOperationException("La cuenta Microsoft 365 no tiene credenciales disponibles.");
    }

    private bool TryGetCached(Guid mailAccountId, DateTimeOffset credentialUpdatedAt, out string accessToken)
    {
        if (_cache.TryGetValue(mailAccountId, out var cached) &&
            cached.CredentialUpdatedAt == credentialUpdatedAt &&
            cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            accessToken = cached.AccessToken;
            return true;
        }

        accessToken = string.Empty;
        return false;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new InvalidOperationException("Faltan las credenciales Microsoft en la configuración segura del servidor.");
    }

    private sealed record CachedAccessToken(
        string AccessToken,
        DateTimeOffset ExpiresAt,
        DateTimeOffset CredentialUpdatedAt);

    private sealed record MicrosoftTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
