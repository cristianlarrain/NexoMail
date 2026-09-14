using System.Runtime.CompilerServices;

namespace NexoMail.ControlCenterSmokeTests;

internal static class GoogleOAuthAutoIndexSyncRegressionTests
{
    [ModuleInitializer]
    public static void Initialize()
    {
        var programSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "backend", "NexoMail.Api", "Program.cs"));

        Ensure(
            programSource.Contains("GmailMetadataIndexService indexService", StringComparison.Ordinal),
            "El callback OAuth de Google debe recibir el servicio de índice para sincronizar inmediatamente después de autorizar.");
        Ensure(
            programSource.Contains("await indexService.SyncAsync(", StringComparison.Ordinal),
            "El callback OAuth de Google debe disparar una sincronización inmediata del índice después de guardar la credencial renovada.");
        Ensure(
            programSource.Contains("cache.Invalidate(userContext.UserId.ToString())", StringComparison.Ordinal),
            "Después de sincronizar por OAuth se debe invalidar la caché de lectura para que el Centro de Control refleje el índice nuevo de inmediato.");
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
