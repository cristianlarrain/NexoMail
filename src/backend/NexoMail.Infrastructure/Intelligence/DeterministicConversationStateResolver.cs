using NexoMail.Application.Intelligence;

namespace NexoMail.Infrastructure.Intelligence;

public sealed class DeterministicConversationStateResolver : IConversationStateResolver
{
    public ConversationStateAssessment Resolve(
        CommunicationConversation conversation,
        ActionabilityAssessment actionability,
        ConversationStateEvidence? evidence = null)
    {
        if (evidence?.IsCancelled == true)
        {
            return new(ConversationWorkState.Cancelled, [IntelligenceReasonCodes.Cancelled]);
        }

        if (evidence?.IsResolved == true)
        {
            return new(ConversationWorkState.Resolved, [IntelligenceReasonCodes.Resolved]);
        }

        if (conversation.Messages.Count == 0)
        {
            return new(
                ConversationWorkState.New,
                [IntelligenceReasonCodes.EmptyConversation, IntelligenceReasonCodes.NotActionable]);
        }

        var deadline = evidence?.Deadline ?? actionability.Deadline;
        if (actionability.IsActionable && deadline is { } due && due <= conversation.EvaluatedAt)
        {
            return new(
                ConversationWorkState.Overdue,
                [IntelligenceReasonCodes.Overdue, IntelligenceReasonCodes.DeadlineSignal]);
        }

        if (actionability.RequiresSemanticReview)
        {
            return new(
                ConversationWorkState.New,
                [IntelligenceReasonCodes.SemanticReviewRequired]);
        }

        if (!actionability.IsActionable)
        {
            return new(ConversationWorkState.New, [IntelligenceReasonCodes.NotActionable]);
        }

        if (actionability.ActionType == CommunicationActionType.WaitForExternal)
        {
            return new(
                ConversationWorkState.WaitingExternal,
                [IntelligenceReasonCodes.WaitingExternal]);
        }

        return new(
            ConversationWorkState.PendingUser,
            [IntelligenceReasonCodes.PendingUser]);
    }
}
