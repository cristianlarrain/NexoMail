using System.Runtime.CompilerServices;
using NexoMail.Infrastructure.Intelligence;

namespace NexoMail.IntelligenceSmokeTests;

internal static class ShadowComparisonServiceImplementationRegression
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var infrastructureAssembly = typeof(CommunicationIntelligenceService).Assembly;
        var implementation = infrastructureAssembly.GetType(
            "NexoMail.Infrastructure.Intelligence.IntelligenceShadowComparisonService");

        Ensure(implementation is not null,
            "Debe existir la implementación del servicio de comparación shadow.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
