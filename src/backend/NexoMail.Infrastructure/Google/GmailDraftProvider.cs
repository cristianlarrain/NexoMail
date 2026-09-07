using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Google;

/// <summary>Creates provider-resident Gmail drafts. Draft content is never persisted by NexoMail.</summary>
public sealed class GmailDraftProvider(
    IHttpClientFactory httpClientFactory,
    NexoMailDbContext database,
    ITokenProtector tokenProtector,
    IOptions<GmailOptions> options,
    IUserContext userContext) : IMailDraftProvider
{
    public MailProviderType ProviderType => MailProviderType.Gmail;

    public async Task SaveDraftAsync(Guid accountId, string? replyToMessageId, ComposeMessage message, CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(accountId, cancellationToken);
        string raw;
        string? threadId = null;

        if (!string.IsNullOrWhiteSpace(replyToMessageId))
        {
            using var originalResponse = await client.GetAsync(
                $"users/me/messages/{Uri.EscapeDataString(replyToMessageId)}?format=metadata&metadataHeaders=Message-ID&metadataHeaders=References",
                cancellationToken);
            if (originalResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new InvalidOperationException("El correo original ya no está disponible para guardar esta respuesta como borrador.");
            originalResponse.EnsureSuccessStatusCode();
            using var original = JsonDocument.Parse(await originalResponse.Content.ReadAsStreamAsync(cancellationToken));
            var headers = Headers(original.RootElement);
            var inReplyTo = Header(headers, "Message-ID");
            var references = Header(headers, "References");
            raw = BuildRfc822(
                message with { FromAccountId = accountId },
                inReplyTo,
                string.IsNullOrWhiteSpace(references) ? inReplyTo : $"{references} {inReplyTo}");
            threadId = original.RootElement.TryGetProperty("threadId", out var thread) ? thread.GetString() : null;
        }
        else
        {
            raw = BuildRfc822(message with { FromAccountId = accountId });
        }

        var encodedRaw = ToBase64Url(Encoding.UTF8.GetBytes(raw));
        HttpResponseMessage response;
        if (string.IsNullOrWhiteSpace(threadId))
        {
            response = await client.PostAsJsonAsync("users/me/drafts", new
            {
                message = new { raw = encodedRaw }
            }, cancellationToken);
        }
        else
        {
            response = await client.PostAsJsonAsync("users/me/drafts", new
            {
                message = new { raw = encodedRaw, threadId }
            }, cancellationToken);
        }

        using (response)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private async Task<HttpClient> CreateClientAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        var credential = await database.OAuthCredentials
            .AsNoTracking()
            .Where(x => x.MailAccountId == accountId)
            .Join(
                database.MailAccounts.AsNoTracking().Where(x => x.UserId == userId && x.IsActive && x.Provider == MailProviderType.Gmail),
                credential => credential.MailAccountId,
                account => account.Id,
                (credential, _) => credential)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No existe una credencial OAuth válida para esta cuenta Gmail.");

        var refreshToken = tokenProtector.Unprotect(credential.EncryptedRefreshToken);
        var tokenClient = httpClientFactory.CreateClient();
        using var tokenResponse = await tokenClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = options.Value.ClientId,
            ["client_secret"] = options.Value.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        }), cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenDocument = JsonDocument.Parse(await tokenResponse.Content.ReadAsStreamAsync(cancellationToken));
        var accessToken = tokenDocument.RootElement.TryGetProperty("access_token", out var token) ? token.GetString() : null;
        if (string.IsNullOrWhiteSpace(accessToken)) throw new InvalidOperationException("Google no entregó un token de acceso válido.");

        var client = httpClientFactory.CreateClient("Gmail");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static string BuildRfc822(ComposeMessage message, string? inReplyTo = null, string? references = null)
    {
        var attachments = message.Attachments ?? [];
        var headers = $"To: {string.Join(", ", message.To)}\r\n"
            + (message.Cc.Count > 0 ? $"Cc: {string.Join(", ", message.Cc)}\r\n" : string.Empty)
            + (message.Bcc.Count > 0 ? $"Bcc: {string.Join(", ", message.Bcc)}\r\n" : string.Empty)
            + $"Subject: {EncodeHeader(message.Subject)}\r\n"
            + (!string.IsNullOrWhiteSpace(inReplyTo) ? $"In-Reply-To: {inReplyTo}\r\n" : string.Empty)
            + (!string.IsNullOrWhiteSpace(references) ? $"References: {references}\r\n" : string.Empty)
            + "MIME-Version: 1.0\r\n";

        if (attachments.Count == 0)
            return headers + $"Content-Type: text/html; charset=utf-8\r\n\r\n{message.HtmlBody}";

        var boundary = "nexomail_" + Guid.NewGuid().ToString("N");
        var builder = new StringBuilder(headers)
            .Append($"Content-Type: multipart/mixed; boundary=\"{boundary}\"\r\n\r\n--{boundary}\r\nContent-Type: text/html; charset=utf-8\r\n\r\n{message.HtmlBody}\r\n");

        foreach (var attachment in attachments)
        {
            var safeName = attachment.Name.Replace("\"", "'").Replace("\r", string.Empty).Replace("\n", string.Empty);
            builder.Append($"--{boundary}\r\nContent-Type: {attachment.ContentType}; name=\"{safeName}\"\r\nContent-Transfer-Encoding: base64\r\nContent-Disposition: attachment; filename=\"{safeName}\"\r\n\r\n");
            for (var offset = 0; offset < attachment.Base64Content.Length; offset += 76)
                builder.Append(attachment.Base64Content.AsSpan(offset, Math.Min(76, attachment.Base64Content.Length - offset))).Append("\r\n");
        }

        return builder.Append($"--{boundary}--\r\n").ToString();
    }

    private static Dictionary<string, string> Headers(JsonElement root) => root
        .GetProperty("payload")
        .GetProperty("headers")
        .EnumerateArray()
        .Where(x => x.TryGetProperty("name", out _) && x.TryGetProperty("value", out _))
        .GroupBy(x => x.GetProperty("name").GetString()!, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.Last().GetProperty("value").GetString()!, StringComparer.OrdinalIgnoreCase);

    private static string Header(Dictionary<string, string> headers, string name) => headers.TryGetValue(name, out var value) ? value : string.Empty;
    private static string EncodeHeader(string value) => value.All(character => character <= 127) ? value : $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?=";
    private static string ToBase64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
