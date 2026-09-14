using NexoMail.Application;
using NexoMail.Application.Intelligence;
using NexoMail.Infrastructure.Intelligence;

var applicationAssembly = typeof(IUserContext).Assembly;
Ensure(applicationAssembly.GetType("NexoMail.Application.Intelligence.IActionabilityAnalyzer") is not null,
    "IActionabilityAnalyzer debe existir como contrato público independiente del proveedor.");
Ensure(applicationAssembly.GetType("NexoMail.Application.Intelligence.IConversationStateResolver") is not null,
    "IConversationStateResolver debe existir como contrato público independiente del proveedor.");
Ensure(applicationAssembly.GetType("NexoMail.Application.Intelligence.IPriorityScorer") is not null,
    "IPriorityScorer debe existir como contrato público independiente del proveedor.");
Ensure(applicationAssembly.GetType("NexoMail.Application.Intelligence.ICommunicationIntelligenceService") is not null,
    "ICommunicationIntelligenceService debe existir como contrato público independiente del proveedor.");

var infrastructureAssembly = typeof(NexoMail.Infrastructure.Data.NexoMailDbContext).Assembly;
Ensure(infrastructureAssembly.GetType("NexoMail.Infrastructure.Intelligence.DeterministicConversationStateResolver") is not null,
    "DeterministicConversationStateResolver debe existir como máquina de estados independiente del proveedor.");

var analyzer = new DeterministicActionabilityAnalyzer();
var now = new DateTimeOffset(2026, 9, 14, 3, 0, 0, TimeSpan.Zero);
var ownAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "user@example.test",
    "user.alt@example.test"
};

var directUnread = analyzer.Analyze(Conversation(
    now,
    ownAddresses,
    Message(
        "direct-1",
        now.AddMinutes(-5),
        CommunicationDirection.Received,
        from: "person@external.test",
        to: ["user@example.test"],
        isRead: false,
        isDirectRecipient: true)));
Ensure(directUnread.IsActionable && directUnread.ActionType == CommunicationActionType.Reply,
    "Un recibido directo y no leído debe ser accionable sin conocer remitente o dominio.");
Ensure(directUnread.ReasonCodes.Contains(IntelligenceReasonCodes.DirectRecipient)
       && directUnread.ReasonCodes.Contains(IntelligenceReasonCodes.Unread),
    "Un recibido directo/no leído debe explicar sus señales generales.");

var bulk = analyzer.Analyze(Conversation(
    now,
    ownAddresses,
    Message(
        "bulk-1",
        now.AddMinutes(-10),
        CommunicationDirection.Received,
        from: "mailer@external.test",
        to: ["user@example.test"],
        isRead: false,
        isDirectRecipient: true,
        isBulk: true,
        hasListUnsubscribe: true,
        precedence: "bulk")));
Ensure(!bulk.IsActionable && bulk.ReasonCodes.Contains(IntelligenceReasonCodes.Bulk),
    "Una comunicación bulk debe descartarse mediante metadatos generales.");

var ownOnly = analyzer.Analyze(Conversation(
    now,
    ownAddresses,
    Message(
        "own-1",
        now.AddMinutes(-3),
        CommunicationDirection.Sent,
        from: "user@example.test",
        to: ["user.alt@example.test"],
        isRead: true)));
Ensure(!ownOnly.IsActionable && ownOnly.ReasonCodes.Contains(IntelligenceReasonCodes.OwnAccountOnly),
    "Un envío exclusivamente a cuentas propias no debe esperar respuesta.");

var external = analyzer.Analyze(Conversation(
    now,
    ownAddresses,
    Message(
        "external-1",
        now.AddMinutes(-2),
        CommunicationDirection.Sent,
        from: "user@example.test",
        to: ["colleague@external.test"],
        isRead: true)));
Ensure(external.IsActionable && external.ActionType == CommunicationActionType.WaitForExternal,
    "Un envío a un tercero sin respuesta debe quedar esperando al externo.");
Ensure(external.ReasonCodes.Contains(IntelligenceReasonCodes.WaitingExternal),
    "La espera de respuesta externa debe ser explicable.");

Console.WriteLine("Nexo Intelligence actionability smoke tests passed.");

static CommunicationConversation Conversation(
    DateTimeOffset evaluatedAt,
    IReadOnlySet<string> ownAddresses,
    params CommunicationMessage[] messages) =>
    new("conversation", messages, ownAddresses, evaluatedAt);

static CommunicationMessage Message(
    string id,
    DateTimeOffset occurredAt,
    CommunicationDirection direction,
    string from,
    IReadOnlyList<string> to,
    bool isRead,
    bool isDirectRecipient = false,
    bool isAutomated = false,
    bool isBulk = false,
    bool hasListUnsubscribe = false,
    string? autoSubmitted = null,
    string? precedence = null) =>
    new(
        id,
        "conversation",
        occurredAt,
        direction,
        "Generic subject",
        from,
        to,
        [],
        isRead,
        isDirectRecipient,
        isAutomated,
        isBulk,
        hasListUnsubscribe,
        autoSubmitted,
        precedence);

static void Ensure(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
