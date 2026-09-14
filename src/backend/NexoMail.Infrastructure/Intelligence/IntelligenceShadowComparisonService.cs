using NexoMail.Application.Intelligence;
using NexoMail.Infrastructure.Google;

namespace NexoMail.Infrastructure.Intelligence;

public sealed class IntelligenceShadowComparisonService(
    GmailControlCenterService legacyService,
    ICommunicationIntelligenceReader intelligenceReader,
    IntelligenceShadowComparator comparator)
    : IIntelligenceShadowComparisonService
{
    private const int ComparisonLookbackDays = 14;

    public async Task<IntelligenceShadowComparisonResult> CompareAsync(
        Guid? accountId,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken = default)
    {
        // These reads are intentionally sequential because both services can share
        // the same scoped EF DbContext in an API request.
        var legacy = await legacyService.GetSnapshotAsync(accountId, cancellationToken);
        var intelligence = await intelligenceReader.AnalyzeAsync(evaluatedAt, cancellationToken);

        // This cutoff exists only to compare both engines on the same operational
        // window used by the current Control Center. It is not a Core Intelligence rule.
        var cutoff = evaluatedAt.AddDays(-ComparisonLookbackDays);
        var comparableIntelligence = intelligence
            .Where(item => item.LatestActivityAt >= cutoff)
            .Where(item => !accountId.HasValue || item.AccountId == accountId.Value)
            .ToArray();

        return comparator.Compare(
            legacy,
            comparableIntelligence,
            accountId,
            evaluatedAt);
    }
}
