namespace NexoMail.Application.Intelligence;

public interface ICommunicationIntelligenceService
{
    CommunicationIntelligenceResult Analyze(
        CommunicationConversation conversation,
        ConversationStateEvidence? evidence = null);
}
