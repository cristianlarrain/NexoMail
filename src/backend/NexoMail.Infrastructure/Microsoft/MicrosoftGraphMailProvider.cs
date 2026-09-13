using System.Net.Http.Json;
using System.Text.Json;
using NexoMail.Application;
using NexoMail.Domain;

namespace NexoMail.Infrastructure.Microsoft;

public sealed class MicrosoftGraphMailProvider(MicrosoftGraphClientFactory clientFactory) : IMailProvider
{
    public MailProviderType ProviderType => MailProviderType.MicrosoftGraph;

    public async Task<PagedResult<MailSummary>> GetMessagesAsync(MailQuery query, CancellationToken ct)
    {
        if (!query.AccountId.HasValue) return new PagedResult<MailSummary>([]);
        var client = await clientFactory.CreateAsync(query.AccountId.Value, ct);
        var url = BuildListUrl(query);
        using var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var items = document.RootElement.TryGetProperty("value", out var value)
            ? value.EnumerateArray().Select(item => ParseSummary(item, query.AccountId.Value, query.FolderId)).ToArray()
            : [];
        var next = document.RootElement.TryGetProperty("@odata.nextLink", out var nextElement) ? nextElement.GetString() : null;
        return new PagedResult<MailSummary>(items, next);
    }

    public async Task<MailMessage?> GetMessageAsync(Guid accountId, string messageId, CancellationToken ct)
    {
        var client = await clientFactory.CreateAsync(accountId, ct);
        var select = "$select=id,subject,body,bodyPreview,receivedDateTime,isRead,hasAttachments,from,toRecipients,ccRecipients,parentFolderId,conversationId,internetMessageHeaders";
        using var response = await client.GetAsync($"me/messages/{Escape(messageId)}?{select}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var attachments = await GetAttachmentMetadataAsync(client, messageId, ct);
        return ParseMessage(document.RootElement, accountId, attachments);
    }

    public async Task<IReadOnlyCollection<MailThreadMessage>> GetThreadAsync(Guid accountId, string messageId, CancellationToken ct)
    {
        var client = await clientFactory.CreateAsync(accountId, ct);
        using var seedResponse = await client.GetAsync($"me/messages/{Escape(messageId)}?$select=conversationId", ct);
        if (!seedResponse.IsSuccessStatusCode) return [];
        using var seed = JsonDocument.Parse(await seedResponse.Content.ReadAsStreamAsync(ct));
        var conversationId = seed.RootElement.TryGetProperty("conversationId", out var conversation) ? conversation.GetString() : null;
        if (string.IsNullOrWhiteSpace(conversationId)) return [];

        var filter = Uri.EscapeDataString($"conversationId eq '{conversationId.Replace("'", "''")}'");
        using var response = await client.GetAsync($"me/messages?$filter={filter}&$top=50&$select=id,from,body,receivedDateTime", ct);
        if (!response.IsSuccessStatusCode) return [];
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        if (!document.RootElement.TryGetProperty("value", out var values)) return [];
        return values.EnumerateArray()
            .Select(item => new MailThreadMessage(
                String(item, "id"),
                ParseGraphAddress(item, "from"),
                item.TryGetProperty("body", out var body) ? String(body, "content") : string.Empty,
                Date(item, "receivedDateTime"),
                string.Equals(String(item, "id"), messageId, StringComparison.Ordinal)))
            .OrderBy(x => x.ReceivedAt)
            .ToArray();
    }

    public async Task<MailAttachmentContent?> GetAttachmentAsync(Guid accountId, string messageId, string attachmentId, CancellationToken ct)
    {
        var client = await clientFactory.CreateAsync(accountId, ct);
        using var response = await client.GetAsync($"me/messages/{Escape(messageId)}/attachments/{Escape(attachmentId)}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var root = document.RootElement;
        var content = root.TryGetProperty("contentBytes", out var bytes) ? bytes.GetString() : null;
        if (string.IsNullOrWhiteSpace(content)) return null;
        return new MailAttachmentContent(
            Convert.FromBase64String(content),
            String(root, "contentType", "application/octet-stream"),
            String(root, "name", "adjunto"));
    }

    public async Task SendAsync(ComposeMessage message, CancellationToken ct)
    {
        var client = await clientFactory.CreateAsync(message.FromAccountId, ct);
        using var response = await client.PostAsJsonAsync("me/sendMail", new
        {
            message = BuildGraphMessage(message),
            saveToSentItems = true
        }, ct);
        response.EnsureSuccessStatusCode();
    }

    public Task ReplyAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken ct) =>
        SendActionAsync(accountId, messageId, "reply", message, ct);

    public Task ReplyAllAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken ct) =>
        SendActionAsync(accountId, messageId, "replyAll", message, ct);

    public async Task ForwardAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken ct)
    {
        var client = await clientFactory.CreateAsync(accountId, ct);
        using var response = await client.PostAsJsonAsync($"me/messages/{Escape(messageId)}/forward", new
        {
            comment = HtmlToPlainText(message.HtmlBody),
            toRecipients = Recipients(message.To)
        }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task MarkReadAsync(Guid accountId, string messageId, bool read, CancellationToken ct)
    {
        var client = await clientFactory.CreateAsync(accountId, ct);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"me/messages/{Escape(messageId)}")
        {
            Content = JsonContent.Create(new { isRead = read })
        };
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public Task MoveToTrashAsync(Guid accountId, string messageId, CancellationToken ct) =>
        MoveToFolderAsync(accountId, messageId, "trash", ct);

    public async Task MoveToFolderAsync(Guid accountId, string messageId, string folderId, CancellationToken ct)
    {
        var client = await clientFactory.CreateAsync(accountId, ct);
        var destination = GraphFolderId(folderId);
        using var response = await client.PostAsJsonAsync($"me/messages/{Escape(messageId)}/move", new { destinationId = destination }, ct);
        response.EnsureSuccessStatusCode();
    }

    public Task EmptyFolderAsync(Guid accountId, string folderId, CancellationToken ct) =>
        Task.FromException(new InvalidOperationException("Durante la marcha blanca, vacía permanentemente esta carpeta desde Microsoft 365."));

    public async Task<IReadOnlyCollection<MailFolder>> GetFoldersAsync(Guid accountId, CancellationToken ct)
    {
        var client = await clientFactory.CreateAsync(accountId, ct);
        var result = new List<MailFolder>
        {
            new("inbox", "Bandeja de entrada", 0),
            new("archive", "Archivados", 0),
            new("sent", "Enviados", 0),
            new("drafts", "Borradores", 0),
            new("spam", "Correo no deseado", 0),
            new("trash", "Papelera", 0)
        };
        using var response = await client.GetAsync("me/mailFolders?$top=100&$select=id,displayName,unreadItemCount", ct);
        if (!response.IsSuccessStatusCode) return result;
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        if (!document.RootElement.TryGetProperty("value", out var values)) return result;
        foreach (var folder in values.EnumerateArray())
        {
            var id = String(folder, "id");
            var display = String(folder, "displayName");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(display) || IsSystemFolderName(display)) continue;
            var unread = folder.TryGetProperty("unreadItemCount", out var count) && count.TryGetInt32(out var value) ? value : 0;
            result.Add(new MailFolder(id, display, unread));
        }
        return result;
    }

    private async Task SendActionAsync(Guid accountId, string messageId, string action, ComposeMessage message, CancellationToken ct)
    {
        var client = await clientFactory.CreateAsync(accountId, ct);
        using var response = await client.PostAsJsonAsync($"me/messages/{Escape(messageId)}/{action}", new
        {
            message = new
            {
                body = new { contentType = "HTML", content = message.HtmlBody },
                toRecipients = Recipients(message.To),
                ccRecipients = Recipients(message.Cc),
                bccRecipients = Recipients(message.Bcc),
                attachments = Attachments(message.Attachments)
            }
        }, ct);
        response.EnsureSuccessStatusCode();
    }

    private static string BuildListUrl(MailQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Cursor) && Uri.TryCreate(query.Cursor, UriKind.Absolute, out _)) return query.Cursor;
        var folder = GraphFolderId(query.FolderId);
        var top = Math.Clamp(query.Take, 1, 50);
        var select = "id,subject,bodyPreview,receivedDateTime,isRead,hasAttachments,from,parentFolderId";
        var url = $"me/mailFolders/{Escape(folder)}/messages?$top={top}&$orderby=receivedDateTime%20desc&$select={select}";
        if (!string.IsNullOrWhiteSpace(query.Search))
            url += "&$search=" + Uri.EscapeDataString($"\"{query.Search.Trim().Replace("\"", string.Empty)}\"");
        return url;
    }

    private static MailSummary ParseSummary(JsonElement item, Guid accountId, string folderId)
    {
        var sender = ParseGraphAddress(item, "from");
        return new MailSummary(
            String(item, "id"), accountId, sender.Name, sender.Address,
            String(item, "subject", "(sin asunto)"), String(item, "bodyPreview"),
            Date(item, "receivedDateTime"), Bool(item, "isRead"), Bool(item, "hasAttachments"), folderId);
    }

    private static MailMessage ParseMessage(JsonElement item, Guid accountId, IReadOnlyCollection<MailAttachment> attachments)
    {
        var sender = ParseGraphAddress(item, "from");
        var body = item.TryGetProperty("body", out var bodyElement) ? String(bodyElement, "content") : string.Empty;
        var unsubscribe = item.TryGetProperty("internetMessageHeaders", out var headers)
            ? headers.EnumerateArray().FirstOrDefault(x => string.Equals(String(x, "name"), "List-Unsubscribe", StringComparison.OrdinalIgnoreCase))
            : default;
        var unsubscribeValue = unsubscribe.ValueKind == JsonValueKind.Object ? String(unsubscribe, "value") : null;
        return new MailMessage(
            String(item, "id"), accountId, sender,
            ParseRecipients(item, "toRecipients"), ParseRecipients(item, "ccRecipients"),
            String(item, "subject", "(sin asunto)"), body, String(item, "bodyPreview"),
            Date(item, "receivedDateTime"), Bool(item, "isRead"), attachments,
            FolderFromParent(String(item, "parentFolderId")), null, SafeHttpUrl(unsubscribeValue));
    }

    private static async Task<IReadOnlyCollection<MailAttachment>> GetAttachmentMetadataAsync(HttpClient client, string messageId, CancellationToken ct)
    {
        using var response = await client.GetAsync($"me/messages/{Escape(messageId)}/attachments?$select=id,name,contentType,size", ct);
        if (!response.IsSuccessStatusCode) return [];
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        if (!document.RootElement.TryGetProperty("value", out var values)) return [];
        return values.EnumerateArray().Select(item => new MailAttachment(
            String(item, "id"), String(item, "name", "adjunto"),
            String(item, "contentType", "application/octet-stream"),
            item.TryGetProperty("size", out var size) && size.TryGetInt64(out var value) ? value : 0)).ToArray();
    }

    private static object BuildGraphMessage(ComposeMessage message) => new
    {
        subject = message.Subject,
        body = new { contentType = "HTML", content = message.HtmlBody },
        toRecipients = Recipients(message.To),
        ccRecipients = Recipients(message.Cc),
        bccRecipients = Recipients(message.Bcc),
        attachments = Attachments(message.Attachments)
    };

    private static object[] Recipients(IEnumerable<string> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => (object)new { emailAddress = new { address = value.Trim() } })
        .ToArray();

    private static object[] Attachments(IReadOnlyCollection<OutgoingAttachment>? values) => (values ?? [])
        .Select(value => (object)new Dictionary<string, object?>
        {
            ["@odata.type"] = "#microsoft.graph.fileAttachment",
            ["name"] = value.Name,
            ["contentType"] = value.ContentType,
            ["contentBytes"] = value.Base64Content
        }).ToArray();

    private static MailAddress ParseGraphAddress(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var wrapper) || !wrapper.TryGetProperty("emailAddress", out var address)) return new("", "");
        return new MailAddress(String(address, "name"), String(address, "address"));
    }

    private static IReadOnlyCollection<MailAddress> ParseRecipients(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array) return [];
        return values.EnumerateArray().Select(value => ParseGraphAddress(new RecipientWrapper(value), "recipient")).ToArray();
    }

    private readonly ref struct RecipientWrapper
    {
        private readonly JsonElement _value;
        public RecipientWrapper(JsonElement value) => _value = value;
        public static implicit operator JsonElement(RecipientWrapper wrapper)
        {
            using var document = JsonDocument.Parse($"{{\"recipient\":{wrapper._value.GetRawText()}}}");
            return document.RootElement.Clone();
        }
    }

    private static string GraphFolderId(string folder) => folder.ToLowerInvariant() switch
    {
        "inbox" => "inbox",
        "archive" => "archive",
        "sent" => "sentitems",
        "drafts" => "drafts",
        "spam" => "junkemail",
        "trash" => "deleteditems",
        _ => folder
    };

    private static string FolderFromParent(string parent) => parent;
    private static bool IsSystemFolderName(string name) => new[] { "Inbox", "Archive", "Sent Items", "Drafts", "Junk Email", "Deleted Items" }.Contains(name, StringComparer.OrdinalIgnoreCase);
    private static string Escape(string value) => Uri.EscapeDataString(value);
    private static string String(JsonElement element, string name, string fallback = "") => element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() ?? fallback : fallback;
    private static bool Bool(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static DateTimeOffset Date(JsonElement element, string name) => element.TryGetProperty(name, out var value) && DateTimeOffset.TryParse(value.GetString(), out var parsed) ? parsed : DateTimeOffset.UtcNow;
    private static string? SafeHttpUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var candidate = value.Trim().Trim('<', '>').Split(',')[0].Trim().Trim('<', '>');
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.ToString() : null;
    }
    private static string HtmlToPlainText(string html) => System.Text.RegularExpressions.Regex.Replace(html ?? string.Empty, "<[^>]+>", " ").Replace("&nbsp;", " ").Trim();
}
