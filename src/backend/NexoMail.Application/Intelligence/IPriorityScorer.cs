namespace NexoMail.Application.Intelligence;

public interface IPriorityScorer
{
    PriorityAssessment Score(
        CommunicationConversation conversation,
        ActionabilityAssessment actionability,
        ConversationStateAssessment state);
}
