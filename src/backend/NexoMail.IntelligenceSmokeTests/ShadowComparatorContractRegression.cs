using System.Runtime.CompilerServices;
using NexoMail.Infrastructure.Intelligence;

namespace NexoMail.IntelligenceSmokeTests;

internal static class ShadowComparatorContractRegression
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var infrastructureAssembly = typeof(CommunicationIntelligenceService).Assembly;
        var comparator = infrastructureAssembly.GetType(
            "NexoMail.Infrastructure.Intelligence.IntelligenceShadowComparator");

        Ensure(comparator is not null,
            "Debe existir un comparador shadow puro, separado del acceso a Gmail y base de datos.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
