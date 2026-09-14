using System.Runtime.CompilerServices;

namespace NexoMail.IntelligenceSmokeTests;

internal static class ApiRegistrationRegression
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var repositoryRoot = Directory.GetCurrentDirectory();
        var programPath = Path.Combine(
            repositoryRoot,
            "src",
            "backend",
            "NexoMail.Api",
            "Program.cs");

        Ensure(File.Exists(programPath),
            "El smoke test debe poder inspeccionar NexoMail.Api/Program.cs desde la raíz del repositorio.");

        var source = File.ReadAllText(programPath);
        Ensure(source.Contains("builder.Services.AddNexoMailIntelligence();", StringComparison.Ordinal),
            "Program.cs debe registrar explícitamente Nexo Intelligence en el contenedor del API.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
