using System.Reflection;
using System.Runtime.CompilerServices;
using NexoMail.Application.Intelligence;
using NexoMail.Infrastructure.Intelligence;

namespace NexoMail.IntelligenceSmokeTests;

internal static class ShadowReaderContractRegression
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var applicationAssembly = typeof(ICommunicationIntelligenceService).Assembly;
        var infrastructureAssembly = typeof(CommunicationIntelligenceService).Assembly;

        var readerContract = applicationAssembly.GetType(
            "NexoMail.Application.Intelligence.ICommunicationIntelligenceReader");
        var snapshotType = applicationAssembly.GetType(
            "NexoMail.Application.Intelligence.CommunicationIntelligenceSnapshot");
        var readerImplementation = infrastructureAssembly.GetType(
            "NexoMail.Infrastructure.Intelligence.LocalIndexCommunicationIntelligenceReader");

        Ensure(readerContract is not null,
            "Debe existir un contrato provider-agnostic para leer análisis de Nexo Intelligence.");
        Ensure(snapshotType is not null,
            "Debe existir un snapshot provider-agnostic para exponer resultados de análisis.");
        Ensure(readerImplementation is not null,
            "Debe existir un lector shadow del índice local separado del Control Center actual.");

        var method = readerContract!.GetMethod("AnalyzeAsync", BindingFlags.Public | BindingFlags.Instance);
        Ensure(method is not null,
            "El lector debe exponer AnalyzeAsync para ejecutar análisis de solo lectura.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
