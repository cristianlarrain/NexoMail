using System.Collections.Concurrent;
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

public sealed class GmailMailProvider(
    IHttpClientFactory httpClientFactory,
    NexoMailDbContext database,
    ITokenProtector tokenProtector,
    IOptions<GmailOptions> options) : IMailProvider
{
    private const int MaximumConcurrentSummaryRequests = 8;
    private readonly SemaphoreSlim databaseGate = new(1, 1);
    private static readonly ConcurrentDictionary<Guid, CachedAccessToken> AccessTokens = new();
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> TokenGates = new();

    public MailProviderType ProviderType => MailProviderType.Gmail;

    public async Task<PagedResult<MailSummary>> GetMessagesAsync(MailQuery query, CancellationToken cancellationToken)
    {
        if (!query.AccountId.HasValue) return new PagedResult<MailSummary>([]);
        var client = await CreateClientAsync(query.AccountId.Value, cancellationToken);
        var label = FolderLabel(query.FolderId);
        var folderSearch = query.FolderId == "archive" ? "-label:inbox -label:sent -label:drafts -label:spam -label:trash" : string.Empty;
        var combinedSearch = string.Join(' ', new[] { folderSearch, query.Search?.Trim() ?? string.Empty }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var search = string.IsNullOrWhiteSpace(combinedSearch) ? string.Empty : $"&q={Uri.EscapeDataString(combinedSearch)}";
        var page = string.IsNullOrWhiteSpace(query.Cursor) ? string.Empty : $"&pageToken={Uri.EscapeDataString(query.Cursor)}";
        var labelParameter = string.IsNullOrWhiteSpace(label) ? string.Empty : $"labelIds={Uri.EscapeDataString(label)}&";
        var includeSpamTrash = query.FolderId is "spam" or "trash" ? "&includeSpamTrash=true" : string.Empty;
        using var listResponse = await client.GetAsync($"users/me/messages?{labelParameter}maxResults={Math.Clamp(query.Take, 1, 50)}{search}{page}{includeSpamTrash}", cancellationToken);
        listResponse.EnsureSuccessStatusCode();
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStreamAsync(cancellationToken));
        if (!list.RootElement.TryGetProperty("messages", out var messages)) return new PagedResult<MailSummary>([]);

        using var gate = new SemaphoreSlim(MaximumConcurrentSummaryRequests);
        var summaryTasks = messages.EnumerateArray().Select(async item =>
        {
            var id = item.GetProperty("id").GetString() ?? throw new InvalidOperationException("Gmail devolvió un mensaje sin identificador.");
            await gate.WaitAsync(cancellationToken);
            try { return await GetSummaryAsync(client, query.AccountId.Value, id, cancellationToken); }
            finally { gate.Release(); }
        });
        var summaries = await Task.WhenAll(summaryTasks);

        return new PagedResult<MailSummary>(summaries.OrderByDescending(x => x.ReceivedAt).ToArray(), list.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null);
    }

    public async Task<MailMessage?> GetMessageAsync(Guid accountId, string messageId, CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(accountId, cancellationToken);
        using var response = await client.GetAsync($"users/me/messages/{Uri.EscapeDataString(messageId)}?format=full", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var message = ParseMessage(document.RootElement, accountId);
        if (!document.RootElement.TryGetProperty("threadId", out var threadId) || string.IsNullOrWhiteSpace(threadId.GetString())) return message;
        using var threadResponse = await client.GetAsync($"users/me/threads/{Uri.EscapeDataString(threadId.GetString()!)}?format=full", cancellationToken);
        if (!threadResponse.IsSuccessStatusCode) return message;
        using var threadDocument = JsonDocument.Parse(await threadResponse.Content.ReadAsStreamAsync(cancellationToken));
        if (!threadDocument.RootElement.TryGetProperty("messages", out var messages)) return message;
        var thread = messages.EnumerateArray().Select(item => ParseMessage(item, accountId)).OrderBy(item => item.ReceivedAt).Select(item => new MailThreadMessage(item.ProviderMessageId, item.From, item.HtmlBody, item.ReceivedAt, item.ProviderMessageId == messageId)).ToArray();
        return message with { Thread = thread };
    }

    public async Task<MailAttachmentContent?> GetAttachmentAsync(Guid accountId, string messageId, string attachmentId, CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(accountId, cancellationToken);
        using var response = await client.GetAsync($"users/me/messages/{Uri.EscapeDataString(messageId)}/attachments/{Uri.EscapeDataString(attachmentId)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("Gmail no encontró este adjunto. Actualiza el correo e inténtalo nuevamente.");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("data", out var data) || string.IsNullOrWhiteSpace(data.GetString())) return null;
        return new MailAttachmentContent(FromBase64Url(data.GetString()!), "application/octet-stream", "adjunto");
    }

    public async Task SendAsync(ComposeMessage message, CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(message.FromAccountId, cancellationToken);
        var raw = BuildRfc822(message);
        await SendRawAsync(client, raw, null, cancellationToken);
    }

    private async Task SendReplyAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(accountId, cancellationToken);
        using var originalResponse = await client.GetAsync($"users/me/messages/{Uri.EscapeDataString(messageId)}?format=metadata&metadataHeaders=Message-ID&metadataHeaders=References", cancellationToken);
        originalResponse.EnsureSuccessStatusCode();
        using var original = JsonDocument.Parse(await originalResponse.Content.ReadAsStreamAsync(cancellationToken));
        var headers = Headers(original.RootElement); var inReplyTo = Header(headers, "Message-ID"); var references = Header(headers, "References");
        var raw = BuildRfc822(message with { FromAccountId = accountId }, inReplyTo, string.IsNullOrWhiteSpace(references) ? inReplyTo : $"{references} {inReplyTo}");
        await SendRawAsync(client, raw, original.RootElement.TryGetProperty("threadId", out var thread) ? thread.GetString() : null, cancellationToken);
    }

    public Task ReplyAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken) => SendReplyAsync(accountId, messageId, message, cancellationToken);
    public Task ReplyAllAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken) => SendReplyAsync(accountId, messageId, message, cancellationToken);
    public Task ForwardAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken) => SendAsync(message with { FromAccountId = accountId }, cancellationToken);

    public async Task MarkReadAsync(Guid accountId, string messageId, bool read, CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(accountId, cancellationToken);
        var update = read
            ? new Dictionary<string, string[]> { ["removeLabelIds"] = ["UNREAD"] }
            : new Dictionary<string, string[]> { ["addLabelIds"] = ["UNREAD"] };
        using var response = await client.PostAsJsonAsync($"users/me/messages/{Uri.EscapeDataString(messageId)}/modify", update, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task MoveToTrashAsync(Guid accountId, string messageId, CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(accountId, cancellationToken);
        using var response = await client.PostAsync($"users/me/messages/{Uri.EscapeDataString(messageId)}/trash", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task MoveToFolderAsync(Guid accountId, string messageId, string folderId, CancellationToken cancellationToken)
    {
        if (folderId == "trash")
        {
            await MoveToTrashAsync(accountId, messageId, cancellationToken);
            return;
        }

        var client = await CreateClientAsync(accountId, cancellationToken);
        if (folderId == "archive")
        {
            using var response = await client.PostAsJsonAsync($"users/me/messages/{Uri.EscapeDataString(messageId)}/modify", new Dictionary<string, string[]> { ["removeLabelIds"] = ["INBOX"] }, cancellationToken);
            response.EnsureSuccessStatusCode();
            return;
        }
        if (folderId == "spam")
        {
            using var response = await client.PostAsJsonAsync($"users/me/messages/{Uri.EscapeDataString(messageId)}/modify", new Dictionary<string, string[]> { ["addLabelIds"] = ["SPAM"], ["removeLabelIds"] = ["INBOX"] }, cancellationToken);
            response.EnsureSuccessStatusCode();
            return;
        }
        if (folderId != "inbox") throw new InvalidOperationException("NexoMail no reconoce la carpeta de destino solicitada.");

        using var untrash = await client.PostAsync($"users/me/messages/{Uri.EscapeDataString(messageId)}/untrash", null, cancellationToken);
        if (!untrash.IsSuccessStatusCode && untrash.StatusCode != System.Net.HttpStatusCode.BadRequest) untrash.EnsureSuccessStatusCode();
        using var restore = await client.PostAsJsonAsync($"users/me/messages/{Uri.EscapeDataString(messageId)}/modify", new Dictionary<string, string[]> { ["addLabelIds"] = ["INBOX"], ["removeLabelIds"] = ["SPAM"] }, cancellationToken);
        restore.EnsureSuccessStatusCode();
    }

    public Task EmptyFolderAsync(Guid accountId, string folderId, CancellationToken cancellationToken)
    {
        return Task.FromException(new InvalidOperationException(folderId == "trash"
            ? "Gmail exige un permiso adicional para borrar permanentemente. Por seguridad, vacía la Papelera desde Gmail."
            : "Solo se puede vaciar la Papelera."));
    }

    public Task<IReadOnlyCollection<MailFolder>> GetFoldersAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<MailFolder>>([
            new("inbox", "Bandeja de entrada", 0),
            new("archive", "Archivados", 0),
            new("sent", "Enviados", 0),
            new("drafts", "Borradores", 0),
            new("spam", "Spam", 0),
            new("trash", "Papelera", 0)
        ]);

    private async Task<MailSummary> GetSummaryAsync(HttpClient client, Guid accountId, string messageId, CancellationToken cancellationToken)
    {
        const string fields = "id,labelIds,snippet,internalDate,payload(headers,filename,parts(filename,parts(filename)))";
        using var response = await client.GetAsync($"users/me/messages/{Uri.EscapeDataString(messageId)}?format=full&fields={Uri.EscapeDataString(fields)}", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var root = document.RootElement; var headers = Headers(root); var sender = ParseAddress(Header(headers, "From"));
        var received = root.TryGetProperty("internalDate", out var timestamp) && long.TryParse(timestamp.GetString(), out var milliseconds) ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) : DateTimeOffset.UtcNow;
        var labels = root.TryGetProperty("labelIds", out var labelIds) ? labelIds.EnumerateArray().Select(x => x.GetString()).ToHashSet() : [];
        return new MailSummary(messageId, accountId, sender.Name, sender.Address, Header(headers, "Subject", "(sin asunto)"), root.TryGetProperty("snippet", out var snippet) ? FixMojibake(snippet.GetString() ?? string.Empty) : string.Empty, received, !labels.Contains("UNREAD"), HasAttachment(root), FolderFromLabels(labels));
    }

    private static MailMessage ParseMessage(JsonElement root, Guid accountId)
    {
        var headers = Headers(root); var sender = ParseAddress(Header(headers, "From")); var labels = root.TryGetProperty("labelIds", out var labelIds) ? labelIds.EnumerateArray().Select(x => x.GetString()).ToHashSet() : [];
        var received = root.TryGetProperty("internalDate", out var timestamp) && long.TryParse(timestamp.GetString(), out var milliseconds) ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) : DateTimeOffset.UtcNow;
        var (html, attachments) = ParsePayload(root.GetProperty("payload"));
        return new MailMessage(root.GetProperty("id").GetString()!, accountId, sender, ParseAddresses(Header(headers, "To")), ParseAddresses(Header(headers, "Cc")), Header(headers, "Subject", "(sin asunto)"), html, root.TryGetProperty("snippet", out var snippet) ? FixMojibake(snippet.GetString() ?? string.Empty) : string.Empty, received, !labels.Contains("UNREAD"), attachments, FolderFromLabels(labels), null, SafeUnsubscribeUrl(Header(headers, "List-Unsubscribe")));
    }

    private async Task<HttpClient> CreateClientAsync(Guid accountId, CancellationToken cancellationToken)
    {
        CredentialSnapshot credential;
        await databaseGate.WaitAsync(cancellationToken);
        try
        {
            var entity = await database.OAuthCredentials
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.MailAccountId == accountId, cancellationToken)
                ?? throw new InvalidOperationException("No existe una credencial OAuth para esta cuenta.");
            credential = new CredentialSnapshot(entity.EncryptedRefreshToken, entity.UpdatedAt);
        }
        finally
        {
            databaseGate.Release();
        }

        var now = DateTimeOffset.UtcNow;
        if (!AccessTokens.TryGetValue(accountId, out var cached) || cached.CredentialUpdatedAt != credential.UpdatedAt || cached.ExpiresAt <= now.AddMinutes(2))
        {
            var gate = TokenGates.GetOrAdd(accountId, static _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                now = DateTimeOffset.UtcNow;
                if (!AccessTokens.TryGetValue(accountId, out cached) || cached.CredentialUpdatedAt != credential.UpdatedAt || cached.ExpiresAt <= now.AddMinutes(2))
                {
                    var optionsValue = options.Value;
                    var refreshToken = tokenProtector.Unprotect(credential.EncryptedRefreshToken);
                    var tokenClient = httpClientFactory.CreateClient();
                    using var response = await tokenClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = optionsValue.ClientId,
                        ["client_secret"] = optionsValue.ClientSecret,
                        ["refresh_token"] = refreshToken,
                        ["grant_type"] = "refresh_token"
                    }), cancellationToken);
                    response.EnsureSuccessStatusCode();
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
                    var accessToken = document.RootElement.TryGetProperty("access_token", out var tokenElement) ? tokenElement.GetString() : null;
                    if (string.IsNullOrWhiteSpace(accessToken)) throw new InvalidOperationException("Google no entregó un token de acceso válido.");
                    var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiresElement) && expiresElement.TryGetInt32(out var seconds) ? seconds : 3600;
                    cached = new CachedAccessToken(accessToken, now.AddSeconds(Math.Max(60, expiresIn - 120)), credential.UpdatedAt);
                    AccessTokens[accountId] = cached;
                }
            }
            finally
            {
                gate.Release();
            }
        }

        if (!AccessTokens.TryGetValue(accountId, out var finalToken)) throw new InvalidOperationException("No fue posible obtener el token de acceso de Google.");
        var client = httpClientFactory.CreateClient("Gmail");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", finalToken.AccessToken);
        return client;
    }

    private static async Task SendRawAsync(HttpClient client, string raw, string? threadId, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("users/me/messages/send", new { raw = ToBase64Url(Encoding.UTF8.GetBytes(raw)), threadId }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static string BuildRfc822(ComposeMessage message, string? inReplyTo = null, string? references = null)
    {
        var attachments = message.Attachments ?? [];
        var headers = $"To: {string.Join(", ", message.To)}\r\n" + (message.Cc.Count > 0 ? $"Cc: {string.Join(", ", message.Cc)}\r\n" : string.Empty) + (message.Bcc.Count > 0 ? $"Bcc: {string.Join(", ", message.Bcc)}\r\n" : string.Empty) + $"Subject: {EncodeHeader(message.Subject)}\r\n" + (!string.IsNullOrWhiteSpace(inReplyTo) ? $"In-Reply-To: {inReplyTo}\r\n" : string.Empty) + (!string.IsNullOrWhiteSpace(references) ? $"References: {references}\r\n" : string.Empty) + "MIME-Version: 1.0\r\n";
        if (attachments.Count == 0) return headers + $"Content-Type: text/html; charset=utf-8\r\n\r\n{message.HtmlBody}";

        var boundary = "nexomail_" + Guid.NewGuid().ToString("N");
        var builder = new StringBuilder(headers).Append($"Content-Type: multipart/mixed; boundary=\"{boundary}\"\r\n\r\n--{boundary}\r\nContent-Type: text/html; charset=utf-8\r\n\r\n{message.HtmlBody}\r\n");
        foreach (var attachment in attachments)
        {
            var safeName = attachment.Name.Replace("\"", "'").Replace("\r", string.Empty).Replace("\n", string.Empty);
            builder.Append($"--{boundary}\r\nContent-Type: {attachment.ContentType}; name=\"{safeName}\"\r\nContent-Transfer-Encoding: base64\r\nContent-Disposition: attachment; filename=\"{safeName}\"\r\n\r\n");
            var base64 = attachment.Base64Content;
            for (var offset = 0; offset < base64.Length; offset += 76) builder.Append(base64.AsSpan(offset, Math.Min(76, base64.Length - offset))).Append("\r\n");
        }
        return builder.Append($"--{boundary}--\r\n").ToString();
    }

    private static string EncodeHeader(string value) => value.All(character => character <= 127) ? value : $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?=";
    private static string? FolderLabel(string folder) => folder switch { "sent" => "SENT", "drafts" => "DRAFT", "trash" => "TRASH", "spam" => "SPAM", "archive" => null, _ => "INBOX" };
    private static string FolderFromLabels(HashSet<string?> labels) => labels.Contains("SENT") ? "sent" : labels.Contains("DRAFT") ? "drafts" : labels.Contains("TRASH") ? "trash" : labels.Contains("SPAM") ? "spam" : labels.Contains("INBOX") ? "inbox" : "archive";
    private static Dictionary<string, string> Headers(JsonElement root) => root.GetProperty("payload").GetProperty("headers").EnumerateArray()
        .Where(x => x.TryGetProperty("name", out _) && x.TryGetProperty("value", out _))
        .GroupBy(x => x.GetProperty("name").GetString()!, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.Last().GetProperty("value").GetString()!, StringComparer.OrdinalIgnoreCase);
    private static string Header(Dictionary<string, string> headers, string name, string fallback = "") => FixMojibake(headers.TryGetValue(name, out var value) ? value : fallback);
    private static MailAddress ParseAddress(string raw) { var match = System.Text.RegularExpressions.Regex.Match(raw, "^(?<name>.*?)\\s*<(?<address>[^>]+)>$"); return match.Success ? new MailAddress(match.Groups["name"].Value.Trim(' ', '\"'), match.Groups["address"].Value) : new MailAddress(raw, raw); }
    private static IReadOnlyCollection<MailAddress> ParseAddresses(string raw) => string.IsNullOrWhiteSpace(raw) ? [] : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(ParseAddress).ToArray();
    private static string? SafeUnsubscribeUrl(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(raw, @"https://[^>\s,]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success || !Uri.TryCreate(match.Value, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return null;
        return uri.AbsoluteUri;
    }
    private static bool HasAttachment(JsonElement root) => root.TryGetProperty("payload", out var payload) && PayloadHasAttachment(payload);
    private static bool PayloadHasAttachment(JsonElement payload) => payload.TryGetProperty("filename", out var filename) && !string.IsNullOrWhiteSpace(filename.GetString()) || payload.TryGetProperty("parts", out var parts) && parts.EnumerateArray().Any(PayloadHasAttachment);

    private static (string Html, IReadOnlyCollection<MailAttachment> Attachments) ParsePayload(JsonElement payload)
    {
        var attachments = new List<MailAttachment>(); var inlineImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); string? html = null; string? plain = null;
        Walk(payload);
        var body = html ?? (plain is null ? string.Empty : $"<pre>{System.Net.WebUtility.HtmlEncode(plain)}</pre>");
        foreach (var image in inlineImages) body = body.Replace($"cid:{image.Key}", image.Value, StringComparison.OrdinalIgnoreCase);
        return (body, attachments);
        void Walk(JsonElement part)
        {
            var mime = part.TryGetProperty("mimeType", out var mimeType) ? mimeType.GetString() : null;
            var filename = part.TryGetProperty("filename", out var file) ? file.GetString() : null;
            if (!string.IsNullOrWhiteSpace(filename) && part.TryGetProperty("body", out var fileBody) && fileBody.TryGetProperty("attachmentId", out var attachmentId)) attachments.Add(new MailAttachment(attachmentId.GetString()!, filename, mime ?? "application/octet-stream", fileBody.TryGetProperty("size", out var size) ? size.GetInt64() : 0));
            if (part.TryGetProperty("body", out var body) && body.TryGetProperty("data", out var data) && !string.IsNullOrWhiteSpace(data.GetString()))
            {
                var raw = data.GetString()!;
                if (mime == "text/html") html = FixMojibake(Encoding.UTF8.GetString(FromBase64Url(raw)));
                else if (mime == "text/plain") plain = FixMojibake(Encoding.UTF8.GetString(FromBase64Url(raw)));
                else if (mime?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true && TryContentId(part, out var contentId)) inlineImages[contentId] = $"data:{mime};base64,{Convert.ToBase64String(FromBase64Url(raw))}";
            }
            if (part.TryGetProperty("parts", out var parts)) foreach (var child in parts.EnumerateArray()) Walk(child);
        }
    }

    private static bool TryContentId(JsonElement part, out string contentId)
    {
        contentId = string.Empty;
        if (!part.TryGetProperty("headers", out var headers)) return false;
        var header = headers.EnumerateArray().FirstOrDefault(x => x.TryGetProperty("name", out var name) && string.Equals(name.GetString(), "Content-ID", StringComparison.OrdinalIgnoreCase));
        if (!header.TryGetProperty("value", out var value) || string.IsNullOrWhiteSpace(value.GetString())) return false;
        contentId = value.GetString()!.Trim().Trim('<', '>');
        return contentId.Length > 0;
    }

    private static string FixMojibake(string value)
    {
        for (var pass = 0; pass < 2 && (value.Contains('Ã') || value.Contains('Â')); pass++)
        {
            var decoded = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(value));
            if (decoded.Contains('\uFFFD')) break;
            value = decoded;
        }
        return value;
    }

    private static string ToBase64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromBase64Url(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));

    private sealed record CredentialSnapshot(string EncryptedRefreshToken, DateTimeOffset UpdatedAt);
    private sealed record CachedAccessToken(string AccessToken, DateTimeOffset ExpiresAt, DateTimeOffset CredentialUpdatedAt);
}
