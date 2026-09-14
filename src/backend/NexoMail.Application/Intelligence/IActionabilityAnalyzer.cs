namespace NexoMail.Application.Intelligence;

public interface IActionabilityAnalyzer
{
    ActionabilityAssessment Analyze(CommunicationConversation conversation);
}
