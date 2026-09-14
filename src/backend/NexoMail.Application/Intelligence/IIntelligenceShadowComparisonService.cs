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

public sealed record IntelligenceOnlyReceivedDiagnostics(
    int Count,
    int UnreadCount,
    int ReadCount,
    int DirectRecipientCount,
    int AutomatedCount,
    int BulkCount,
    int ListUnsubscribeCount,
    int ReplyDiscouragedSenderCount,
    int PromotionsCategoryCount,
    int SocialCategoryCount,
    int ForumsCategoryCount,
    int UpdatesCategoryCount,
    int AutoSubmittedCount,
    int BulkPrecedenceCount,
    int ListPrecedenceCount,
    int JunkPrecedenceCount,
    int SingleMessageThreadCount,
    int MultiMessageThreadCount);

public sealed record IntelligenceShadowComparisonResult(
    int LegacyPendingCount,
    int IntelligencePendingCount,
    int AgreementPendingCount,
    int LegacyOnlyCount,
    int IntelligenceOnlyCount,
    int DirectionMismatchCount,
    IReadOnlyList<IntelligenceShadowComparisonItem> Items,
    DateTimeOffset GeneratedAt,
    string EngineVersion,
    IntelligenceOnlyReceivedDiagnostics? IntelligenceOnlyReceivedDiagnostics = null,
    int SemanticReviewCandidateCount = 0,
    IntelligenceOnlyReceivedDiagnostics? SemanticReviewCandidateDiagnostics = null);

public interface IIntelligenceShadowComparisonService
{
    Task<IntelligenceShadowComparisonResult> CompareAsync(
        Guid? accountId,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken = default);
}
