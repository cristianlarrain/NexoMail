using System.Text.Json;
using NexoMail.Application.Intelligence;

namespace NexoMail.Infrastructure.Intelligence;

public sealed class SemanticResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyList<SemanticAnalysisResult> Parse(
        IReadOnlyList<SemanticCommunicationCandidate> expectedCandidates,
        string providerOutput,
        string providerVersion)
    {
        ArgumentNullException.ThrowIfNull(expectedCandidates);
        if (expectedCandidates.Count == 0) return [];

        ProviderEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ProviderEnvelope>(CleanJson(providerOutput), JsonOptions);
        }
        catch (JsonException)
        {
            return expectedCandidates
                .Select(candidate => Uncertain(candidate.CorrelationId, providerVersion, "SEMANTIC_PARSE_FAILURE"))
                .ToArray();
        }

        if (envelope?.Results is null)
        {
            return expectedCandidates
                .Select(candidate => Uncertain(candidate.CorrelationId, providerVersion, "SEMANTIC_PARSE_FAILURE"))
                .ToArray();
        }

        var groups = envelope.Results
            .Where(result => !string.IsNullOrWhiteSpace(result.CorrelationId))
            .GroupBy(result => result.CorrelationId!.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var output = new List<SemanticAnalysisResult>(expectedCandidates.Count);
        foreach (var candidate in expectedCandidates)
        {
            if (!groups.TryGetValue(candidate.CorrelationId, out var matches))
            {
                output.Add(Uncertain(candidate.CorrelationId, providerVersion, "SEMANTIC_MISSING_CORRELATION"));
                continue;
            }

            if (matches.Length != 1)
            {
                output.Add(Uncertain(candidate.CorrelationId, providerVersion, "SEMANTIC_DUPLICATE_CORRELATION"));
                continue;
            }

            output.Add(ParseOne(candidate.CorrelationId, matches[0], providerVersion));
        }

        return output;
    }

    private static SemanticAnalysisResult ParseOne(
        string correlationId,
        ProviderResult payload,
        string providerVersion)
    {
        if (!TryAction(payload.ActionType, out var actionType))
            return Uncertain(correlationId, providerVersion, "SEMANTIC_INVALID_ACTION");

        if (payload.Confidence is null
            || double.IsNaN(payload.Confidence.Value)
            || double.IsInfinity(payload.Confidence.Value)
            || payload.Confidence.Value < 0
            || payload.Confidence.Value > 1)
            return Uncertain(correlationId, providerVersion, "SEMANTIC_INVALID_CONFIDENCE");

        var expectedRequiresAction = actionType is CommunicationActionType.Reply
            or CommunicationActionType.Confirm
            or CommunicationActionType.Review
            or CommunicationActionType.CompleteTask;
        var mustBeUncertain = actionType == CommunicationActionType.Unknown;

        if ((actionType == CommunicationActionType.None && payload.RequiresAction)
            || (expectedRequiresAction && !payload.RequiresAction))
            return Uncertain(correlationId, providerVersion, "SEMANTIC_CONTRADICTORY_RESULT");

        if (mustBeUncertain && !payload.IsUncertain)
            return Uncertain(correlationId, providerVersion, "SEMANTIC_CONTRADICTORY_RESULT");

        DateTimeOffset? deadline = null;
        if (!string.IsNullOrWhiteSpace(payload.Deadline))
        {
            if (!DateTimeOffset.TryParse(payload.Deadline, out var parsedDeadline))
                return Uncertain(correlationId, providerVersion, "SEMANTIC_INVALID_DEADLINE");
            deadline = parsedDeadline;
        }

        var reasons = (payload.ReasonCodes ?? [])
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Select(reason => reason.Trim())
            .Append("SEMANTIC_PROVIDER_RESULT")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (payload.IsUncertain || mustBeUncertain)
        {
            reasons = reasons
                .Append("SEMANTIC_UNCERTAIN")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        return new SemanticAnalysisResult(
            correlationId,
            new SemanticActionabilityAssessment(
                actionType,
                payload.RequiresAction && !mustBeUncertain,
                payload.Confidence.Value,
                deadline,
                reasons,
                payload.IsUncertain || mustBeUncertain),
            providerVersion);
    }

    private static bool TryAction(string? value, out CommunicationActionType actionType)
    {
        actionType = value?.Trim() switch
        {
            "None" => CommunicationActionType.None,
            "Reply" => CommunicationActionType.Reply,
            "Confirm" => CommunicationActionType.Confirm,
            "Review" => CommunicationActionType.Review,
            "CompleteTask" => CommunicationActionType.CompleteTask,
            "Unknown" => CommunicationActionType.Unknown,
            _ => (CommunicationActionType)(-1)
        };

        return actionType is CommunicationActionType.None
            or CommunicationActionType.Reply
            or CommunicationActionType.Confirm
            or CommunicationActionType.Review
            or CommunicationActionType.CompleteTask
            or CommunicationActionType.Unknown;
    }

    private static SemanticAnalysisResult Uncertain(
        string correlationId,
        string providerVersion,
        string reasonCode) =>
        new(
            correlationId,
            new SemanticActionabilityAssessment(
                CommunicationActionType.Unknown,
                RequiresAction: false,
                Confidence: 0,
                Deadline: null,
                ReasonCodes: [reasonCode, "SEMANTIC_UNCERTAIN"],
                IsUncertain: true),
            providerVersion);

    private static string CleanJson(string? value)
    {
        var clean = value?.Trim() ?? string.Empty;
        if (clean.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = clean.IndexOf('\n');
            if (firstNewLine >= 0) clean = clean[(firstNewLine + 1)..];
            var lastFence = clean.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0) clean = clean[..lastFence];
        }
        return clean.Trim();
    }

    private sealed class ProviderEnvelope
    {
        public ProviderResult[]? Results { get; set; }
    }

    private sealed class ProviderResult
    {
        public string? CorrelationId { get; set; }
        public string? ActionType { get; set; }
        public bool RequiresAction { get; set; }
        public double? Confidence { get; set; }
        public string? Deadline { get; set; }
        public string[]? ReasonCodes { get; set; }
        public bool IsUncertain { get; set; }
    }
}
