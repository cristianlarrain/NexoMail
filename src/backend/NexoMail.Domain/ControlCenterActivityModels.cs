namespace NexoMail.Domain;

public sealed record ControlCenterActivitySnapshot(
    int Days,
    int OffsetDays,
    string StartDate,
    string EndDate,
    IReadOnlyCollection<ControlCenterDay> Activity,
    int UnavailableAccounts,
    DateTimeOffset GeneratedAt);
