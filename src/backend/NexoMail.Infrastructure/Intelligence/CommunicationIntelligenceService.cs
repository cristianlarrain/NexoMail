using NexoMail.Application.Intelligence;

namespace NexoMail.Infrastructure.Intelligence;

public sealed class CommunicationIntelligenceService : ICommunicationIntelligenceService
{
    public CommunicationIntelligenceResult Analyze(
        CommunicationConversation conversation,
        ConversationStateEvidence? evidence = null) =>
        new(
            new ActionabilityAssessment(
                false,
                CommunicationActionType.None,
                0d,
                null,
                [IntelligenceReasonCodes.NotActionable]),
            new ConversationStateAssessment(
                ConversationWorkState.New,
                [IntelligenceReasonCodes.NotActionable]),
            new PriorityAssessment(
                0,
                PriorityBand.Low,
                [IntelligenceReasonCodes.NotActionable],
                new Dictionary<string, int>()),
            string.Empty);
}
