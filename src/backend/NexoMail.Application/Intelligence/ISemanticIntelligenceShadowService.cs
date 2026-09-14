namespace NexoMail.Application.Intelligence;

public interface ISemanticIntelligenceShadowService
{
    Task<SemanticShadowResult> AnalyzeAsync(
        Guid? accountId,
        int? limit,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken = default);
}
