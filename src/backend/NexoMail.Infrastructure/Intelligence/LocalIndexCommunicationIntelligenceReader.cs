using NexoMail.Application;
using NexoMail.Application.Intelligence;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Intelligence;

public sealed class LocalIndexCommunicationIntelligenceReader(
    NexoMailDbContext database,
    IUserContext userContext,
    MailMessageIndexConversationAdapter adapter,
    ICommunicationIntelligenceService intelligence) : ICommunicationIntelligenceReader
{
    public Task<IReadOnlyList<CommunicationIntelligenceSnapshot>> AnalyzeAsync(
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken = default)
    {
        _ = database;
        _ = userContext;
        _ = adapter;
        _ = intelligence;
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<CommunicationIntelligenceSnapshot>>([]);
    }
}
