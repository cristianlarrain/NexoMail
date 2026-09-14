using Microsoft.Extensions.Options;
using NexoMail.Application.Intelligence;

namespace NexoMail.Infrastructure.Intelligence;

public sealed class OpenAiSemanticCommunicationAnalyzer(
    AiResponseClient responseClient,
    SemanticPromptBuilder promptBuilder,
    SemanticResponseParser responseParser,
    IOptions<SemanticIntelligenceOptions> options)
    : ISemanticCommunicationAnalyzer
{
    private const string OperationType = "intelligence_semantic_classification";
    private const string ProviderVersion = "openai/responses";

    public async Task<IReadOnlyList<SemanticAnalysisResult>> AnalyzeAsync(
        IReadOnlyList<SemanticCommunicationCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0) return [];

        var settings = options.Value;
        var batches = BuildBatches(
            candidates,
            Math.Max(1, settings.MaxCandidatesPerBatch),
            Math.Max(1_000, settings.MaxPromptCharacters));
        var results = new List<SemanticAnalysisResult>(candidates.Count);

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var prompt = promptBuilder.Build(batch);
                var output = await responseClient.SendAsync(
                    OperationType,
                    prompt.Instructions,
                    prompt.Input,
                    maxOutputTokens: 1_800,
                    reasoningEffort: "low",
                    cancellationToken);

                results.AddRange(responseParser.Parse(batch, output, ProviderVersion));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                results.AddRange(batch.Select(ProviderFailure));
            }
        }

        return results;
    }

    private IReadOnlyList<IReadOnlyList<SemanticCommunicationCandidate>> BuildBatches(
        IReadOnlyList<SemanticCommunicationCandidate> candidates,
        int maxCandidatesPerBatch,
        int maxPromptCharacters)
    {
        var batches = new List<IReadOnlyList<SemanticCommunicationCandidate>>();
        var current = new List<SemanticCommunicationCandidate>();

        foreach (var candidate in candidates)
        {
            if (current.Count == 0)
            {
                current.Add(candidate);
                continue;
            }

            var tentative = current.Append(candidate).ToArray();
            var prompt = promptBuilder.Build(tentative);
            var exceedsCount = tentative.Length > maxCandidatesPerBatch;
            var exceedsPrompt = prompt.Instructions.Length + prompt.Input.Length > maxPromptCharacters;

            if (exceedsCount || exceedsPrompt)
            {
                batches.Add(current.ToArray());
                current = [candidate];
            }
            else
            {
                current.Add(candidate);
            }
        }

        if (current.Count > 0)
            batches.Add(current.ToArray());

        return batches;
    }

    private static SemanticAnalysisResult ProviderFailure(SemanticCommunicationCandidate candidate) =>
        new(
            candidate.CorrelationId,
            new SemanticActionabilityAssessment(
                CommunicationActionType.Unknown,
                RequiresAction: false,
                Confidence: 0,
                Deadline: null,
                ReasonCodes: ["SEMANTIC_PROVIDER_FAILURE", "SEMANTIC_UNCERTAIN"],
                IsUncertain: true),
            ProviderVersion);
}
