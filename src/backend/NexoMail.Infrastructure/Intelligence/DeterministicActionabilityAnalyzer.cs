using NexoMail.Application.Intelligence;

namespace NexoMail.Infrastructure.Intelligence;

public sealed class DeterministicActionabilityAnalyzer : IActionabilityAnalyzer
{
    public ActionabilityAssessment Analyze(CommunicationConversation conversation) =>
        new(
            false,
            CommunicationActionType.None,
            1d,
            null,
            [IntelligenceReasonCodes.NotActionable]);
}
