namespace NexoMail.Application.Intelligence;

public enum IntelligenceShadowComparisonCategory
{
    Agreement,
    LegacyOnly,
    IntelligenceOnly,
    DirectionMismatch
}

public sealed record IntelligenceShadowComparisonItem(
    Guid AccountId,
    string ConversationId,
    string LatestMessageId,
    DateTimeOffset LatestActivityAt,
    bool LegacyPending,
    string? LegacyDirection,
    bool IntelligencePending,
    ConversationWorkState IntelligenceState,
    CommunicationActionType IntelligenceActionType,
    int IntelligencePriorityScore,
    bool PendingAgreement,
    bool? DirectionAgreement,
    IntelligenceShadowComparisonCategory Category,
    IReadOnlyList<string> ReasonCodes);

public sealed record IntelligenceShadowComparisonResult(
    int LegacyPendingCount,
    int IntelligencePendingCount,
    int AgreementPendingCount,
    int LegacyOnlyCount,
    int IntelligenceOnlyCount,
    int DirectionMismatchCount,
    IReadOnlyList<IntelligenceShadowComparisonItem> Items,
    DateTimeOffset GeneratedAt,
    string EngineVersion);

public interface IIntelligenceShadowComparisonService
{
    Task<IntelligenceShadowComparisonResult> CompareAsync(
        Guid? accountId,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken = default);
}
