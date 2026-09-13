namespace NexoMail.Domain;

public sealed record CommercialEntitlementDefinition(string Code, string Name, string Description);

public static class CommercialEntitlements
{
    public const string UnifiedMail = "unified_mail";
    public const string MailActions = "mail_actions";
    public const string ControlCenterBasic = "control_center_basic";
    public const string TrackingBasic = "tracking_basic";
    public const string NexiAi = "nexi_ai";
    public const string ControlCenterFull = "control_center_full";
    public const string AdvancedAnalytics = "advanced_analytics";
    public const string SignaturesTemplates = "signatures_templates";
    public const string UserManagement = "user_management";
    public const string RolesPolicies = "roles_policies";
    public const string OrganizationAnalytics = "organization_analytics";
    public const string PrioritySupport = "priority_support";
    public const string WhiteLabelBranding = "white_label_branding";
    public const string CustomDomain = "custom_domain";
    public const string CustomLimits = "custom_limits";

    public static IReadOnlyList<CommercialEntitlementDefinition> Definitions { get; } =
    [
        new(UnifiedMail, "Bandeja unificada", "Centraliza las cuentas de correo del usuario."),
        new(MailActions, "Enviar, responder y organizar", "Permite las acciones principales de correo."),
        new(ControlCenterBasic, "Centro de Control básico", "Muestra pendientes y seguimiento esencial."),
        new(TrackingBasic, "Seguimiento esencial", "Permite marcar y gestionar seguimientos básicos."),
        new(NexiAi, "Nexi e IA", "Habilita búsqueda, análisis, redacción y asistencia con IA."),
        new(ControlCenterFull, "Centro de Control completo", "Habilita todas las vistas avanzadas del Centro de Control."),
        new(AdvancedAnalytics, "Estadísticas avanzadas", "Habilita tendencias, actividad e informes avanzados."),
        new(SignaturesTemplates, "Firmas y plantillas", "Habilita firmas y plantillas reutilizables."),
        new(UserManagement, "Gestión de usuarios", "Habilita administración centralizada de usuarios."),
        new(RolesPolicies, "Roles y políticas", "Habilita roles, permisos y políticas organizacionales."),
        new(OrganizationAnalytics, "Estadísticas de organización", "Habilita métricas agregadas de la organización."),
        new(PrioritySupport, "Soporte prioritario", "Identifica cuentas con soporte prioritario."),
        new(WhiteLabelBranding, "Marca White Label", "Habilita identidad visual personalizada."),
        new(CustomDomain, "Dominio personalizado", "Habilita dominio e identidad de acceso personalizados."),
        new(CustomLimits, "Límites personalizados", "Permite límites comerciales definidos a medida."),
    ];

    public static IReadOnlyList<string> DefaultsForPlan(string? planCode) => planCode?.Trim().ToLowerInvariant() switch
    {
        CommercialPlanCatalog.Premium =>
        [UnifiedMail, MailActions, ControlCenterBasic, TrackingBasic, NexiAi, ControlCenterFull, AdvancedAnalytics, SignaturesTemplates],
        CommercialPlanCatalog.Corporate =>
        [UnifiedMail, MailActions, ControlCenterBasic, TrackingBasic, NexiAi, ControlCenterFull, AdvancedAnalytics, SignaturesTemplates, UserManagement, RolesPolicies, OrganizationAnalytics, PrioritySupport],
        CommercialPlanCatalog.WhiteLabel =>
        [UnifiedMail, MailActions, ControlCenterBasic, TrackingBasic, NexiAi, ControlCenterFull, AdvancedAnalytics, SignaturesTemplates, UserManagement, RolesPolicies, OrganizationAnalytics, PrioritySupport, WhiteLabelBranding, CustomDomain, CustomLimits],
        _ => [UnifiedMail, MailActions, ControlCenterBasic, TrackingBasic],
    };

    public static bool IsKnown(string code) => Definitions.Any(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
}

public static class CommercialSubscriptionStatuses
{
    public const string Active = "active";
    public const string Trialing = "trialing";
    public const string Legacy = "legacy";
    public const string Pending = "pending";
    public const string PastDue = "past_due";
    public const string Canceled = "canceled";
    public const string Expired = "expired";

    public static bool GrantsPaidAccess(string? status) => status is Active or Trialing or Legacy;
}
