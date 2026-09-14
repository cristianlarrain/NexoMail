using NexoMail.Application.Intelligence;

namespace NexoMail.Infrastructure.Intelligence;

public sealed class DeterministicActionabilityAnalyzer : IActionabilityAnalyzer
{
    public ActionabilityAssessment Analyze(CommunicationConversation conversation)
    {
        if (conversation.Messages.Count == 0)
        {
            return new(
                false,
                CommunicationActionType.None,
                1d,
                null,
                [IntelligenceReasonCodes.EmptyConversation, IntelligenceReasonCodes.NotActionable]);
        }

        var latest = conversation.Messages.OrderByDescending(x => x.OccurredAt).First();
        var ownAddresses = conversation.OwnAddresses
            .Select(Normalize)
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (latest.Direction == CommunicationDirection.Sent)
        {
            var recipients = latest.ToAddresses
                .Concat(latest.CcAddresses)
                .Select(Normalize)
                .Where(x => x.Length > 0)
                .ToArray();
            var hasExternalRecipient = recipients.Length == 0 || recipients.Any(x => !ownAddresses.Contains(x));

            if (!hasExternalRecipient)
            {
                return new(
                    false,
                    CommunicationActionType.None,
                    1d,
                    null,
                    [IntelligenceReasonCodes.OwnAccountOnly, IntelligenceReasonCodes.NotActionable]);
            }

            return new(
                true,
                CommunicationActionType.WaitForExternal,
                0.8d,
                null,
                [IntelligenceReasonCodes.LatestSentExternal, IntelligenceReasonCodes.WaitingExternal]);
        }

        var reasons = new List<string> { IntelligenceReasonCodes.LatestReceived };
        var isAutomated = latest.IsAutomated ||
                          (!string.IsNullOrWhiteSpace(latest.AutoSubmitted) &&
                           !string.Equals(latest.AutoSubmitted.Trim(), "no", StringComparison.OrdinalIgnoreCase));
        var isBulk = latest.IsBulk ||
                     string.Equals(latest.Precedence?.Trim(), "bulk", StringComparison.OrdinalIgnoreCase);
        var isList = latest.HasListUnsubscribe ||
                     string.Equals(latest.Precedence?.Trim(), "list", StringComparison.OrdinalIgnoreCase);

        if (isAutomated) reasons.Add(IntelligenceReasonCodes.Automated);
        if (isBulk) reasons.Add(IntelligenceReasonCodes.Bulk);
        if (isList) reasons.Add(IntelligenceReasonCodes.ListMessage);

        if (isAutomated || isBulk || isList)
        {
            reasons.Add(IntelligenceReasonCodes.NotActionable);
            return new(false, CommunicationActionType.None, 0.95d, null, reasons);
        }

        if (latest.IsDirectRecipient)
        {
            reasons.Add(IntelligenceReasonCodes.DirectRecipient);
            if (!latest.IsRead) reasons.Add(IntelligenceReasonCodes.Unread);
            return new(
                true,
                CommunicationActionType.Reply,
                latest.IsRead ? 0.75d : 0.85d,
                null,
                reasons);
        }

        reasons.Add(IntelligenceReasonCodes.NotActionable);
        return new(false, CommunicationActionType.None, 0.6d, null, reasons);
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
