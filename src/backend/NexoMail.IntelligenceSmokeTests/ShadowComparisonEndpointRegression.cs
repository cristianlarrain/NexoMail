using System.Runtime.CompilerServices;

namespace NexoMail.IntelligenceSmokeTests;

internal static class ShadowComparisonEndpointRegression
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
        var comparisonRoute = source.IndexOf(
            "mail.MapGet(\"/intelligence/compare\"",
            StringComparison.Ordinal);

        Ensure(authorizedMailGroup >= 0,
            "El endpoint de comparación debe heredar autenticación desde el grupo /mail.");
        Ensure(comparisonRoute > authorizedMailGroup,
            "Debe existir GET /api/mail/intelligence/compare dentro del grupo /mail autenticado.");
        Ensure(source.Contains("IIntelligenceShadowComparisonService service", StringComparison.Ordinal),
            "El endpoint de comparación debe consumir el orquestador shadow por su contrato.");
        Ensure(source.Contains("service.CompareAsync(accountId, DateTimeOffset.UtcNow, ct)", StringComparison.Ordinal),
            "El endpoint de comparación debe ejecutar CompareAsync con accountId opcional y la hora actual.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
