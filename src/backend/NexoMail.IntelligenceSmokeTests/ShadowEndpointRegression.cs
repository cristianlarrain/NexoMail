using System.Runtime.CompilerServices;

namespace NexoMail.IntelligenceSmokeTests;

internal static class ShadowEndpointRegression
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
        var authorizedMailGroup = source.IndexOf(
            "var mail = api.MapGroup(\"/mail\").RequireAuthorization();",
            StringComparison.Ordinal);
        var shadowRoute = source.IndexOf(
            "mail.MapGet(\"/intelligence/shadow\"",
            StringComparison.Ordinal);

        Ensure(authorizedMailGroup >= 0,
            "El endpoint shadow debe heredar autenticación desde el grupo /mail.");
        Ensure(shadowRoute > authorizedMailGroup,
            "Debe existir GET /api/mail/intelligence/shadow dentro del grupo /mail autenticado.");
        Ensure(source.Contains("ICommunicationIntelligenceReader reader", StringComparison.Ordinal),
            "El endpoint shadow debe consumir ICommunicationIntelligenceReader, no Gmail ni el clasificador legado.");
        Ensure(source.Contains("reader.AnalyzeAsync(DateTimeOffset.UtcNow, ct)", StringComparison.Ordinal),
            "El endpoint shadow debe ejecutar análisis de solo lectura con la hora actual de evaluación.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
