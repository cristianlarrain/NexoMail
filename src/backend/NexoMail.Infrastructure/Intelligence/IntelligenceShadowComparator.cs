using NexoMail.Application.Intelligence;
using NexoMail.Domain;

namespace NexoMail.Infrastructure.Intelligence;

public sealed class IntelligenceShadowComparator
{
    public IntelligenceShadowComparisonResult Compare(
        ControlCenterSnapshot legacy,
        IReadOnlyList<CommunicationIntelligenceSnapshot> intelligence,
        Guid? accountId,
        DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        ArgumentNullException.ThrowIfNull(intelligence);

        return new IntelligenceShadowComparisonResult(
            LegacyPendingCount: 0,
            IntelligencePendingCount: 0,
            AgreementPendingCount: 0,
            LegacyOnlyCount: 0,
            IntelligenceOnlyCount: 0,
            DirectionMismatchCount: 0,
            Items: Array.Empty<IntelligenceShadowComparisonItem>(),
            GeneratedAt: generatedAt,
            EngineVersion: intelligence.FirstOrDefault()?.Intelligence.EngineVersion ?? "unknown");
    }
}
