using NexoMail.Application.Intelligence;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Intelligence;

public sealed class MailMessageIndexConversationAdapter
{
    public IReadOnlyList<CommunicationConversation> Adapt(
        IReadOnlyCollection<MailMessageIndexEntity> messages,
        IReadOnlyCollection<string> ownAddresses,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(ownAddresses);

        var normalizedOwnAddresses = ownAddresses
            .Select(NormalizeEmail)
            .Where(x => x.Contains('@'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return messages
            .GroupBy(x => new { x.AccountId, x.ThreadId })
            .Select(group =>
            {
                var conversationId = $"{group.Key.AccountId:N}:{group.Key.ThreadId}";
                var adaptedMessages = group
                    .OrderBy(x => x.OccurredAt)
                    .ThenBy(x => x.ProviderMessageId, StringComparer.Ordinal)
                    .Select(x => AdaptMessage(x, conversationId, normalizedOwnAddresses))
                    .ToArray();

                return new CommunicationConversation(
                    conversationId,
                    adaptedMessages,
                    new HashSet<string>(normalizedOwnAddresses, StringComparer.OrdinalIgnoreCase),
                    evaluatedAt);
            })
            .OrderBy(x => x.Messages.Count == 0 ? DateTimeOffset.MinValue : x.Messages[^1].OccurredAt)
            .ThenBy(x => x.ConversationId, StringComparer.Ordinal)
            .ToArray();
    }

    private static CommunicationMessage AdaptMessage(
        MailMessageIndexEntity source,
        string conversationId,
        IReadOnlySet<string> ownAddresses)
    {
        var direction = ParseDirection(source.Direction);
        var toAddresses = DeserializeAddresses(source.ToAddresses);
        var isDirectRecipient = direction == CommunicationDirection.Received
                                && toAddresses.Any(ownAddresses.Contains);
        var autoSubmitted = NullIfBlank(source.AutoSubmitted);
        var precedence = NullIfBlank(source.Precedence);
        var isAutomated = autoSubmitted is not null
                          && !string.Equals(autoSubmitted, "no", StringComparison.OrdinalIgnoreCase);
        var isBulk = string.Equals(precedence, "bulk", StringComparison.OrdinalIgnoreCase);

        return new CommunicationMessage(
            source.ProviderMessageId,
            conversationId,
            source.OccurredAt,
            direction,
            source.Subject ?? string.Empty,
            NormalizeEmail(source.FromAddress),
            toAddresses,
            [],
            IsRead: !source.IsUnread,
            IsDirectRecipient: isDirectRecipient,
            IsAutomated: isAutomated,
            IsBulk: isBulk,
            HasListUnsubscribe: source.HasListUnsubscribe,
            AutoSubmitted: autoSubmitted,
            Precedence: precedence);
    }

    private static CommunicationDirection ParseDirection(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "received" => CommunicationDirection.Received,
            "sent" => CommunicationDirection.Sent,
            _ => throw new InvalidOperationException($"Unsupported indexed mail direction '{value}'.")
        };

    private static IReadOnlyList<string> DeserializeAddresses(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];

        return value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\t', 2))
            .Select(parts => NormalizeEmail(parts.Length == 2 ? parts[1] : parts[0]))
            .Where(x => x.Contains('@'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeEmail(string value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
