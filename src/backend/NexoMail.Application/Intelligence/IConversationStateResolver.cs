namespace NexoMail.Application.Intelligence;

public interface IConversationStateResolver
{
    ConversationStateAssessment Resolve(
        CommunicationConversation conversation,
        ActionabilityAssessment actionability,
        ConversationStateEvidence? evidence = null);
}
