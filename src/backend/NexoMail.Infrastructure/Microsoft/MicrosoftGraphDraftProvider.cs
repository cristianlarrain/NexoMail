using System.Net.Http.Json;
using System.Text.Json;
using NexoMail.Application;
using NexoMail.Domain;

namespace NexoMail.Infrastructure.Microsoft;

public sealed class MicrosoftGraphDraftProvider(MicrosoftGraphClientFactory clientFactory) : IMailDraftProvider
{
    public MailProviderType ProviderType => MailProviderType.MicrosoftGraph;

    public async Task SaveDraftAsync(Guid accountId, string? replyToMessageId, ComposeMessage message, CancellationToken ct)
    {
        var client = await clientFactory.CreateAsync(accountId, ct);
        if (string.IsNullOrWhiteSpace(replyToMessageId))
        {
            using var response = await client.PostAsJsonAsync("me/messages", BuildMessage(message), ct);
            response.EnsureSuccessStatusCode();
            return;
        }

        using var create = await client.PostAsJsonAsync($"me/messages/{Uri.EscapeDataString(replyToMessageId)}/createReply", new { }, ct);
        create.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await create.Content.ReadAsStreamAsync(ct));
        var draftId = document.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
        if (string.IsNullOrWhiteSpace(draftId)) throw new InvalidOperationException("Microsoft 365 no devolvió el borrador de respuesta.");
        await PatchDraftAsync(client, draftId, message, ct);
    }

    public async Task UpdateDraftAsync(Guid accountId, string draftMessageId, ComposeMessage message, CancellationToken ct)
    {
        var client = await clientFactory.CreateAsync(accountId, ct);
        await PatchDraftAsync(client, draftMessageId, message, ct);
    }

    public async Task SendDraftAsync(Guid accountId, string draftMessageId, ComposeMessage message, CancellationToken ct)
    {
        var client = await clientFactory.CreateAsync(accountId, ct);
        await PatchDraftAsync(client, draftMessageId, message, ct);
        using var response = await client.PostAsync($"me/messages/{Uri.EscapeDataString(draftMessageId)}/send", null, ct);
        response.EnsureSuccessStatusCode();
    }

    private static async Task PatchDraftAsync(HttpClient client, string draftMessageId, ComposeMessage message, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"me/messages/{Uri.EscapeDataString(draftMessageId)}")
        {
            Content = JsonContent.Create(BuildMessage(message))
        };
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private static object BuildMessage(ComposeMessage message) => new
    {
        subject = message.Subject,
        body = new { contentType = "HTML", content = message.HtmlBody },
        toRecipients = Recipients(message.To),
        ccRecipients = Recipients(message.Cc),
        bccRecipients = Recipients(message.Bcc),
        attachments = (message.Attachments ?? []).Select(value => (object)new Dictionary<string, object?>
        {
            ["@odata.type"] = "#microsoft.graph.fileAttachment",
            ["name"] = value.Name,
            ["contentType"] = value.ContentType,
            ["contentBytes"] = value.Base64Content
        }).ToArray()
    };

    private static object[] Recipients(IEnumerable<string> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => (object)new { emailAddress = new { address = value.Trim() } })
        .ToArray();
}
