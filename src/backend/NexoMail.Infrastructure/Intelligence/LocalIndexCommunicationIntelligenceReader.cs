using Microsoft.EntityFrameworkCore;
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
    public async Task<IReadOnlyList<CommunicationIntelligenceSnapshot>> AnalyzeAsync(
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!userContext.IsAuthenticated) return [];

        var userId = userContext.UserId;
        var accounts = await database.MailAccounts
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive)
            .Select(x => new { x.Id, x.EmailAddress })
            .ToArrayAsync(cancellationToken);

        if (accounts.Length == 0) return [];

        var accountIds = accounts.Select(x => x.Id).ToArray();
        var ownAddresses = accounts
            .Select(x => x.EmailAddress)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        var indexedMessages = await database.MailMessageIndex
            .AsNoTracking()
            .Where(x => x.UserId == userId && accountIds.Contains(x.AccountId))
            .ToArrayAsync(cancellationToken);

        var snapshots = new List<CommunicationIntelligenceSnapshot>();

        foreach (var account in accounts.OrderBy(x => x.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var accountMessages = indexedMessages
                .Where(x => x.AccountId == account.Id)
                .ToArray();
            var conversations = adapter.Adapt(accountMessages, ownAddresses, evaluatedAt);

            foreach (var conversation in conversations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (conversation.Messages.Count == 0) continue;

                var latest = conversation.Messages[^1];
                snapshots.Add(new CommunicationIntelligenceSnapshot(
                    account.Id,
                    conversation.ConversationId,
                    latest.MessageId,
                    latest.OccurredAt,
                    intelligence.Analyze(conversation)));
            }
        }

        return snapshots
            .OrderByDescending(x => x.Intelligence.Priority.Score)
            .ThenByDescending(x => x.LatestActivityAt)
            .ThenBy(x => x.ConversationId, StringComparer.Ordinal)
            .ToArray();
    }
}
