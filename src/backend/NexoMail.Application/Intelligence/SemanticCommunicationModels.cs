namespace NexoMail.Application.Intelligence;

public sealed record SemanticThreadExcerpt(
    CommunicationDirection Direction,
    DateTimeOffset OccurredAt,
    string Snippet);

public sealed record SemanticCommunicationCandidate(
    string CorrelationId,
    string ConversationId,
    string LatestMessageId,
    DateTimeOffset OccurredAt,
    string Subject,
    string Snippet,
    IReadOnlyList<SemanticThreadExcerpt> RecentExcerpts,
    bool IsRead,
    bool IsDirectRecipient,
    bool ReplyDiscouragedSender,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> StructuralReasonCodes);

public sealed record SemanticActionabilityAssessment(
    CommunicationActionType ActionType,
    bool RequiresAction,
    double Confidence,
    DateTimeOffset? Deadline,
    IReadOnlyList<string> ReasonCodes,
    bool IsUncertain);

public sealed record SemanticAnalysisResult(
    string CorrelationId,
    SemanticActionabilityAssessment Assessment,
    string ProviderVersion);

public sealed record SemanticCandidateSnapshot(
    Guid AccountId,
    string ConversationId,
    string LatestMessageId,
    DateTimeOffset LatestActivityAt,
    SemanticCommunicationCandidate Candidate);

public sealed record SemanticShadowItem(
    Guid AccountId,
    string ConversationId,
    string LatestMessageId,
    DateTimeOffset LatestActivityAt,
    SemanticActionabilityAssessment Assessment,
    string ProviderVersion);

public sealed record SemanticShadowDiagnostics(
    int CandidateCount,
    int ClassifiedActionCount,
    int ClassifiedNoActionCount,
    int UncertainCount,
    IReadOnlyDictionary<CommunicationActionType, int> ActionTypeCounts,
    IReadOnlyDictionary<string, int> ConfidenceBuckets,
    int DeadlineDetectedCount,
    int ProviderFailureCount,
    int ParseFailureCount);

public sealed record SemanticShadowResult(
    bool Enabled,
    int RequestedLimit,
    IReadOnlyList<SemanticShadowItem> Items,
    SemanticShadowDiagnostics Diagnostics,
    DateTimeOffset GeneratedAt);
