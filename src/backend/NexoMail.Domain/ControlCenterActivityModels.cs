namespace NexoMail.Domain;

public sealed record ControlCenterAccountActivity(
    Guid AccountId,
    string AccountName,
    string AccountColor,
    bool IsAvailable,
    IReadOnlyCollection<ControlCenterDay> Activity);

public sealed record ControlCenterActivitySnapshot(
    int Days,
    int OffsetDays,
    string StartDate,
    string EndDate,
    IReadOnlyCollection<ControlCenterDay> Activity,
    IReadOnlyCollection<ControlCenterAccountActivity> Accounts,
    int UnavailableAccounts,
    DateTimeOffset GeneratedAt);
