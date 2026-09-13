using System.Runtime.CompilerServices;
using NexoMail.Infrastructure.Imap;
using NexoMail.Infrastructure.Microsoft;

internal static class MailProviderBetaSmoke
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var microsoft = new Microsoft365Options();
        Assert(microsoft.AuthorityTenant == "organizations", "Microsoft 365 debe limitarse a cuentas organizacionales durante la marcha blanca.");

        var scope = MicrosoftOAuthService.Scope;
        foreach (var required in new[] { "offline_access", "User.Read", "Mail.ReadWrite", "Mail.Send" })
            Assert(scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(required, StringComparer.Ordinal), $"Falta el permiso Microsoft requerido: {required}.");
        Assert(!scope.Contains("Calendars", StringComparison.OrdinalIgnoreCase), "La marcha blanca no debe pedir permisos de calendario.");
        Assert(!scope.Contains("Files", StringComparison.OrdinalIgnoreCase), "La marcha blanca no debe pedir permisos de archivos.");

        var consentMessage = MicrosoftOAuthErrors.ToUserMessage("AADSTS65001: admin consent required");
        Assert(consentMessage.Contains("administrador", StringComparison.OrdinalIgnoreCase), "El rechazo por consentimiento institucional debe explicar la autorización administrativa.");

        Assert(ImapConnectionRequest.NormalizeSecurity("SSL/TLS") == "ssl", "SSL/TLS debe normalizar a ssl.");
        Assert(ImapConnectionRequest.NormalizeSecurity("STARTTLS") == "starttls", "STARTTLS debe normalizar a starttls.");
        AssertThrows(() => ImapConnectionRequest.ValidateHost("localhost"), "IMAP no debe aceptar localhost.");
        AssertThrows(() => ImapConnectionRequest.ValidateHost("127.0.0.1"), "IMAP no debe aceptar direcciones IP literales.");
        AssertThrows(() => ImapConnectionRequest.ValidatePort(0), "IMAP no debe aceptar puertos fuera de rango.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertThrows(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException(message);
    }
}
