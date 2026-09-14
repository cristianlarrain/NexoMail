using System.Runtime.CompilerServices;
using NexoMail.Application.Intelligence;

namespace NexoMail.IntelligenceSmokeTests;

internal static class SemanticContractsRegression
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    private static void Run()
    {
        var candidate = new SemanticCommunicationCandidate(
            "corr-1",
            "conv-1",
            "msg-1",
            DateTimeOffset.UtcNow,
            "Subject",
            "Snippet",
            [],
            IsRead: true,
            IsDirectRecipient: true,
            ReplyDiscouragedSender: false,
            Categories: [],
            StructuralReasonCodes: []);

        Ensure(candidate.CorrelationId == "corr-1",
            "El candidato semántico debe tener correlación explícita.");

        var result = new SemanticAnalysisResult(
            "corr-1",
            new SemanticActionabilityAssessment(
                CommunicationActionType.Unknown,
                RequiresAction: false,
                Confidence: 0,
                Deadline: null,
                ReasonCodes: ["SEMANTIC_UNCERTAIN"],
                IsUncertain: true),
            "test-provider/1");

        Ensure(result.CorrelationId == candidate.CorrelationId,
            "El resultado debe conservar la correlación del candidato.");
        Ensure(typeof(ISemanticCommunicationAnalyzer).GetMethod("AnalyzeAsync") is not null,
            "Debe existir el analizador semántico por lotes.");
        Ensure(typeof(SemanticActionabilityAssessment).GetProperty("IsUncertain") is not null,
            "La evaluación semántica debe poder abstenerse explícitamente.");

        var semanticTypes = typeof(SemanticCommunicationCandidate).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "NexoMail.Application.Intelligence"
                           && type.Name.Contains("Semantic", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Ensure(semanticTypes.All(type => !type.FullName!.Contains("OpenAI", StringComparison.OrdinalIgnoreCase)),
            "Los contratos de Application no deben acoplarse a OpenAI.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
