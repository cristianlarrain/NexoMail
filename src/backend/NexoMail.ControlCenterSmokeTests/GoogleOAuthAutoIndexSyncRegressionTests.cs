using System.Runtime.CompilerServices;

namespace NexoMail.ControlCenterSmokeTests;

internal static class GoogleOAuthAutoIndexSyncRegressionTests
{
    [ModuleInitializer]
    public static void Initialize()
    {
        var serviceSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "backend",
            "NexoMail.Infrastructure",
            "Google",
            "GoogleOAuthService.cs"));

        Ensure(
            serviceSource.Contains("GmailMetadataIndexService? metadataIndexService", StringComparison.Ordinal),
            "GoogleOAuthService debe recibir el servicio de índice para sincronizar inmediatamente después de autorizar.");
        Ensure(
            serviceSource.Contains("await metadataIndexService.SyncAsync(", StringComparison.Ordinal),
            "GoogleOAuthService debe disparar una sincronización inmediata del índice después de guardar la credencial renovada.");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "backend", "NexoMail.Api", "Program.cs")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("No fue posible localizar la raíz del repositorio para validar la sincronización posterior a OAuth.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
