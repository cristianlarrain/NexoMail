namespace NexoMail.Domain;

public sealed record ControlCenterDay(
    string Date,
    int Received,
    int Sent);

public sealed record ControlCenterPendingItem(
    Guid AccountId,
    string AccountName,
    string AccountColor,
    string MessageId,
    string ConversationId,
    string Direction,
    string Counterpart,
    string Subject,
    DateTimeOffset Since,
    bool IsRead);

public sealed record ControlCenterAccountSummary(
    Guid AccountId,
    string AccountName,
    string AccountColor,
    int ReceivedWithoutReply,
    int SentWithoutResponse,
    int Unread,
    bool IsAvailable);

public sealed record ControlCenterSnapshot(
    int ReceivedWithoutReply,
    int SentWithoutResponse,
    int Unread,
    int Overdue,
    IReadOnlyCollection<ControlCenterDay> Activity,
    IReadOnlyCollection<ControlCenterPendingItem> PriorityItems,
    IReadOnlyCollection<ControlCenterPendingItem> PendingItems,
    IReadOnlyCollection<ControlCenterAccountSummary> Accounts,
    int UnavailableAccounts,
    DateTimeOffset GeneratedAt);
