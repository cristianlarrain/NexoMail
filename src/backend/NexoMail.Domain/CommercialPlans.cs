namespace NexoMail.Domain;

public sealed record CommercialPlanDefinition(
    string Code,
    string Name,
    string Price,
    string Cadence,
    int? MaxAccounts,
    string Description,
    IReadOnlyList<string> Features,
    bool IsFeatured = false,
    bool IsCorporate = false,
    bool IsWhiteLabel = false);

public static class CommercialPlanCatalog
{
    public const string Freemium = "freemium";
    public const string Premium = "premium";
    public const string Corporate = "corporate";
    public const string WhiteLabel = "white_label";

    private static readonly CommercialPlanDefinition[] Plans =
    [
        new(
            Freemium,
            "Freemium",
            "$0",
            "para siempre",
            2,
            "Para comenzar a centralizar sus cuentas personales sin costo.",
            ["Hasta 2 cuentas de correo", "Bandeja unificada", "Enviar, responder y organizar", "Centro de Control básico", "Seguimiento esencial"]),
        new(
            Premium,
            "Premium",
            "$4.990",
            "CLP / mes",
            10,
            "Para profesionales que necesitan más control, productividad y seguimiento.",
            ["Hasta 10 cuentas de correo", "Centro de Control completo", "Estadísticas y seguimientos avanzados", "Firmas y plantillas", "Funciones de Nexi e IA"],
            IsFeatured: true),
        new(
            Corporate,
            "Corporativo",
            "$7.990",
            "CLP / usuario / mes",
            null,
            "Para equipos y organizaciones que requieren administración centralizada.",
            ["Todo lo de Premium", "Gestión de usuarios", "Roles y políticas", "Estadísticas de organización", "Soporte prioritario"],
            IsCorporate: true),
        new(
            WhiteLabel,
            "White Label",
            "A medida",
            "cotización personalizada",
            null,
            "Para organizaciones que quieran ofrecer NexoMail bajo su propia identidad.",
            ["Marca y logotipo propios", "Dominio personalizado", "Colores e identidad visual", "Configuración y límites a medida", "Implementación por EIDOS Digital"],
            IsCorporate: true,
            IsWhiteLabel: true),
    ];

    public static IReadOnlyList<CommercialPlanDefinition> All => Plans;

    public static CommercialPlanDefinition Resolve(string? code) =>
        Plans.FirstOrDefault(plan => string.Equals(plan.Code, code, StringComparison.OrdinalIgnoreCase)) ?? Plans[0];
}
