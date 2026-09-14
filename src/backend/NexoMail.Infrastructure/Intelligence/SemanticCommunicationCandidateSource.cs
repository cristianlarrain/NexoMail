using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Application.Intelligence;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Intelligence;

public sealed class SemanticCommunicationCandidateSource(
    NexoMailDbContext database,
    IUserContext userContext,
    ICommunicationIntelligenceReader intelligenceReader)
{
    private const int MaximumSubjectCharacters = 600;
    private const int MaximumSnippetCharacters = 1000;
    private const int MaximumRecentExcerpts = 6;
    private const int MaximumExcerptCharacters = 800;

    public async Task<IReadOnlyList<SemanticCandidateSnapshot>> ReadAsync(
        DateTimeOffset evaluatedAt,
        Guid? accountId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!userContext.IsAuthenticated || limit <= 0) return [];

        var snapshots = await intelligenceReader.AnalyzeAsync(evaluatedAt, cancellationToken);
        var selected = snapshots
            .Where(snapshot => snapshot.Intelligence.Actionability.RequiresSemanticReview)
            .Where(snapshot => !accountId.HasValue || snapshot.AccountId == accountId.Value)
            .OrderByDescending(snapshot => snapshot.LatestActivityAt)
            .ThenBy(snapshot => snapshot.AccountId)
            .ThenBy(snapshot => snapshot.ConversationId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

        if (selected.Length == 0) return [];

        var userId = userContext.UserId;
        var selectedAccountIds = selected.Select(snapshot => snapshot.AccountId).Distinct().ToArray();
        var activeAccountIds = await database.MailAccounts
            .AsNoTracking()
            .Where(account => account.UserId == userId
                              && account.IsActive
                              && selectedAccountIds.Contains(account.Id))
            .Select(account => account.Id)
            .ToArrayAsync(cancellationToken);

        if (activeAccountIds.Length == 0) return [];

        var indexedMessages = await database.MailMessageIndex
            .AsNoTracking()
            .Where(message => message.UserId == userId && activeAccountIds.Contains(message.AccountId))
            .ToArrayAsync(cancellationToken);

        var results = new List<SemanticCandidateSnapshot>(selected.Length);
        foreach (var snapshot in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!activeAccountIds.Contains(snapshot.AccountId)) continue;

            var threadId = ConversationIdentity.Normalize(snapshot.AccountId, snapshot.ConversationId);
            var thread = indexedMessages
                .Where(message => message.AccountId == snapshot.AccountId
                                  && string.Equals(message.ThreadId, threadId, StringComparison.Ordinal))
                .OrderBy(message => message.OccurredAt)
                .ThenBy(message => message.ProviderMessageId, StringComparer.Ordinal)
                .ToArray();

            var latest = thread
                .Where(message => string.Equals(message.ProviderMessageId, snapshot.LatestMessageId, StringComparison.Ordinal))
                .OrderByDescending(message => message.OccurredAt)
                .FirstOrDefault()
                ?? thread.LastOrDefault();
            if (latest is null) continue;

            var signals = snapshot.Signals;
            var categories = BuildCategories(signals);
            var reasons = snapshot.Intelligence.Actionability.ReasonCodes
                .Concat(snapshot.Intelligence.State.ReasonCodes)
                .Concat(snapshot.Intelligence.Priority.ReasonCodes)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var excerpts = thread
                .TakeLast(MaximumRecentExcerpts)
                .Select(message => new SemanticThreadExcerpt(
                    ParseDirection(message.Direction),
                    message.OccurredAt,
                    Limit(message.Snippet ?? string.Empty, MaximumExcerptCharacters)))
                .ToArray();
            var correlationId = $"{snapshot.AccountId:N}:{snapshot.LatestMessageId}";

            var candidate = new SemanticCommunicationCandidate(
                CorrelationId: correlationId,
                ConversationId: threadId,
                LatestMessageId: snapshot.LatestMessageId,
                OccurredAt: snapshot.LatestActivityAt,
                Subject: Limit(latest.Subject ?? string.Empty, MaximumSubjectCharacters),
                Snippet: Limit(latest.Snippet ?? string.Empty, MaximumSnippetCharacters),
                RecentExcerpts: excerpts,
                IsRead: !(signals?.IsUnread ?? latest.IsUnread),
                IsDirectRecipient: signals?.IsDirectRecipient ?? false,
                ReplyDiscouragedSender: signals?.IsReplyDiscouragedSender ?? false,
                Categories: categories,
                StructuralReasonCodes: reasons);

            results.Add(new SemanticCandidateSnapshot(
                snapshot.AccountId,
                threadId,
                snapshot.LatestMessageId,
                snapshot.LatestActivityAt,
                candidate));
        }

        return results;
    }

    private static IReadOnlyList<string> BuildCategories(CommunicationSnapshotSignals? signals)
    {
        if (signals is null) return [];
        var categories = new List<string>(4);
        if (signals.IsPromotionsCategory) categories.Add("PROMOTIONS");
        if (signals.IsSocialCategory) categories.Add("SOCIAL");
        if (signals.IsForumsCategory) categories.Add("FORUMS");
        if (signals.IsUpdatesCategory) categories.Add("UPDATES");
        return categories;
    }

    private static CommunicationDirection ParseDirection(string? direction) =>
        string.Equals(direction, "sent", StringComparison.OrdinalIgnoreCase)
            ? CommunicationDirection.Sent
            : CommunicationDirection.Received;

    private static string Limit(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];
}
