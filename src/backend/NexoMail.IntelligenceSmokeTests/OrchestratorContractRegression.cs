using System.Runtime.CompilerServices;

namespace NexoMail.IntelligenceSmokeTests;

internal static class OrchestratorContractRegression
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var infrastructureAssembly = typeof(NexoMail.Infrastructure.Data.NexoMailDbContext).Assembly;
        if (infrastructureAssembly.GetType("NexoMail.Infrastructure.Intelligence.CommunicationIntelligenceService") is null)
        {
            throw new InvalidOperationException(
                "CommunicationIntelligenceService debe existir como orquestador independiente del proveedor.");
        }
    }
}
