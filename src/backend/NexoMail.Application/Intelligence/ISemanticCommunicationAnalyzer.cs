namespace NexoMail.Application.Intelligence;

public interface ISemanticCommunicationAnalyzer
{
    Task<IReadOnlyList<SemanticAnalysisResult>> AnalyzeAsync(
        IReadOnlyList<SemanticCommunicationCandidate> candidates,
        CancellationToken cancellationToken = default);
}
