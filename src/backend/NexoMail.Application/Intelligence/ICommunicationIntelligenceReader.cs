namespace NexoMail.Application.Intelligence;

public sealed record CommunicationIntelligenceSnapshot(
    Guid AccountId,
    string ConversationId,
    string LatestMessageId,
    DateTimeOffset LatestActivityAt,
    CommunicationIntelligenceResult Intelligence);

public interface ICommunicationIntelligenceReader
{
    Task<IReadOnlyList<CommunicationIntelligenceSnapshot>> AnalyzeAsync(
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken = default);
}
