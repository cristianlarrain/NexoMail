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
        // The diagnostic legacy snapshot evaluates the cached local Gmail index even
        // when its freshness status is stale/error, without changing /control-center.
        var legacy = await legacyService.GetDiagnosticSnapshotAsync(accountId, cancellationToken);
        var intelligence = await intelligenceReader.AnalyzeAsync(evaluatedAt, cancellationToken);

        var legacyAccountIds = legacy.Accounts
            .Select(account => account.AccountId)
            .ToHashSet();

        // This cutoff exists only to compare both engines on the same operational
        // window used by the current Control Center. It is not a Core Intelligence rule.
        // Filtering by legacyAccountIds keeps the comparison on the exact Gmail account
        // universe represented by the diagnostic legacy snapshot.
        var cutoff = evaluatedAt.AddDays(-ComparisonLookbackDays);
        var comparableIntelligence = intelligence
            .Where(item => item.LatestActivityAt >= cutoff)
            .Where(item => legacyAccountIds.Contains(item.AccountId))
            .ToArray();

        return comparator.Compare(
            legacy,
            comparableIntelligence,
            accountId,
            evaluatedAt);
    }
}
