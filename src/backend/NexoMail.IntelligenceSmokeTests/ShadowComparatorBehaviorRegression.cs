using System.Runtime.CompilerServices;
using NexoMail.Application.Intelligence;
using NexoMail.Domain;
using NexoMail.Infrastructure.Intelligence;

namespace NexoMail.IntelligenceSmokeTests;

internal static class ShadowComparatorBehaviorRegression
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var now = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        var accountA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var accountB = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var legacy = new ControlCenterSnapshot(
            ReceivedWithoutReply: 4,
            SentWithoutResponse: 2,
            Unread: 0,
            Overdue: 0,
            Activity: Array.Empty<ControlCenterDay>(),
            PriorityItems: Array.Empty<ControlCenterPendingItem>(),
            PendingItems:
            [
                Pending(accountA, "legacy-received", "conv-received", "received", now.AddHours(-1)),
                Pending(accountA, "legacy-sent", "conv-sent", "sent", now.AddHours(-2)),
                Pending(accountA, "legacy-only", "conv-legacy-only", "received", now.AddHours(-3)),
                Pending(accountA, "legacy-mismatch", "conv-mismatch", "received", now.AddHours(-4)),
                Pending(accountB, "other-account", "conv-other", "received", now.AddHours(-1))
            ],
            Accounts: Array.Empty<ControlCenterAccountSummary>(),
            UnavailableAccounts: 0,
            GeneratedAt: now);

        IReadOnlyList<CommunicationIntelligenceSnapshot> intelligence =
        [
            Snapshot(accountA, "intel-received", "conv-received", now.AddHours(-1),
                ConversationWorkState.PendingUser, CommunicationActionType.Reply, 80),
            Snapshot(accountA, "intel-sent", "conv-sent", now.AddHours(-2),
                ConversationWorkState.WaitingExternal, CommunicationActionType.WaitForExternal, 70),
            Snapshot(accountA, "intel-mismatch", "conv-mismatch", now.AddHours(-4),
                ConversationWorkState.WaitingExternal, CommunicationActionType.WaitForExternal, 60),
            Snapshot(accountA, "intel-only", "conv-intelligence-only", now.AddMinutes(-30),
                ConversationWorkState.PendingUser, CommunicationActionType.Reply, 90),
            Snapshot(accountB, "intel-other", "conv-other", now.AddHours(-1),
                ConversationWorkState.PendingUser, CommunicationActionType.Reply, 50)
        ];

        var result = new IntelligenceShadowComparator().Compare(legacy, intelligence, accountA, now);

        Ensure(result.LegacyPendingCount == 4,
            "La comparación debe contar solo los pendientes legacy de la cuenta solicitada.");
        Ensure(result.IntelligencePendingCount == 4,
            "La comparación debe contar solo los pendientes Intelligence de la cuenta solicitada.");
        Ensure(result.AgreementPendingCount == 3,
            "Tres conversaciones deben coincidir en que existe trabajo pendiente.");
        Ensure(result.LegacyOnlyCount == 1,
            "Debe detectar una conversación pendiente solo para el motor legacy.");
        Ensure(result.IntelligenceOnlyCount == 1,
            "Debe detectar una conversación pendiente solo para Nexo Intelligence.");
        Ensure(result.DirectionMismatchCount == 1,
            "Debe detectar cuando ambos motores ven pendiente pero discrepan sobre quién debe actuar.");
        Ensure(result.Items.Count == 5,
            "El detalle debe contener la unión de conversaciones pendientes de ambos motores.");
        Ensure(result.Items.Count(item => item.Category == IntelligenceShadowComparisonCategory.Agreement) == 2,
            "Dos conversaciones deben coincidir también en la dirección de la acción.");
        Ensure(result.Items.Count(item => item.Category == IntelligenceShadowComparisonCategory.DirectionMismatch) == 1,
            "La discrepancia de dirección debe quedar explícitamente categorizada.");
        Ensure(result.Items.Count(item => item.Category == IntelligenceShadowComparisonCategory.LegacyOnly) == 1,
            "Legacy-only debe quedar explícitamente categorizado.");
        Ensure(result.Items.Count(item => item.Category == IntelligenceShadowComparisonCategory.IntelligenceOnly) == 1,
            "Intelligence-only debe quedar explícitamente categorizado.");
        Ensure(result.Items.All(item => item.AccountId == accountA),
            "El comparador debe respetar el filtro de cuenta.");
        Ensure(result.EngineVersion == "nexo-intelligence/test",
            "El diagnóstico debe conservar la versión del motor Intelligence evaluado.");
    }

    private static ControlCenterPendingItem Pending(
        Guid accountId,
        string messageId,
        string conversationId,
        string direction,
        DateTimeOffset since) =>
        new(
            AccountId: accountId,
            AccountName: "Fictional Account",
            AccountColor: "#123456",
            MessageId: messageId,
            ConversationId: conversationId,
            Direction: direction,
            Counterpart: "external@example.test",
            Subject: "Generic subject",
            Since: since,
            IsRead: false);

    private static CommunicationIntelligenceSnapshot Snapshot(
        Guid accountId,
        string messageId,
        string conversationId,
        DateTimeOffset latestActivityAt,
        ConversationWorkState state,
        CommunicationActionType actionType,
        int score) =>
        new(
            AccountId: accountId,
            ConversationId: conversationId,
            LatestMessageId: messageId,
            LatestActivityAt: latestActivityAt,
            Intelligence: new CommunicationIntelligenceResult(
                Actionability: new ActionabilityAssessment(
                    IsActionable: true,
                    ActionType: actionType,
                    Confidence: 0.9,
                    Deadline: null,
                    ReasonCodes: ["TEST_ACTIONABLE"]),
                State: new ConversationStateAssessment(
                    State: state,
                    ReasonCodes: ["TEST_STATE"]),
                Priority: new PriorityAssessment(
                    Score: score,
                    Band: PriorityBand.High,
                    ReasonCodes: ["TEST_PRIORITY"],
                    Signals: new Dictionary<string, int> { ["test"] = score }),
                EngineVersion: "nexo-intelligence/test"));

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
