using System.Runtime.CompilerServices;
using NexoMail.Application.Intelligence;
using NexoMail.Infrastructure.Intelligence;

namespace NexoMail.IntelligenceSmokeTests;

internal static class OrchestratorBehaviorRegression
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var analyzer = new DeterministicActionabilityAnalyzer();
        var resolver = new DeterministicConversationStateResolver();
        var scorer = new DeterministicPriorityScorer();
        var service = new CommunicationIntelligenceService(analyzer, resolver, scorer);

        var now = new DateTimeOffset(2026, 9, 14, 3, 0, 0, TimeSpan.Zero);
        var conversation = new CommunicationConversation(
            "orchestrator-conversation",
            [
                new CommunicationMessage(
                    "orchestrator-message",
                    "orchestrator-conversation",
                    now.AddMinutes(-15),
                    CommunicationDirection.Received,
                    "Generic subject",
                    "sender@external.test",
                    ["user@example.test"],
                    [],
                    IsRead: false,
                    IsDirectRecipient: true,
                    IsAutomated: false,
                    IsBulk: false,
                    HasListUnsubscribe: false,
                    AutoSubmitted: null,
                    Precedence: null)
            ],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "user@example.test" },
            now);

        var result = service.Analyze(conversation);
        Ensure(!result.Actionability.IsActionable
               && result.Actionability.RequiresSemanticReview
               && result.Actionability.ActionType == CommunicationActionType.Unknown,
            "El orquestador debe conservar la abstención semántica calculada por el analizador.");
        Ensure(result.State.State == ConversationWorkState.New
               && result.State.ReasonCodes.Contains(IntelligenceReasonCodes.SemanticReviewRequired),
            "El orquestador debe mantener fuera de PendingUser un recibido ambiguo.");
        Ensure(result.Priority.Score == 0
               && result.Priority.ReasonCodes.Contains(IntelligenceReasonCodes.SemanticReviewRequired),
            "El orquestador no debe asignar prioridad determinista antes de la revisión semántica.");
        Ensure(!string.IsNullOrWhiteSpace(result.EngineVersion),
            "Todo resultado debe identificar la versión del motor.");

        var resolved = service.Analyze(
            conversation,
            new ConversationStateEvidence(IsResolved: true));
        Ensure(resolved.State.State == ConversationWorkState.Resolved,
            "La evidencia explícita de resolución debe atravesar el orquestador.");
        Ensure(resolved.Priority.Score == 0,
            "Una conversación resuelta no debe consumir prioridad de trabajo.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
