using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexoMail.Application;
using NexoMail.Domain;

namespace NexoMail.Infrastructure.Microsoft;

public sealed class MicrosoftGraphMailProvider(
    IHttpClientFactory httpClientFactory,
    MicrosoftGraphTokenProvider tokenProvider) : IMailProvider
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0/";

    public MailProviderType ProviderType => MailProviderType.MicrosoftGraph;

    public async Task<PagedResult<MailSummary>> GetMessagesAsync(MailQuery query, CancellationToken cancellationToken)
    {
        if (!query.AccountId.HasValue)
            return new PagedResult<MailSummary>([]);

        if (!string.Equals(query.FolderId, "inbox", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(query.Search))
        {
            return new PagedResult<MailSummary>([]);
        }

        var requestUrl = string.IsNullOrWhiteSpace(query.Cursor)
            ? BuildInboxUrl(query.Take)
            : MicrosoftGraphCursor.Decode(query.Cursor);

        var accessToken = await tokenProvider.GetAccessTokenAsync(query.AccountId.Value, cancellationToken);
        var client = httpClientFactory.CreateClient("MicrosoftGraph");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        EnsureGraphSuccess(response);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var graphPage = await JsonSerializer.DeserializeAsync<GraphMessagePage>(stream, cancellationToken: cancellationToken)
            ?? new GraphMessagePage();

        var items = graphPage.Value
            .Select(message => new MailSummary(
                message.Id ?? string.Empty,
                query.AccountId.Value,
                message.From?.EmailAddress?.Name ?? string.Empty,
                message.From?.EmailAddress?.Address ?? string.Empty,
                message.Subject ?? string.Empty,
                message.BodyPreview ?? string.Empty,
                message.ReceivedDateTime,
                message.IsRead,
                message.HasAttachments,
                "inbox"))
            .ToArray();

        var nextCursor = string.IsNullOrWhiteSpace(graphPage.NextLink)
            ? null
            : MicrosoftGraphCursor.Encode(graphPage.NextLink);

        return new PagedResult<MailSummary>(items, nextCursor);
    }

    public async Task<MailMessage?> GetMessageAsync(Guid accountId, string messageId, CancellationToken cancellationToken)
    {
        var accessToken = await tokenProvider.GetAccessTokenAsync(accountId, cancellationToken);
        var client = httpClientFactory.CreateClient("MicrosoftGraph");
        var select = Uri.EscapeDataString("id,from,toRecipients,ccRecipients,subject,body,bodyPreview,receivedDateTime,isRead,hasAttachments");
        var requestUrl = $"{GraphBase}me/messages/{Uri.EscapeDataString(messageId)}?$select={select}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        EnsureGraphSuccess(response);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var message = await JsonSerializer.DeserializeAsync<GraphMessage>(stream, cancellationToken: cancellationToken);
        if (message is null || string.IsNullOrWhiteSpace(message.Id))
            return null;

        return new MailMessage(
            message.Id,
            accountId,
            ToMailAddress(message.From),
            message.ToRecipients.Select(ToMailAddress).ToArray(),
            message.CcRecipients.Select(ToMailAddress).ToArray(),
            message.Subject ?? string.Empty,
            ToHtmlBody(message.Body),
            message.BodyPreview ?? string.Empty,
            message.ReceivedDateTime,
            message.IsRead,
            [],
            "inbox");
    }

    public Task<IReadOnlyCollection<MailThreadMessage>> GetThreadAsync(Guid accountId, string messageId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<MailThreadMessage>>([]);

    public Task<MailAttachmentContent?> GetAttachmentAsync(Guid accountId, string messageId, string attachmentId, CancellationToken cancellationToken) =>
        Task.FromException<MailAttachmentContent?>(Unsupported());

    public Task SendAsync(ComposeMessage message, CancellationToken cancellationToken) => Task.FromException(Unsupported());

    public Task ReplyAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken) => Task.FromException(Unsupported());

    public Task ReplyAllAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken) => Task.FromException(Unsupported());

    public Task ForwardAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken) => Task.FromException(Unsupported());

    public async Task MarkReadAsync(Guid accountId, string messageId, bool read, CancellationToken cancellationToken)
    {
        var accessToken = await tokenProvider.GetAccessTokenAsync(accountId, cancellationToken);
        var client = httpClientFactory.CreateClient("MicrosoftGraph");
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{GraphBase}me/messages/{Uri.EscapeDataString(messageId)}")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { isRead = read }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        EnsureGraphSuccess(response);
    }

    public Task MoveToTrashAsync(Guid accountId, string messageId, CancellationToken cancellationToken) => Task.FromException(Unsupported());

    public Task MoveToFolderAsync(Guid accountId, string messageId, string folderId, CancellationToken cancellationToken) => Task.FromException(Unsupported());

    public Task EmptyFolderAsync(Guid accountId, string folderId, CancellationToken cancellationToken) => Task.FromException(Unsupported());

    public Task<IReadOnlyCollection<MailFolder>> GetFoldersAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<MailFolder>>([new MailFolder("inbox", "Bandeja de entrada", 0)]);

    private static string BuildInboxUrl(int take)
    {
        var pageSize = Math.Clamp(take, 1, 50);
        var select = Uri.EscapeDataString("id,from,subject,bodyPreview,receivedDateTime,isRead,hasAttachments");
        var orderBy = Uri.EscapeDataString("receivedDateTime desc");
        return $"{GraphBase}me/mailFolders/inbox/messages?$top={pageSize}&$orderby={orderBy}&$select={select}";
    }

    private static MailAddress ToMailAddress(GraphRecipient? recipient) =>
        new(recipient?.EmailAddress?.Name ?? string.Empty, recipient?.EmailAddress?.Address ?? string.Empty);

    private static string ToHtmlBody(GraphBody? body)
    {
        var content = body?.Content ?? string.Empty;
        if (!string.Equals(body?.ContentType, "text", StringComparison.OrdinalIgnoreCase))
            return content;

        return WebUtility.HtmlEncode(content)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "<br />", StringComparison.Ordinal);
    }

    private static void EnsureGraphSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                "Microsoft 365 rechazó el acceso al buzón. Vuelve a conectar la cuenta o revisa los permisos de la organización.");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new InvalidOperationException(
                "Microsoft 365 está limitando temporalmente las solicitudes. Inténtalo nuevamente en unos minutos.");
        }

        if ((int)response.StatusCode >= 500)
        {
            throw new HttpRequestException(
                "Microsoft 365 no está disponible temporalmente.",
                null,
                response.StatusCode);
        }

        throw new HttpRequestException(
            "Microsoft Graph no pudo completar la operación.",
            null,
            response.StatusCode);
    }

    private static NotSupportedException Unsupported() =>
        new("Esta operación aún no está disponible para Microsoft 365 en esta fase.");

    private sealed class GraphMessagePage
    {
        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; init; }

        [JsonPropertyName("value")]
        public GraphMessage[] Value { get; init; } = [];
    }

    private sealed class GraphMessage
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("from")]
        public GraphRecipient? From { get; init; }

        [JsonPropertyName("toRecipients")]
        public GraphRecipient[] ToRecipients { get; init; } = [];

        [JsonPropertyName("ccRecipients")]
        public GraphRecipient[] CcRecipients { get; init; } = [];

        [JsonPropertyName("subject")]
        public string? Subject { get; init; }

        [JsonPropertyName("body")]
        public GraphBody? Body { get; init; }

        [JsonPropertyName("bodyPreview")]
        public string? BodyPreview { get; init; }

        [JsonPropertyName("receivedDateTime")]
        public DateTimeOffset ReceivedDateTime { get; init; }

        [JsonPropertyName("isRead")]
        public bool IsRead { get; init; }

        [JsonPropertyName("hasAttachments")]
        public bool HasAttachments { get; init; }
    }

    private sealed class GraphBody
    {
        [JsonPropertyName("contentType")]
        public string? ContentType { get; init; }

        [JsonPropertyName("content")]
        public string? Content { get; init; }
    }

    private sealed class GraphRecipient
    {
        [JsonPropertyName("emailAddress")]
        public GraphEmailAddress? EmailAddress { get; init; }
    }

    private sealed class GraphEmailAddress
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("address")]
        public string? Address { get; init; }
    }
}
