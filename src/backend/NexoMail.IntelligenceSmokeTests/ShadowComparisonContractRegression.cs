using System.Reflection;
using System.Runtime.CompilerServices;
using NexoMail.Application.Intelligence;

namespace NexoMail.IntelligenceSmokeTests;

internal static class ShadowComparisonContractRegression
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var applicationAssembly = typeof(ICommunicationIntelligenceService).Assembly;
        var serviceContract = applicationAssembly.GetType(
            "NexoMail.Application.Intelligence.IIntelligenceShadowComparisonService");
        var resultType = applicationAssembly.GetType(
            "NexoMail.Application.Intelligence.IntelligenceShadowComparisonResult");
        var itemType = applicationAssembly.GetType(
            "NexoMail.Application.Intelligence.IntelligenceShadowComparisonItem");

        Ensure(serviceContract is not null,
            "Debe existir un contrato para comparar Control Center y Nexo Intelligence sin alterar producción.");
        Ensure(resultType is not null,
            "Debe existir un resultado resumido de comparación shadow.");
        Ensure(itemType is not null,
            "Debe existir un detalle por conversación para diagnosticar diferencias shadow.");

        var method = serviceContract!.GetMethod("CompareAsync", BindingFlags.Public | BindingFlags.Instance);
        Ensure(method is not null,
            "El comparador debe exponer CompareAsync.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
