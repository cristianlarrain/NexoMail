using NexoMail.Application.Intelligence;

namespace NexoMail.Infrastructure.Intelligence;

public sealed class DeterministicPriorityScorer : IPriorityScorer
{
    public PriorityAssessment Score(
        CommunicationConversation conversation,
        ActionabilityAssessment actionability,
        ConversationStateAssessment state) =>
        new(
            0,
            PriorityBand.Low,
            [IntelligenceReasonCodes.NotActionable],
            new Dictionary<string, int>());
}
