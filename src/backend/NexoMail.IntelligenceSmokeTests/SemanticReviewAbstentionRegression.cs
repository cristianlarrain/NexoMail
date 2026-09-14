using System.Runtime.CompilerServices;
using NexoMail.Application.Intelligence;
using NexoMail.Infrastructure.Intelligence;

namespace NexoMail.IntelligenceSmokeTests;

internal static class SemanticReviewAbstentionRegression
{
    [ModuleInitializer]
    public static void Initialize() => Run();

    private static void Run()
    {
        var now = new DateTimeOffset(2026, 9, 14, 6, 45, 0, TimeSpan.Zero);
        var ownAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "user@example.test"
        };
        var directReceived = new CommunicationConversation(
            "semantic-review-conversation",
            [new CommunicationMessage(
                "semantic-review-message",
                "semantic-review-conversation",
                now.AddMinutes(-5),
                CommunicationDirection.Received,
                "Generic subject",
                "person@external.test",
                ["user@example.test"],
                [],
                IsRead: false,
                IsDirectRecipient: true,
                IsAutomated: false,
                IsBulk: false,
                HasListUnsubscribe: false,
                AutoSubmitted: null,
                Precedence: null)],
            ownAddresses,
            now);

        var analyzer = new DeterministicActionabilityAnalyzer();
        var assessment = analyzer.Analyze(directReceived);

        Ensure(!assessment.IsActionable,
            "Un recibido directo sin evidencia semántica no debe declararse accionable de forma determinista.");
        Ensure(assessment.ActionType == CommunicationActionType.Unknown,
            "Un recibido directo ambiguo debe abstenerse con ActionType.Unknown.");
        Ensure(assessment.ReasonCodes.Contains("SEMANTIC_REVIEW_REQUIRED", StringComparer.Ordinal),
            "La abstención determinista debe explicar que requiere revisión semántica.");

        var semanticReviewProperty = typeof(ActionabilityAssessment).GetProperty("RequiresSemanticReview");
        Ensure(semanticReviewProperty is not null,
            "ActionabilityAssessment debe exponer RequiresSemanticReview.");
        Ensure(semanticReviewProperty?.GetValue(assessment) is true,
            "El recibido directo ambiguo debe quedar marcado como RequiresSemanticReview=true.");

        var resolver = new DeterministicConversationStateResolver();
        var state = resolver.Resolve(directReceived, assessment);
        Ensure(state.State == ConversationWorkState.New,
            "Mientras no exista decisión semántica, un recibido ambiguo no debe quedar PendingUser.");
        Ensure(state.ReasonCodes.Contains("SEMANTIC_REVIEW_REQUIRED", StringComparer.Ordinal),
            "El estado debe conservar la explicación de revisión semántica pendiente.");

        var sentExternal = new CommunicationConversation(
            "waiting-external-conversation",
            [new CommunicationMessage(
                "waiting-external-message",
                "waiting-external-conversation",
                now.AddMinutes(-3),
                CommunicationDirection.Sent,
                "Generic subject",
                "user@example.test",
                ["person@external.test"],
                [],
                IsRead: true,
                IsDirectRecipient: false,
                IsAutomated: false,
                IsBulk: false,
                HasListUnsubscribe: false,
                AutoSubmitted: null,
                Precedence: null)],
            ownAddresses,
            now);
        var waiting = analyzer.Analyze(sentExternal);
        Ensure(waiting.IsActionable && waiting.ActionType == CommunicationActionType.WaitForExternal,
            "La abstención semántica para recibidos no debe alterar WaitingExternal en enviados.");

        Ensure(typeof(IntelligenceShadowComparisonResult).GetProperty("SemanticReviewCandidateCount") is not null,
            "El comparador debe exponer cuántos casos quedaron pendientes de revisión semántica.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
