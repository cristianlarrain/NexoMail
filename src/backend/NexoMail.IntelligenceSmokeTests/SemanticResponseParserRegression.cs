using System.Runtime.CompilerServices;
using NexoMail.Application.Intelligence;
using NexoMail.Infrastructure.Intelligence;

namespace NexoMail.IntelligenceSmokeTests;

internal static class SemanticResponseParserRegression
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    private static void Run()
    {
        var parser = new SemanticResponseParser();
        var candidates = new[]
        {
            Candidate("c1"),
            Candidate("c2"),
            Candidate("c3"),
            Candidate("c4"),
            Candidate("c5"),
            Candidate("c6")
        };

        const string providerOutput = """
        {"results":[
          {"correlationId":"c2","actionType":"None","requiresAction":false,"confidence":0.93,"deadline":null,"reasonCodes":["INFORMATIONAL"],"isUncertain":false},
          {"correlationId":"c1","actionType":"Reply","requiresAction":true,"confidence":0.88,"deadline":null,"reasonCodes":["EXPLICIT_REPLY_REQUEST"],"isUncertain":false},
          {"correlationId":"c3","actionType":"DeleteEverything","requiresAction":true,"confidence":0.80,"deadline":null,"reasonCodes":[],"isUncertain":false},
          {"correlationId":"c4","actionType":"Review","requiresAction":true,"confidence":1.4,"deadline":null,"reasonCodes":[],"isUncertain":false},
          {"correlationId":"c5","actionType":"None","requiresAction":true,"confidence":0.70,"deadline":null,"reasonCodes":[],"isUncertain":false},
          {"correlationId":"c5","actionType":"Reply","requiresAction":true,"confidence":0.75,"deadline":null,"reasonCodes":[],"isUncertain":false}
        ]}
        """;

        var results = parser.Parse(candidates, providerOutput, "provider/test");

        Ensure(results.Count == candidates.Length,
            "El parser debe devolver exactamente un resultado por candidato esperado.");
        Ensure(results.Select(x => x.CorrelationId).SequenceEqual(candidates.Select(x => x.CorrelationId)),
            "La salida debe conservar el orden de los candidatos, no el orden del proveedor.");

        var reply = results[0];
        Ensure(reply.Assessment.ActionType == CommunicationActionType.Reply
               && reply.Assessment.RequiresAction
               && Math.Abs(reply.Assessment.Confidence - 0.88) < 0.0001
               && !reply.Assessment.IsUncertain,
            "Una respuesta válida Reply debe mapearse sin perder confianza.");
        Ensure(reply.Assessment.ReasonCodes.Contains("SEMANTIC_PROVIDER_RESULT"),
            "Una respuesta válida debe quedar identificada como resultado del proveedor.");

        var none = results[1];
        Ensure(none.Assessment.ActionType == CommunicationActionType.None
               && !none.Assessment.RequiresAction
               && !none.Assessment.IsUncertain,
            "None válido debe conservarse como no accionable.");

        EnsureUncertain(results[2], "SEMANTIC_INVALID_ACTION");
        EnsureUncertain(results[3], "SEMANTIC_INVALID_CONFIDENCE");
        EnsureUncertain(results[4], "SEMANTIC_DUPLICATE_CORRELATION");
        EnsureUncertain(results[5], "SEMANTIC_MISSING_CORRELATION");

        var malformed = parser.Parse([Candidate("x1")], "not-json", "provider/test");
        EnsureUncertain(malformed.Single(), "SEMANTIC_PARSE_FAILURE");

        var empty = parser.Parse([], providerOutput, "provider/test");
        Ensure(empty.Count == 0, "Sin candidatos no debe haber resultados sintéticos.");
    }

    private static SemanticCommunicationCandidate Candidate(string id) => new(
        id, $"conv-{id}", $"msg-{id}", DateTimeOffset.UtcNow,
        "Subject", "Snippet", [], IsRead: true, IsDirectRecipient: true,
        ReplyDiscouragedSender: false, Categories: [], StructuralReasonCodes: ["SEMANTIC_REVIEW_REQUIRED"]);

    private static void EnsureUncertain(SemanticAnalysisResult result, string reason)
    {
        Ensure(result.Assessment.ActionType == CommunicationActionType.Unknown,
            $"{result.CorrelationId} inválido debe convertirse en Unknown.");
        Ensure(!result.Assessment.RequiresAction && result.Assessment.Confidence == 0,
            $"{result.CorrelationId} inválido no debe inventar acción ni confianza.");
        Ensure(result.Assessment.IsUncertain,
            $"{result.CorrelationId} inválido debe quedar uncertain.");
        Ensure(result.Assessment.ReasonCodes.Contains(reason)
               && result.Assessment.ReasonCodes.Contains("SEMANTIC_UNCERTAIN"),
            $"{result.CorrelationId} debe explicar la causa de incertidumbre.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
