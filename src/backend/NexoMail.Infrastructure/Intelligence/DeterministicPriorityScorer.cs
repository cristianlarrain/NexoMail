using NexoMail.Application.Intelligence;

namespace NexoMail.Infrastructure.Intelligence;

public sealed class DeterministicPriorityScorer : IPriorityScorer
{
    public PriorityAssessment Score(
        CommunicationConversation conversation,
        ActionabilityAssessment actionability,
        ConversationStateAssessment state)
    {
        if (!actionability.IsActionable)
        {
            var reason = actionability.RequiresSemanticReview
                ? IntelligenceReasonCodes.SemanticReviewRequired
                : IntelligenceReasonCodes.NotActionable;

            return new(
                0,
                PriorityBand.Low,
                [reason],
                new Dictionary<string, int>());
        }

        if (state.State is ConversationWorkState.Resolved or ConversationWorkState.Cancelled)
        {
            var closedReason = state.State == ConversationWorkState.Resolved
                ? IntelligenceReasonCodes.Resolved
                : IntelligenceReasonCodes.Cancelled;
            return new(
                0,
                PriorityBand.Low,
                [closedReason],
                new Dictionary<string, int>());
        }

        var signals = new Dictionary<string, int>(StringComparer.Ordinal);
        var reasons = new List<string>();
        var score = 10;

        void Add(string code, int points)
        {
            if (points <= 0) return;
            signals[code] = points;
            reasons.Add(code);
            score += points;
        }

        switch (state.State)
        {
            case ConversationWorkState.PendingUser:
                Add(IntelligenceReasonCodes.PendingUser, 25);
                break;
            case ConversationWorkState.WaitingExternal:
                Add(IntelligenceReasonCodes.WaitingExternal, 10);
                break;
            case ConversationWorkState.Overdue:
                Add(IntelligenceReasonCodes.Overdue, 35);
                break;
        }

        if (conversation.Messages.Count > 0)
        {
            var latest = conversation.Messages.OrderByDescending(x => x.OccurredAt).First();
            if (!latest.IsRead) Add(IntelligenceReasonCodes.Unread, 20);
            if (latest.IsDirectRecipient) Add(IntelligenceReasonCodes.DirectRecipient, 15);

            var age = conversation.EvaluatedAt - latest.OccurredAt;
            var agePoints = age >= TimeSpan.FromHours(48)
                ? 10
                : age >= TimeSpan.FromHours(24)
                    ? 5
                    : 0;
            Add(IntelligenceReasonCodes.AgeSignal, agePoints);
        }

        if (actionability.Deadline is { } deadline)
        {
            var remaining = deadline - conversation.EvaluatedAt;
            var deadlinePoints = remaining <= TimeSpan.FromHours(24)
                ? 25
                : remaining <= TimeSpan.FromHours(72)
                    ? 15
                    : 0;
            Add(IntelligenceReasonCodes.DeadlineSignal, deadlinePoints);
        }

        score = Math.Clamp(score, 0, 100);
        var band = score >= 75
            ? PriorityBand.Critical
            : score >= 50
                ? PriorityBand.High
                : score >= 25
                    ? PriorityBand.Normal
                    : PriorityBand.Low;

        return new(score, band, reasons, signals);
    }
}
