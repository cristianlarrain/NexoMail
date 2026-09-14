using System.Runtime.CompilerServices;
using NexoMail.Application.Intelligence;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Intelligence;

namespace NexoMail.IntelligenceSmokeTests;

internal static class IndexAdapterBehaviorRegression
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var accountA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var accountB = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var now = new DateTimeOffset(2026, 9, 14, 4, 30, 0, TimeSpan.Zero);

        var rows = new MailMessageIndexEntity[]
        {
            new()
            {
                Id = Guid.NewGuid(), UserId = userId, AccountId = accountA,
                ProviderMessageId = "a-received", ThreadId = "shared-thread", Direction = "received",
                FromAddress = "SENDER@EXTERNAL.TEST",
                ToAddresses = "User One\tUSER.ONE@EXAMPLE.TEST\nOther\tother@external.test",
                Subject = "Generic subject", OccurredAt = now.AddMinutes(-20),
                IsUnread = true, AutoSubmitted = "", Precedence = "", HasListUnsubscribe = false
            },
            new()
            {
                Id = Guid.NewGuid(), UserId = userId, AccountId = accountA,
                ProviderMessageId = "a-sent", ThreadId = "shared-thread", Direction = "sent",
                FromAddress = "user.one@example.test",
                ToAddresses = "External\tRECEIVER@EXTERNAL.TEST",
                Subject = "Generic reply", OccurredAt = now.AddMinutes(-10),
                IsUnread = false, AutoSubmitted = "", Precedence = "", HasListUnsubscribe = false
            },
            new()
            {
                Id = Guid.NewGuid(), UserId = userId, AccountId = accountB,
                ProviderMessageId = "b-bulk", ThreadId = "shared-thread", Direction = "received",
                FromAddress = "bulk@external.test",
                ToAddresses = "User Two\tuser.two@example.test",
                Subject = "Generic bulk message", OccurredAt = now.AddMinutes(-5),
                IsUnread = false, AutoSubmitted = "", Precedence = "bulk", HasListUnsubscribe = false
            },
            new()
            {
                Id = Guid.NewGuid(), UserId = userId, AccountId = accountA,
                ProviderMessageId = "a-auto", ThreadId = "auto-thread", Direction = "received",
                FromAddress = "automation@external.test",
                ToAddresses = "User One\tuser.one@example.test",
                Subject = "Generic automated message", OccurredAt = now.AddMinutes(-3),
                IsUnread = true, AutoSubmitted = "auto-generated", Precedence = "", HasListUnsubscribe = true
            }
        };

        var adapter = new MailMessageIndexConversationAdapter();
        var conversations = adapter.Adapt(
            rows,
            ["USER.ONE@EXAMPLE.TEST", "user.two@example.test"],
            now);

        Ensure(conversations.Count == 3,
            "El adaptador debe separar conversaciones por AccountId + ThreadId.");

        var accountAThread = conversations.Single(x =>
            x.Messages.Any(m => m.MessageId == "a-received"));
        Ensure(accountAThread.Messages.Count == 2,
            "Los mensajes del mismo hilo y cuenta deben permanecer juntos.");
        Ensure(accountAThread.Messages[0].MessageId == "a-received"
               && accountAThread.Messages[1].MessageId == "a-sent",
            "Los mensajes de una conversación deben quedar ordenados cronológicamente.");

        var received = accountAThread.Messages[0];
        Ensure(received.Direction == CommunicationDirection.Received,
            "Direction=received debe mapear a CommunicationDirection.Received.");
        Ensure(!received.IsRead,
            "IsUnread=true debe mapear a IsRead=false.");
        Ensure(received.IsDirectRecipient,
            "Un recibido dirigido a una cuenta propia debe marcarse como destinatario directo.");
        Ensure(received.FromAddress == "sender@external.test",
            "Las direcciones deben normalizarse para comparación estable.");
        Ensure(received.ToAddresses.SequenceEqual(["user.one@example.test", "other@external.test"]),
            "ToAddresses debe deserializar el formato nombre-tab-email y normalizar emails.");
        Ensure(received.CcAddresses.Count == 0,
            "El índice actual no persiste CC; el adaptador no debe inventar destinatarios CC.");

        var sent = accountAThread.Messages[1];
        Ensure(sent.Direction == CommunicationDirection.Sent
               && sent.ToAddresses.SequenceEqual(["receiver@external.test"]),
            "Los enviados y sus destinatarios externos deben conservarse correctamente.");
        Ensure(!sent.IsDirectRecipient,
            "IsDirectRecipient solo representa recepción dirigida directamente al usuario.");

        var bulk = conversations.Single(x => x.Messages.Any(m => m.MessageId == "b-bulk")).Messages.Single();
        Ensure(bulk.IsBulk && string.Equals(bulk.Precedence, "bulk", StringComparison.OrdinalIgnoreCase),
            "Precedence=bulk debe conservarse como señal de mensaje masivo.");
        Ensure(bulk.IsDirectRecipient,
            "La señal de destinatario directo debe conservarse aunque otra capa luego descarte un bulk.");

        var automated = conversations.Single(x => x.Messages.Any(m => m.MessageId == "a-auto")).Messages.Single();
        Ensure(automated.IsAutomated
               && automated.HasListUnsubscribe
               && automated.AutoSubmitted == "auto-generated",
            "Las señales universales de automatización y lista deben preservarse desde el índice.");

        Ensure(conversations.All(x => x.OwnAddresses.SetEquals(
            new HashSet<string>(["user.one@example.test", "user.two@example.test"], StringComparer.OrdinalIgnoreCase))),
            "Cada conversación debe recibir el conjunto normalizado de direcciones propias.");
        Ensure(conversations.All(x => x.EvaluatedAt == now),
            "La evaluación debe usar el instante entregado al adaptador, no DateTimeOffset.UtcNow interno.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
