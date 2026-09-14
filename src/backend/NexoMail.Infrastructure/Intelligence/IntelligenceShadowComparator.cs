using NexoMail.Application.Intelligence;
using NexoMail.Domain;

namespace NexoMail.Infrastructure.Intelligence;

public sealed class IntelligenceShadowComparator
{
    public IntelligenceShadowComparisonResult Compare(
        ControlCenterSnapshot legacy,
        IReadOnlyList<CommunicationIntelligenceSnapshot> intelligence,
        Guid? accountId,
        DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        ArgumentNullException.ThrowIfNull(intelligence);

        var legacyPending = legacy.PendingItems
            .Where(item => !accountId.HasValue || item.AccountId == accountId.Value)
            .GroupBy(item => (item.AccountId, ConversationId: NormalizeConversationId(item.AccountId, item.ConversationId)))
            .Select(group => group
                .OrderByDescending(item => item.Since)
                .ThenByDescending(item => item.MessageId, StringComparer.Ordinal)
                .First())
            .ToDictionary(
                item => (item.AccountId, ConversationId: NormalizeConversationId(item.AccountId, item.ConversationId)));

        var intelligenceByConversation = intelligence
            .Where(item => !accountId.HasValue || item.AccountId == accountId.Value)
            .GroupBy(item => (item.AccountId, ConversationId: NormalizeConversationId(item.AccountId, item.ConversationId)))
            .Select(group => group
                .OrderByDescending(item => item.LatestActivityAt)
                .ThenByDescending(item => item.LatestMessageId, StringComparer.Ordinal)
                .First())
            .ToDictionary(
                item => (item.AccountId, ConversationId: NormalizeConversationId(item.AccountId, item.ConversationId)));

        var intelligencePending = intelligenceByConversation
            .Where(pair => IsPending(pair.Value.Intelligence))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        var intelligenceOnlyReceived = intelligencePending
            .Where(pair => !legacyPending.ContainsKey(pair.Key) && IsReceivedPending(pair.Value.Intelligence))
            .Select(pair => pair.Value)
            .ToArray();

        var keys = legacyPending.Keys
            .Union(intelligencePending.Keys)
            .ToArray();

        var items = new List<IntelligenceShadowComparisonItem>(keys.Length);
        var agreementPendingCount = 0;
        var legacyOnlyCount = 0;
        var intelligenceOnlyCount = 0;
        var directionMismatchCount = 0;

        foreach (var key in keys)
        {
            legacyPending.TryGetValue(key, out var legacyItem);
            intelligenceByConversation.TryGetValue(key, out var intelligenceItem);

            var legacyIsPending = legacyItem is not null;
            var intelligenceIsPending = intelligenceItem is not null
                && IsPending(intelligenceItem.Intelligence);
            var pendingAgreement = legacyIsPending == intelligenceIsPending;

            bool? directionAgreement = null;
            IntelligenceShadowComparisonCategory category;

            if (legacyIsPending && intelligenceIsPending)
            {
                agreementPendingCount++;
                directionAgreement = DirectionAgrees(
                    legacyItem!.Direction,
                    intelligenceItem!.Intelligence);
                if (directionAgreement == true)
                {
                    category = IntelligenceShadowComparisonCategory.Agreement;
                }
                else
                {
                    category = IntelligenceShadowComparisonCategory.DirectionMismatch;
                    directionMismatchCount++;
                }
            }
            else if (legacyIsPending)
            {
                category = IntelligenceShadowComparisonCategory.LegacyOnly;
                legacyOnlyCount++;
            }
            else
            {
                category = IntelligenceShadowComparisonCategory.IntelligenceOnly;
                intelligenceOnlyCount++;
            }

            var latestMessageId = intelligenceItem?.LatestMessageId
                ?? legacyItem?.MessageId
                ?? string.Empty;
            var latestActivityAt = intelligenceItem?.LatestActivityAt
                ?? legacyItem?.Since
                ?? generatedAt;
            var state = intelligenceItem?.Intelligence.State.State
                ?? ConversationWorkState.New;
            var actionType = intelligenceItem?.Intelligence.Actionability.ActionType
                ?? CommunicationActionType.None;
            var priorityScore = intelligenceItem?.Intelligence.Priority.Score ?? 0;

            var reasonCodes = BuildReasonCodes(category, intelligenceItem?.Intelligence);

            items.Add(new IntelligenceShadowComparisonItem(
                AccountId: key.AccountId,
                ConversationId: key.ConversationId,
                LatestMessageId: latestMessageId,
                LatestActivityAt: latestActivityAt,
                LegacyPending: legacyIsPending,
                LegacyDirection: legacyItem?.Direction,
                IntelligencePending: intelligenceIsPending,
                IntelligenceState: state,
                IntelligenceActionType: actionType,
                IntelligencePriorityScore: priorityScore,
                PendingAgreement: pendingAgreement,
                DirectionAgreement: directionAgreement,
                Category: category,
                ReasonCodes: reasonCodes));
        }

        var orderedItems = items
            .OrderBy(item => CategoryRank(item.Category))
            .ThenByDescending(item => item.IntelligencePriorityScore)
            .ThenByDescending(item => item.LatestActivityAt)
            .ThenBy(item => item.AccountId)
            .ThenBy(item => item.ConversationId, StringComparer.Ordinal)
            .ToArray();

        var engineVersion = intelligenceByConversation.Values
            .Select(item => item.Intelligence.EngineVersion)
            .FirstOrDefault(version => !string.IsNullOrWhiteSpace(version))
            ?? "unknown";

        return new IntelligenceShadowComparisonResult(
            LegacyPendingCount: legacyPending.Count,
            IntelligencePendingCount: intelligencePending.Count,
            AgreementPendingCount: agreementPendingCount,
            LegacyOnlyCount: legacyOnlyCount,
            IntelligenceOnlyCount: intelligenceOnlyCount,
            DirectionMismatchCount: directionMismatchCount,
            Items: orderedItems,
            GeneratedAt: generatedAt,
            EngineVersion: engineVersion,
            IntelligenceOnlyReceivedDiagnostics: BuildReceivedDiagnostics(intelligenceOnlyReceived));
    }

