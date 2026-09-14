using NexoMail.Application.Intelligence;

namespace NexoMail.Infrastructure.Intelligence;

public sealed class DeterministicConversationStateResolver : IConversationStateResolver
{
    public ConversationStateAssessment Resolve(
        CommunicationConversation conversation,
        ActionabilityAssessment actionability,
        ConversationStateEvidence? evidence = null) =>
        new(ConversationWorkState.New, [IntelligenceReasonCodes.NotActionable]);
}
