using Microsoft.Extensions.Options;
using NexoMail.Application.Intelligence;

namespace NexoMail.Infrastructure.Intelligence;

public sealed class SemanticIntelligenceShadowService(
    SemanticCommunicationCandidateSource source,
    ISemanticCommunicationAnalyzer analyzer,
    IOptions<SemanticIntelligenceOptions> options)
    : ISemanticIntelligenceShadowService
{
    private static readonly HashSet<string> ParseFailureReasons = new(StringComparer.Ordinal)
    {
        "SEMANTIC_PARSE_FAILURE",
        "SEMANTIC_MISSING_CORRELATION",
        "SEMANTIC_DUPLICATE_CORRELATION",
        "SEMANTIC_INVALID_ACTION",
        "SEMANTIC_INVALID_CONFIDENCE",
        "SEMANTIC_INVALID_DEADLINE",
        "SEMANTIC_CONTRADICTORY_RESULT"
    };

    public async Task<SemanticShadowResult> AnalyzeAsync(
        Guid? accountId,
        int? limit,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = options.Value;
        var maximum = Math.Max(1, settings.MaxCandidatesPerRequest);
        var effectiveLimit = Math.Clamp(limit ?? maximum, 1, maximum);

        if (!settings.Enabled)
        {
            return new SemanticShadowResult(
                Enabled: false,
                RequestedLimit: effectiveLimit,
                Items: [],
                Diagnostics: EmptyDiagnostics(),
                GeneratedAt: evaluatedAt);
        }

        var candidates = await source.ReadAsync(
            evaluatedAt,
            accountId,
            effectiveLimit,
            cancellationToken);

        if (candidates.Count == 0)
        {
            return new SemanticShadowResult(
                Enabled: true,
                RequestedLimit: effectiveLimit,
                Items: [],
                Diagnostics: EmptyDiagnostics(),
                GeneratedAt: evaluatedAt);
        }

        var analysis = await analyzer.AnalyzeAsync(
            candidates.Select(snapshot => snapshot.Candidate).ToArray(),
            cancellationToken);

        var analysisByCorrelation = analysis
            .GroupBy(result => result.CorrelationId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var items = new List<SemanticShadowItem>(candidates.Count);
        foreach (var snapshot in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SemanticAnalysisResult result;
            if (!analysisByCorrelation.TryGetValue(snapshot.Candidate.CorrelationId, out var matches)
                || matches.Length != 1)
            {
                result = MissingCorrelation(snapshot.Candidate.CorrelationId);
            }
            else
            {
                result = matches[0];
            }

            items.Add(new SemanticShadowItem(
                snapshot.AccountId,
                snapshot.ConversationId,
                snapshot.LatestMessageId,
                snapshot.LatestActivityAt,
                result.Assessment,
                result.ProviderVersion));
        }

        var orderedItems = items
            .OrderByDescending(item => item.Assessment.RequiresAction && !item.Assessment.IsUncertain)
            .ThenByDescending(item => item.Assessment.Confidence)
            .ThenByDescending(item => item.LatestActivityAt)
            .ThenBy(item => item.AccountId)
            .ThenBy(item => item.ConversationId, StringComparer.Ordinal)
            .ToArray();

        return new SemanticShadowResult(
            Enabled: true,
            RequestedLimit: effectiveLimit,
            Items: orderedItems,
            Diagnostics: BuildDiagnostics(items),
            GeneratedAt: evaluatedAt);
    }

    private static SemanticShadowDiagnostics BuildDiagnostics(IReadOnlyList<SemanticShadowItem> items)
    {
        var actionTypeCounts = items
            .GroupBy(item => item.Assessment.ActionType)
            .ToDictionary(group => group.Key, group => group.Count());

        var confidenceBuckets = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["0.00-0.49"] = 0,
            ["0.50-0.74"] = 0,
            ["0.75-0.89"] = 0,
            ["0.90-1.00"] = 0
        };

        foreach (var item in items)
            confidenceBuckets[ConfidenceBucket(item.Assessment.Confidence)]++;

        return new SemanticShadowDiagnostics(
            CandidateCount: items.Count,
            ClassifiedActionCount: items.Count(item => !item.Assessment.IsUncertain && item.Assessment.RequiresAction),
            ClassifiedNoActionCount: items.Count(item => !item.Assessment.IsUncertain && !item.Assessment.RequiresAction),
            UncertainCount: items.Count(item => item.Assessment.IsUncertain),
            ActionTypeCounts: actionTypeCounts,
            ConfidenceBuckets: confidenceBuckets,
            DeadlineDetectedCount: items.Count(item => item.Assessment.Deadline.HasValue),
            ProviderFailureCount: items.Count(item => item.Assessment.ReasonCodes.Contains("SEMANTIC_PROVIDER_FAILURE", StringComparer.Ordinal)),
            ParseFailureCount: items.Count(item => item.Assessment.ReasonCodes.Any(ParseFailureReasons.Contains)));
    }

    private static string ConfidenceBucket(double confidence)
    {
        var value = Math.Clamp(confidence, 0, 1);
        if (value < 0.50) return "0.00-0.49";
        if (value < 0.75) return "0.50-0.74";
        if (value < 0.90) return "0.75-0.89";
        return "0.90-1.00";
    }

    private static SemanticShadowDiagnostics EmptyDiagnostics() =>
        new(
            CandidateCount: 0,
            ClassifiedActionCount: 0,
            ClassifiedNoActionCount: 0,
            UncertainCount: 0,
            ActionTypeCounts: new Dictionary<CommunicationActionType, int>(),
            ConfidenceBuckets: new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["0.00-0.49"] = 0,
                ["0.50-0.74"] = 0,
                ["0.75-0.89"] = 0,
                ["0.90-1.00"] = 0
            },
            DeadlineDetectedCount: 0,
            ProviderFailureCount: 0,
            ParseFailureCount: 0);

    private static SemanticAnalysisResult MissingCorrelation(string correlationId) =>
        new(
            correlationId,
            new SemanticActionabilityAssessment(
                CommunicationActionType.Unknown,
                RequiresAction: false,
                Confidence: 0,
                Deadline: null,
                ReasonCodes: ["SEMANTIC_MISSING_CORRELATION", "SEMANTIC_UNCERTAIN"],
                IsUncertain: true),
            "shadow/local-fallback");
}
