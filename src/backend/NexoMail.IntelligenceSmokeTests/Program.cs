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

var analyzer = new DeterministicActionabilityAnalyzer();
var resolver = new DeterministicConversationStateResolver();
var scorer = new DeterministicPriorityScorer();
var now = new DateTimeOffset(2026, 9, 14, 3, 0, 0, TimeSpan.Zero);
var ownAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "user@example.test",
    "user.alt@example.test"
};

var directConversation = Conversation(
    now,
    ownAddresses,
    Message(
        "direct-1",
        now.AddMinutes(-5),
        CommunicationDirection.Received,
        from: "person@external.test",
        to: ["user@example.test"],
        isRead: false,
        isDirectRecipient: true));
var directUnread = analyzer.Analyze(directConversation);
Ensure(!directUnread.IsActionable
       && directUnread.ActionType == CommunicationActionType.Unknown
       && directUnread.RequiresSemanticReview,
    "Un recibido directo sin evidencia semántica debe abstenerse en vez de asumir Reply.");
Ensure(directUnread.ReasonCodes.Contains(IntelligenceReasonCodes.DirectRecipient)
       && directUnread.ReasonCodes.Contains(IntelligenceReasonCodes.Unread)
       && directUnread.ReasonCodes.Contains(IntelligenceReasonCodes.SemanticReviewRequired),
    "Un recibido directo ambiguo debe explicar sus señales y la necesidad de revisión semántica.");

var bulkConversation = Conversation(
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
        precedence: "bulk"));
var bulk = analyzer.Analyze(bulkConversation);
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

var externalConversation = Conversation(
    now,
    ownAddresses,
    Message(
        "external-1",
        now.AddMinutes(-2),
        CommunicationDirection.Sent,
        from: "user@example.test",
        to: ["colleague@external.test"],
        isRead: true));
var external = analyzer.Analyze(externalConversation);
Ensure(external.IsActionable && external.ActionType == CommunicationActionType.WaitForExternal,
    "Un envío a un tercero sin respuesta debe quedar esperando al externo.");
Ensure(external.ReasonCodes.Contains(IntelligenceReasonCodes.WaitingExternal),
    "La espera de respuesta externa debe ser explicable.");

var pendingState = resolver.Resolve(directConversation, directUnread);
Ensure(pendingState.State == ConversationWorkState.New
       && pendingState.ReasonCodes.Contains(IntelligenceReasonCodes.SemanticReviewRequired),
    "Un recibido ambiguo debe permanecer fuera de PendingUser hasta una decisión semántica.");

var waitingState = resolver.Resolve(externalConversation, external);
Ensure(waitingState.State == ConversationWorkState.WaitingExternal
       && waitingState.ReasonCodes.Contains(IntelligenceReasonCodes.WaitingExternal),
    "El último enviado externo debe quedar WaitingExternal.");

var repliedConversation = Conversation(
    now,
    ownAddresses,
    Message(
        "sent-before-reply",
        now.AddHours(-2),
        CommunicationDirection.Sent,
        from: "user@example.test",
        to: ["colleague@external.test"],
        isRead: true),
    Message(
        "received-reply",
        now.AddMinutes(-30),
        CommunicationDirection.Received,
        from: "colleague@external.test",
        to: ["user@example.test"],
        isRead: false,
        isDirectRecipient: true));
var repliedActionability = analyzer.Analyze(repliedConversation);
var repliedState = resolver.Resolve(repliedConversation, repliedActionability);
Ensure(repliedState.State == ConversationWorkState.New
       && repliedActionability.RequiresSemanticReview,
    "Una respuesta externa posterior debe pasar a revisión semántica antes de asignar trabajo al usuario.");

var resolvedState = resolver.Resolve(
    directConversation,
    directUnread,
    new ConversationStateEvidence(IsResolved: true));
Ensure(resolvedState.State == ConversationWorkState.Resolved
       && resolvedState.ReasonCodes.Contains(IntelligenceReasonCodes.Resolved),
    "La evidencia explícita de resolución debe cerrar el trabajo.");

var cancelledState = resolver.Resolve(
    directConversation,
    directUnread,
    new ConversationStateEvidence(IsResolved: true, IsCancelled: true));
Ensure(cancelledState.State == ConversationWorkState.Cancelled
       && cancelledState.ReasonCodes.Contains(IntelligenceReasonCodes.Cancelled),
    "Cancelled debe prevalecer sobre Resolved cuando ambas evidencias existen.");

var overdueState = resolver.Resolve(
    externalConversation,
    external,
    new ConversationStateEvidence(Deadline: now.AddMinutes(-1)));
Ensure(overdueState.State == ConversationWorkState.Overdue
       && overdueState.ReasonCodes.Contains(IntelligenceReasonCodes.Overdue)
       && overdueState.ReasonCodes.Contains(IntelligenceReasonCodes.DeadlineSignal),
    "Un trabajo accionable con plazo vencido debe quedar Overdue por evidencia temporal explícita.");

var bulkState = resolver.Resolve(bulkConversation, bulk);
var nonActionablePriority = scorer.Score(bulkConversation, bulk, bulkState);
Ensure(nonActionablePriority.Score == 0 && nonActionablePriority.Band == PriorityBand.Low,
    "Lo no accionable no debe consumir prioridad de trabajo.");

var semanticReviewPriority = scorer.Score(directConversation, directUnread, pendingState);
Ensure(semanticReviewPriority.Score == 0
       && semanticReviewPriority.ReasonCodes.Contains(IntelligenceReasonCodes.SemanticReviewRequired),
    "Un candidato semántico no debe recibir prioridad determinista antes de ser clasificado.");

var waitingPriority = scorer.Score(externalConversation, external, waitingState);
Ensure(waitingPriority.Score > semanticReviewPriority.Score
       && waitingPriority.Signals.ContainsKey(IntelligenceReasonCodes.WaitingExternal),
    "WaitingExternal debe conservar prioridad determinista mientras los recibidos ambiguos se abstienen.");

Console.WriteLine("Nexo Intelligence actionability, state and priority smoke tests passed.");

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
