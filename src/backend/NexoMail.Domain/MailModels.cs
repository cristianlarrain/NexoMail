namespace NexoMail.Domain;

public enum MailProviderType { Demo, MicrosoftGraph, Gmail, Imap }

public sealed record MailAddress(string Name, string Address);

/// <summary>One selectable address from the connected account's Google Contacts.</summary>
public sealed record ContactSuggestion(string Name, string EmailAddress);

public sealed record MailAccount(
    Guid Id,
    MailProviderType Provider,
    string EmailAddress,
    string DisplayName,
    string Color,
    bool IsActive = true);

public sealed record MailAccountSettings(string DisplayName, string Color);

public sealed record MailFolder(string Id, string DisplayName, int UnreadCount);

public sealed record MailAttachment(string Id, string Name, string ContentType, long Size);

/// <summary>Transient attachment data returned by the provider; it is never persisted.</summary>
public sealed record MailAttachmentContent(byte[] Content, string ContentType, string FileName);

public sealed record MailThreadMessage(string ProviderMessageId, MailAddress From, string HtmlBody, DateTimeOffset ReceivedAt, bool IsCurrent);

public sealed record OutgoingAttachment(string Name, string ContentType, string Base64Content);

public sealed record MailSummary(
    string ProviderMessageId,
    Guid AccountId,
    string SenderName,
    string SenderAddress,
    string Subject,
    string Preview,
    DateTimeOffset ReceivedAt,
    bool IsRead,
    bool HasAttachments,
    string FolderId = "inbox");

public sealed record MailMessage(
    string ProviderMessageId,
    Guid AccountId,
    MailAddress From,
    IReadOnlyCollection<MailAddress> To,
    IReadOnlyCollection<MailAddress> Cc,
    string Subject,
    string HtmlBody,
    string Preview,
    DateTimeOffset ReceivedAt,
    bool IsRead,
    IReadOnlyCollection<MailAttachment> Attachments,
    string FolderId = "inbox",
    IReadOnlyCollection<MailThreadMessage>? Thread = null,
    string? UnsubscribeUrl = null);

public sealed record ComposeMessage(
    Guid FromAccountId,
    IReadOnlyCollection<string> To,
    IReadOnlyCollection<string> Cc,
    IReadOnlyCollection<string> Bcc,
    string Subject,
    string HtmlBody,
    IReadOnlyCollection<OutgoingAttachment>? Attachments = null);

public sealed record MailQuery(Guid? AccountId, string FolderId = "inbox", int Take = 50, string? Cursor = null, string? Search = null);

public sealed record PagedResult<T>(IReadOnlyCollection<T> Items, string? NextCursor = null);

// Lightweight local-index DTOs. These contain metadata only; no message bodies or attachment bytes are persisted.
public sealed record ContactAnalyticsItem(
    string Email,
    string Name,
    IReadOnlyCollection<string> Accounts,
    int Sent,
    int Received,
    int Replies,
    int Awaiting,
    int? AverageResponseMinutes,
    DateTimeOffset LastInteraction,
    IReadOnlyCollection<string> Subjects);

public sealed record ContactAnalyticsSnapshot(
    int Days,
    IReadOnlyCollection<ContactAnalyticsItem> Contacts,
    int TotalSent,
    int TotalReceived,
    int TotalReplies,
    int TotalAwaiting,
    int? AverageResponseMinutes,
    int IndexedMessages,
    DateTimeOffset? IndexedAt);

public sealed record DocumentIndexItem(
    Guid AccountId,
    string AccountName,
    string MessageId,
    string AttachmentId,
    string FileName,
    string ContentType,
    long Size,
    string DocumentType,
    DateTimeOffset ReceivedAt,
    string SenderName,
    string SenderAddress,
    string Subject,
    string Context);

public sealed record DocumentIndexSnapshot(
    IReadOnlyCollection<DocumentIndexItem> Items,
    int Total,
    bool HasMore,
    int IndexedMessages,
    DateTimeOffset? IndexedAt);

public sealed record MailMetadataSyncResult(
    int Accounts,
    int MessagesIndexed,
    int AttachmentsIndexed,
    DateTimeOffset IndexedAt);