    private static IntelligenceOnlyReceivedDiagnostics BuildReceivedDiagnostics(
        IReadOnlyList<CommunicationIntelligenceSnapshot> snapshots)
    {
        var signals = snapshots
            .Select(snapshot => snapshot.Signals)
            .Where(signal => signal is not null)
            .Cast<CommunicationSnapshotSignals>()
            .ToArray();

        return new IntelligenceOnlyReceivedDiagnostics(
            Count: snapshots.Count,
            UnreadCount: signals.Count(signal => signal.IsUnread),
            ReadCount: signals.Count(signal => !signal.IsUnread),
            DirectRecipientCount: signals.Count(signal => signal.IsDirectRecipient),
            AutomatedCount: signals.Count(signal => signal.IsAutomated),
            BulkCount: signals.Count(signal => signal.IsBulk),
            ListUnsubscribeCount: signals.Count(signal => signal.HasListUnsubscribe),
            ReplyDiscouragedSenderCount: signals.Count(signal => signal.IsReplyDiscouragedSender),
            PromotionsCategoryCount: signals.Count(signal => signal.IsPromotionsCategory),
            SocialCategoryCount: signals.Count(signal => signal.IsSocialCategory),
            ForumsCategoryCount: signals.Count(signal => signal.IsForumsCategory),
            UpdatesCategoryCount: signals.Count(signal => signal.IsUpdatesCategory),
            AutoSubmittedCount: signals.Count(signal => HasNonDefaultAutoSubmitted(signal.AutoSubmitted)),
            BulkPrecedenceCount: signals.Count(signal => PrecedenceIs(signal.Precedence, "bulk")),
            ListPrecedenceCount: signals.Count(signal => PrecedenceIs(signal.Precedence, "list")),
            JunkPrecedenceCount: signals.Count(signal => PrecedenceIs(signal.Precedence, "junk")),
            SingleMessageThreadCount: signals.Count(signal => signal.MessageCount == 1),
            MultiMessageThreadCount: signals.Count(signal => signal.MessageCount > 1));
    }

    private static bool HasNonDefaultAutoSubmitted(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !string.Equals(value.Trim(), "no", StringComparison.OrdinalIgnoreCase);

    private static bool PrecedenceIs(string? value, string expected) =>
        string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeConversationId(Guid accountId, string conversationId)
    {
        var value = conversationId?.Trim() ?? string.Empty;
        var compositePrefix = $"{accountId:N}:";

        return value.StartsWith(compositePrefix, StringComparison.OrdinalIgnoreCase)
            ? value[compositePrefix.Length..]
            : value;
    }

    private static bool IsPending(CommunicationIntelligenceResult intelligence) =>
        intelligence.Actionability.IsActionable
        && intelligence.State.State is ConversationWorkState.PendingUser
            or ConversationWorkState.WaitingExternal
            or ConversationWorkState.Overdue;

    private static bool IsReceivedPending(CommunicationIntelligenceResult intelligence) =>
        intelligence.State.State == ConversationWorkState.PendingUser
        || (intelligence.State.State == ConversationWorkState.Overdue
            && intelligence.Actionability.ActionType != CommunicationActionType.WaitForExternal);

    private static bool DirectionAgrees(
        string legacyDirection,
        CommunicationIntelligenceResult intelligence)
    {
        if (legacyDirection.Equals("received", StringComparison.OrdinalIgnoreCase))
        {
            return intelligence.State.State == ConversationWorkState.PendingUser
                || (intelligence.State.State == ConversationWorkState.Overdue
                    && intelligence.Actionability.ActionType != CommunicationActionType.WaitForExternal);
        }

        if (legacyDirection.Equals("sent", StringComparison.OrdinalIgnoreCase))
        {
            return intelligence.State.State == ConversationWorkState.WaitingExternal
                || (intelligence.State.State == ConversationWorkState.Overdue
                    && intelligence.Actionability.ActionType == CommunicationActionType.WaitForExternal);
        }

        return false;
    }

    private static IReadOnlyList<string> BuildReasonCodes(
        IntelligenceShadowComparisonCategory category,
        CommunicationIntelligenceResult? intelligence)
    {
        var reasons = new List<string> { category.ToString().ToUpperInvariant() };
        if (intelligence is null) return reasons;

        reasons.AddRange(intelligence.Actionability.ReasonCodes);
        reasons.AddRange(intelligence.State.ReasonCodes);
        reasons.AddRange(intelligence.Priority.ReasonCodes);
        return reasons.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static int CategoryRank(IntelligenceShadowComparisonCategory category) => category switch
    {
        IntelligenceShadowComparisonCategory.DirectionMismatch => 0,
        IntelligenceShadowComparisonCategory.LegacyOnly => 1,
        IntelligenceShadowComparisonCategory.IntelligenceOnly => 2,
        _ => 3
    };
}
