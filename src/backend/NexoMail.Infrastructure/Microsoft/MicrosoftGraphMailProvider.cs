using System.Net.Http.Headers;
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
        response.EnsureSuccessStatusCode();

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

    public Task<MailMessage?> GetMessageAsync(Guid accountId, string messageId, CancellationToken cancellationToken) =>
        Task.FromException<MailMessage?>(Unsupported());

    public Task<IReadOnlyCollection<MailThreadMessage>> GetThreadAsync(Guid accountId, string messageId, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyCollection<MailThreadMessage>>(Unsupported());

    public Task<MailAttachmentContent?> GetAttachmentAsync(Guid accountId, string messageId, string attachmentId, CancellationToken cancellationToken) =>
        Task.FromException<MailAttachmentContent?>(Unsupported());

    public Task SendAsync(ComposeMessage message, CancellationToken cancellationToken) => Task.FromException(Unsupported());

    public Task ReplyAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken) => Task.FromException(Unsupported());

    public Task ReplyAllAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken) => Task.FromException(Unsupported());

    public Task ForwardAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken) => Task.FromException(Unsupported());

    public Task MarkReadAsync(Guid accountId, string messageId, bool read, CancellationToken cancellationToken) => Task.FromException(Unsupported());

    public Task MoveToTrashAsync(Guid accountId, string messageId, CancellationToken cancellationToken) => Task.FromException(Unsupported());

    public Task MoveToFolderAsync(Guid accountId, string messageId, string folderId, CancellationToken cancellationToken) => Task.FromException(Unsupported());

    public Task EmptyFolderAsync(Guid accountId, string folderId, CancellationToken cancellationToken) => Task.FromException(Unsupported());

    public Task<IReadOnlyCollection<MailFolder>> GetFoldersAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyCollection<MailFolder>>(Unsupported());

    private static string BuildInboxUrl(int take)
    {
        var pageSize = Math.Clamp(take, 1, 100);
        var select = Uri.EscapeDataString("id,from,subject,bodyPreview,receivedDateTime,isRead,hasAttachments");
        var orderBy = Uri.EscapeDataString("receivedDateTime desc");
        return $"{GraphBase}me/mailFolders/inbox/messages?$top={pageSize}&$orderby={orderBy}&$select={select}";
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

        [JsonPropertyName("subject")]
        public string? Subject { get; init; }

        [JsonPropertyName("bodyPreview")]
        public string? BodyPreview { get; init; }

        [JsonPropertyName("receivedDateTime")]
        public DateTimeOffset ReceivedDateTime { get; init; }

        [JsonPropertyName("isRead")]
        public bool IsRead { get; init; }

        [JsonPropertyName("hasAttachments")]
        public bool HasAttachments { get; init; }
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
