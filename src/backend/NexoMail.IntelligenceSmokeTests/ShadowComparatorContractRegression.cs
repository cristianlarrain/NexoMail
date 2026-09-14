using System.Reflection;
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

        var method = comparator!.GetMethod("Compare", BindingFlags.Public | BindingFlags.Instance);
        Ensure(method is not null,
            "El comparador shadow puro debe exponer Compare sobre snapshots ya calculados.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
