using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Google;

/// <summary>Reads contacts on demand from Google. Contacts are never stored locally.</summary>
public sealed class GoogleContactsService(
    IHttpClientFactory httpClientFactory,
    NexoMailDbContext database,
    ITokenProtector tokenProtector,
    IOptions<GmailOptions> options,
    IUserContext userContext)
{
    public async Task<IReadOnlyCollection<ContactSuggestion>> GetContactsAsync(Guid accountId, string? search, CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        var account = await database.MailAccounts.SingleOrDefaultAsync(x => x.Id == accountId && x.UserId == userId && x.IsActive, cancellationToken);
        if (account is null || account.Provider != MailProviderType.Gmail)
            throw new InvalidOperationException("Selecciona una cuenta Gmail válida para consultar sus contactos.");

        var client = await CreateClientAsync(accountId, cancellationToken);
        var contacts = await GetPeopleAsync(client, "people/me/connections?personFields=names,emailAddresses&pageSize=1000&sortOrder=LAST_NAME_ASCENDING", "connections", cancellationToken);
        var otherContacts = await GetPeopleAsync(client, "otherContacts?readMask=names,emailAddresses&pageSize=1000", "otherContacts", cancellationToken);

        var term = search?.Trim() ?? string.Empty;
        var suggestions = new List<ContactSuggestion>();
        foreach (var person in contacts.Concat(otherContacts))
        {
            var name = GetFirstString(person, "names", "displayName");
            if (!person.TryGetProperty("emailAddresses", out var addresses)) continue;
            foreach (var entry in addresses.EnumerateArray())
            {
                if (!entry.TryGetProperty("value", out var value) || string.IsNullOrWhiteSpace(value.GetString())) continue;
                var email = value.GetString()!.Trim();
                var label = string.IsNullOrWhiteSpace(name) ? email : name;
                if (term.Length > 0 && !label.Contains(term, StringComparison.OrdinalIgnoreCase) && !email.Contains(term, StringComparison.OrdinalIgnoreCase)) continue;
                suggestions.Add(new ContactSuggestion(label, email));
            }
        }

        return suggestions
            .DistinctBy(contact => contact.EmailAddress, StringComparer.OrdinalIgnoreCase)
            .OrderBy(contact => contact.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase) || contact.EmailAddress.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(contact => contact.Name, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    private static async Task<IReadOnlyCollection<JsonElement>> GetPeopleAsync(HttpClient client, string url, string collectionName, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Esta cuenta aún no autorizó el acceso a todos sus contactos. Habilita People API y vuelve a conectar la cuenta Gmail.");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        return document.RootElement.TryGetProperty(collectionName, out var people) ? people.EnumerateArray().Select(person => person.Clone()).ToArray() : [];
    }

    private async Task<HttpClient> CreateClientAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        var credential = await database.OAuthCredentials
            .Where(x => x.MailAccountId == accountId)
            .Join(database.MailAccounts.Where(x => x.UserId == userId && x.IsActive), credential => credential.MailAccountId, account => account.Id, (credential, _) => credential)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No existe una credencial OAuth válida para esta cuenta.");
        var refreshToken = tokenProtector.Unprotect(credential.EncryptedRefreshToken);
        var tokenClient = httpClientFactory.CreateClient();
        using var response = await tokenClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = options.Value.ClientId, ["client_secret"] = options.Value.ClientSecret,
            ["refresh_token"] = refreshToken, ["grant_type"] = "refresh_token"
        }), cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var client = httpClientFactory.CreateClient("GooglePeople");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", document.RootElement.GetProperty("access_token").GetString());
        return client;
    }

    private static string? GetFirstString(JsonElement person, string collection, string field)
    {
        if (!person.TryGetProperty(collection, out var values)) return null;
        foreach (var value in values.EnumerateArray())
            if (value.TryGetProperty(field, out var property) && !string.IsNullOrWhiteSpace(property.GetString())) return property.GetString()?.Trim();
        return null;
    }
}
