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
            var sourceByMessageId = accountMessages
                .GroupBy(x => x.ProviderMessageId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(x => x.OccurredAt).First(),
                    StringComparer.Ordinal);
            var threadCounts = accountMessages
                .GroupBy(x => x.ThreadId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var conversations = adapter.Adapt(accountMessages, ownAddresses, evaluatedAt);

            foreach (var conversation in conversations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (conversation.Messages.Count == 0) continue;

                var latest = conversation.Messages[^1];
                sourceByMessageId.TryGetValue(latest.MessageId, out var source);
                var signals = BuildSignals(latest, source, threadCounts);

                snapshots.Add(new CommunicationIntelligenceSnapshot(
                    account.Id,
                    conversation.ConversationId,
                    latest.MessageId,
                    latest.OccurredAt,
                    intelligence.Analyze(conversation),
                    signals));
            }
        }

        return snapshots
            .OrderByDescending(x => x.Intelligence.Priority.Score)
            .ThenByDescending(x => x.LatestActivityAt)
            .ThenBy(x => x.ConversationId, StringComparer.Ordinal)
            .ToArray();
    }

    private static CommunicationSnapshotSignals BuildSignals(
        CommunicationMessage latest,
        MailMessageIndexEntity? source,
        IReadOnlyDictionary<string, int> threadCounts)
    {
        var labels = ParseLabels(source?.GmailLabels);
        var messageCount = source is not null && threadCounts.TryGetValue(source.ThreadId, out var count)
            ? count
            : 1;

        return new CommunicationSnapshotSignals(
            IsUnread: !latest.IsRead,
            IsDirectRecipient: latest.IsDirectRecipient,
            IsAutomated: latest.IsAutomated,
            IsBulk: latest.IsBulk,
            HasListUnsubscribe: latest.HasListUnsubscribe,
            IsReplyDiscouragedSender: IsReplyDiscouragedSender(latest.FromAddress),
            IsPromotionsCategory: labels.Contains("CATEGORY_PROMOTIONS"),
            IsSocialCategory: labels.Contains("CATEGORY_SOCIAL"),
            IsForumsCategory: labels.Contains("CATEGORY_FORUMS"),
            IsUpdatesCategory: labels.Contains("CATEGORY_UPDATES"),
            AutoSubmitted: latest.AutoSubmitted,
            Precedence: latest.Precedence,
            MessageCount: Math.Max(1, messageCount));
    }

    private static HashSet<string> ParseLabels(string? value) =>
        (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsReplyDiscouragedSender(string fromAddress)
    {
        if (string.IsNullOrWhiteSpace(fromAddress)) return false;
        var normalized = fromAddress.Trim().ToLowerInvariant();
        var at = normalized.IndexOf('@');
        var localPart = at > 0 ? normalized[..at] : normalized;

        return localPart.Contains("no-reply", StringComparison.Ordinal)
               || localPart.Contains("noreply", StringComparison.Ordinal)
               || localPart.Contains("do-not-reply", StringComparison.Ordinal)
               || localPart.Contains("donotreply", StringComparison.Ordinal)
               || localPart.Contains("mailer-daemon", StringComparison.Ordinal);
    }
}
