using System.Runtime.CompilerServices;

namespace NexoMail.IntelligenceSmokeTests;

internal static class SemanticEndpointRegression
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    private static void Run()
    {
        var programPath = FindRepoFile("src", "backend", "NexoMail.Api", "Program.cs");
        var program = File.ReadAllText(programPath);

        var authenticatedGroup = "var mail = api.MapGroup(\"/mail\").RequireAuthorization();";
        var endpoint = "mail.MapPost(\"/intelligence/semantic-shadow\"";

        Ensure(program.Contains(authenticatedGroup, StringComparison.Ordinal),
            "El grupo /mail debe seguir requiriendo autenticación.");
        Ensure(program.Contains(endpoint, StringComparison.Ordinal),
            "Debe existir POST /api/mail/intelligence/semantic-shadow dentro del grupo autenticado.");
        Ensure(program.Contains("ISemanticIntelligenceShadowService", StringComparison.Ordinal),
            "El endpoint debe resolver el servicio shadow semántico.");
        Ensure(program.Contains("request.AccountId", StringComparison.Ordinal)
               && program.Contains("request.Limit", StringComparison.Ordinal)
               && program.Contains("DateTimeOffset.UtcNow", StringComparison.Ordinal),
            "El endpoint debe propagar cuenta, límite y tiempo de evaluación.");
        Ensure(program.Contains("public sealed record SemanticShadowRequest(Guid? AccountId, int? Limit);", StringComparison.Ordinal),
            "El contrato HTTP debe mantenerse en scope API.");
        Ensure(program.Contains("mail.MapGet(\"/control-center\"", StringComparison.Ordinal),
            "La ruta visible del Control Center debe permanecer intacta.");

        var start = program.IndexOf(endpoint, StringComparison.Ordinal);
        if (start >= 0)
        {
            var end = program.IndexOf("});", start, StringComparison.Ordinal);
            var block = end > start ? program[start..(end + 3)] : program[start..];
            Ensure(!block.Contains("Gmail", StringComparison.OrdinalIgnoreCase)
                   && !block.Contains("IMailGateway", StringComparison.Ordinal)
                   && !block.Contains("SendAsync", StringComparison.Ordinal)
                   && !block.Contains("Delete", StringComparison.OrdinalIgnoreCase)
                   && !block.Contains("Move", StringComparison.OrdinalIgnoreCase),
                "El endpoint shadow no debe llamar proveedores de correo ni mutar mensajes.");
        }
    }

    private static string FindRepoFile(params string[] relativeParts)
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. relativeParts]);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new InvalidOperationException("No fue posible localizar Program.cs.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
