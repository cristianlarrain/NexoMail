using NexoMail.Application.Intelligence;

namespace NexoMail.Infrastructure.Intelligence;

public sealed class CommunicationIntelligenceService(
    IActionabilityAnalyzer actionabilityAnalyzer,
    IConversationStateResolver stateResolver,
    IPriorityScorer priorityScorer) : ICommunicationIntelligenceService
{
    public const string Version = "nexo-intelligence/0.1-deterministic";

    public CommunicationIntelligenceResult Analyze(
        CommunicationConversation conversation,
        ConversationStateEvidence? evidence = null)
    {
        var actionability = actionabilityAnalyzer.Analyze(conversation);
        var state = stateResolver.Resolve(conversation, actionability, evidence);
        var priority = priorityScorer.Score(conversation, actionability, state);

        return new(actionability, state, priority, Version);
    }
}
