namespace NexoMail.Application.Intelligence;

public enum CommunicationDirection
{
    Received,
    Sent
}

public enum CommunicationActionType
{
    None,
    Reply,
    Confirm,
    Review,
    CompleteTask,
    WaitForExternal,
    Unknown
}

public enum ConversationWorkState
{
    New,
    PendingUser,
    WaitingExternal,
    Resolved,
    Cancelled,
    Overdue
}

public enum PriorityBand
{
    Low,
    Normal,
    High,
    Critical
}

public sealed record CommunicationMessage(
    string MessageId,
    string ConversationId,
    DateTimeOffset OccurredAt,
    CommunicationDirection Direction,
    string Subject,
    string FromAddress,
    IReadOnlyList<string> ToAddresses,
    IReadOnlyList<string> CcAddresses,
    bool IsRead,
    bool IsDirectRecipient,
    bool IsAutomated,
    bool IsBulk,
    bool HasListUnsubscribe,
    string? AutoSubmitted,
    string? Precedence);

public sealed record CommunicationConversation(
    string ConversationId,
    IReadOnlyList<CommunicationMessage> Messages,
    IReadOnlySet<string> OwnAddresses,
    DateTimeOffset EvaluatedAt);

public sealed record ConversationStateEvidence(
    bool IsResolved = false,
    bool IsCancelled = false,
    DateTimeOffset? Deadline = null);

public sealed record ActionabilityAssessment(
    bool IsActionable,
    CommunicationActionType ActionType,
    double Confidence,
    DateTimeOffset? Deadline,
    IReadOnlyList<string> ReasonCodes,
    bool RequiresSemanticReview = false);

public sealed record ConversationStateAssessment(
    ConversationWorkState State,
    IReadOnlyList<string> ReasonCodes);

public sealed record PriorityAssessment(
    int Score,
    PriorityBand Band,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyDictionary<string, int> Signals);

public sealed record CommunicationIntelligenceResult(
    ActionabilityAssessment Actionability,
    ConversationStateAssessment State,
    PriorityAssessment Priority,
    string EngineVersion);
