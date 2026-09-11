using System.Runtime.CompilerServices;
using NexoMail.Infrastructure.Microsoft;

internal static class MicrosoftOAuthConsentErrorSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    private static void Run()
    {
        const string cancelled = "Microsoft canceló o rechazó la autorización.";
        const string adminApproval = "La organización requiere aprobación de un administrador para conectar esta cuenta de Microsoft 365.";
        const string generic = "Microsoft no pudo completar la autorización. Inténtalo nuevamente.";
        const string sensitiveMarker = "SENSITIVE-PROVIDER-DESCRIPTION-DO-NOT-ECHO";

        Ensure(
            MicrosoftOAuthService.AuthorizationFailureMessage("access_denied", null) == cancelled,
            "access_denied debe usar el mensaje seguro de cancelación o rechazo.");

        foreach (var description in new[]
                 {
                     "AADSTS65001: consent required",
                     "AADSTS90094: admin consent is required",
                     "This app needs admin approval",
                     "Contact your administrator to approve this application"
                 })
        {
            Ensure(
                MicrosoftOAuthService.AuthorizationFailureMessage("access_denied", description) == adminApproval,
                "Los errores de consentimiento administrativo deben clasificarse explícitamente.");
        }

        var arbitraryDescription = $"unexpected provider failure {sensitiveMarker}";
        var arbitrary = MicrosoftOAuthService.AuthorizationFailureMessage("server_error", arbitraryDescription);
        Ensure(arbitrary == generic, "Los errores arbitrarios deben usar un mensaje genérico seguro.");
        Ensure(!arbitrary.Contains(sensitiveMarker, StringComparison.Ordinal),
            "La descripción arbitraria del proveedor nunca debe devolverse al usuario.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
