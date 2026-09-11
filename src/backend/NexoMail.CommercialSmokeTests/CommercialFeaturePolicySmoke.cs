using System.Runtime.CompilerServices;
using NexoMail.Domain;

internal static class CommercialFeaturePolicySmoke
{
    [ModuleInitializer]
    internal static void Verify()
    {
        Ensure(CommercialFeaturePolicy.RequiredEntitlement("GET", "/api/mail/messages") == CommercialEntitlements.UnifiedMail,
            "Leer la bandeja debe exigir Bandeja unificada.");
        Ensure(CommercialFeaturePolicy.RequiredEntitlement("POST", "/api/mail/send") == CommercialEntitlements.MailActions,
            "Enviar correo debe exigir acciones de correo.");
        Ensure(CommercialFeaturePolicy.RequiredEntitlement("POST", "/api/mail/messages/abc/reply") == CommercialEntitlements.MailActions,
            "Responder debe exigir acciones de correo.");
        Ensure(CommercialFeaturePolicy.RequiredEntitlement("GET", "/api/mail/control-center") == CommercialEntitlements.ControlCenterBasic,
            "El Centro de Control debe exigir la capacidad básica.");
        Ensure(CommercialFeaturePolicy.RequiredEntitlement("GET", "/api/mail/control-center/activity") == CommercialEntitlements.AdvancedAnalytics,
            "La actividad avanzada debe exigir estadísticas avanzadas.");
        Ensure(CommercialFeaturePolicy.RequiredEntitlement("POST", "/api/mail/control-center/tracking/abc/123") == CommercialEntitlements.TrackingBasic,
            "El seguimiento manual debe exigir seguimiento esencial.");
        Ensure(CommercialFeaturePolicy.RequiredEntitlement("POST", "/api/mail/ai/write") == CommercialEntitlements.NexiAi,
            "Las operaciones de IA deben exigir Nexi e IA.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
