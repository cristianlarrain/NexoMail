namespace NexoMail.Application.Intelligence;

public sealed record CommunicationSnapshotSignals(
    bool IsUnread,
    bool IsDirectRecipient,
    bool IsAutomated,
    bool IsBulk,
    bool HasListUnsubscribe,
    bool IsReplyDiscouragedSender,
    bool IsPromotionsCategory,
    bool IsSocialCategory,
    bool IsForumsCategory,
    bool IsUpdatesCategory,
    string? AutoSubmitted,
    string? Precedence,
    int MessageCount);

public sealed record CommunicationIntelligenceSnapshot(
    Guid AccountId,
    string ConversationId,
    string LatestMessageId,
    DateTimeOffset LatestActivityAt,
    CommunicationIntelligenceResult Intelligence,
    CommunicationSnapshotSignals? Signals = null);

public interface ICommunicationIntelligenceReader
{
    Task<IReadOnlyList<CommunicationIntelligenceSnapshot>> AnalyzeAsync(
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken = default);
}
