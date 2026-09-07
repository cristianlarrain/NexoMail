namespace NexoMail.Domain;

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
