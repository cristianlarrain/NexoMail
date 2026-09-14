using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Application.Intelligence;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Intelligence;

namespace NexoMail.IntelligenceSmokeTests;

internal static class ShadowReaderBehaviorRegression
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var otherUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var activeOne = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var activeTwo = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var inactive = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var otherAccount = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var ownOne = "one@example.test";
        var ownTwo = "two@example.test";
        var now = new DateTimeOffset(2026, 9, 14, 5, 0, 0, TimeSpan.Zero);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NexoMailDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var database = new NexoMailDbContext(options);
        await database.Database.EnsureCreatedAsync();

        database.Users.AddRange(
            new UserEntity
            {
                Id = userId,
                DisplayName = "User A",
                Email = ownOne,
                CreatedAt = now,
                IsActive = true,
                IsEmailVerified = true,
            },
            new UserEntity
            {
                Id = otherUserId,
                DisplayName = "User B",
                Email = "other-user@example.test",
                CreatedAt = now,
                IsActive = true,
                IsEmailVerified = true,
            });

        database.MailAccounts.AddRange(
            Account(activeOne, userId, ownOne, true, now),
            Account(activeTwo, userId, ownTwo, true, now),
            Account(inactive, userId, "inactive@example.test", false, now),
            Account(otherAccount, otherUserId, "other-user@example.test", true, now));

        database.MailMessageIndex.AddRange(
            Message(
                userId, activeOne, "direct-received", "thread-direct", "received",
                "sender@external.test", $"User One\t{ownOne}", now.AddMinutes(-30), isUnread: true),
            Message(
                userId, activeOne, "own-only-sent", "thread-own-only", "sent",
                ownOne, $"User Two\t{ownTwo}", now.AddMinutes(-20)),
            Message(
                userId, activeTwo, "external-sent", "thread-external", "sent",
                ownTwo, "External\texternal@external.test", now.AddMinutes(-10)),
            Message(
                userId, inactive, "inactive-message", "thread-inactive", "received",
                "sender@external.test", "Inactive\tinactive@example.test", now.AddMinutes(-5), isUnread: true),
            Message(
                otherUserId, otherAccount, "other-user-message", "thread-other", "received",
                "sender@external.test", "Other\tother-user@example.test", now.AddMinutes(-4), isUnread: true));

        await database.SaveChangesAsync();

        var intelligence = new CommunicationIntelligenceService(
            new DeterministicActionabilityAnalyzer(),
            new DeterministicConversationStateResolver(),
            new DeterministicPriorityScorer());
        var reader = new LocalIndexCommunicationIntelligenceReader(
            database,
            new ShadowReaderUserContext(userId, ownOne),
            new MailMessageIndexConversationAdapter(),
            intelligence);

        var snapshots = await reader.AnalyzeAsync(now);

        Ensure(snapshots.Count == 3,
            "El lector shadow debe analizar solo conversaciones de cuentas activas del usuario actual.");
        Ensure(snapshots.All(x => x.AccountId is var id && (id == activeOne || id == activeTwo)),
            "El lector shadow no debe exponer cuentas inactivas ni cuentas de otros usuarios.");
        Ensure(snapshots.All(x => x.Intelligence.EngineVersion == "nexo-intelligence/0.1-deterministic"),
            "Cada snapshot debe conservar la versión del motor que produjo el análisis.");

        var direct = snapshots.Single(x => x.LatestMessageId == "direct-received");
        Ensure(direct.AccountId == activeOne
               && !direct.Intelligence.Actionability.IsActionable
               && direct.Intelligence.Actionability.RequiresSemanticReview
               && direct.Intelligence.Actionability.ActionType == CommunicationActionType.Unknown
               && direct.Intelligence.State.State == ConversationWorkState.New
               && direct.Intelligence.Priority.Score == 0,
            "Un recibido directo ambiguo debe quedar como candidato semántico, no como PendingUser determinista.");

        var ownOnly = snapshots.Single(x => x.LatestMessageId == "own-only-sent");
        Ensure(!ownOnly.Intelligence.Actionability.IsActionable
               && ownOnly.Intelligence.State.State == ConversationWorkState.New
               && ownOnly.Intelligence.Priority.Score == 0,
            "Un envío únicamente entre cuentas activas propias no debe quedar esperando respuesta externa.");

        var external = snapshots.Single(x => x.LatestMessageId == "external-sent");
        Ensure(external.AccountId == activeTwo
               && external.Intelligence.Actionability.IsActionable
               && external.Intelligence.State.State == ConversationWorkState.WaitingExternal
               && external.Intelligence.Priority.Score > 0,
            "Un envío a destinatario externo debe quedar en WaitingExternal.");

        Ensure(!snapshots.Any(x => x.LatestMessageId is "inactive-message" or "other-user-message"),
            "El aislamiento de usuario/cuenta activa debe aplicarse antes de ejecutar el motor.");
        Ensure(snapshots.SequenceEqual(snapshots
                .OrderByDescending(x => x.Intelligence.Priority.Score)
                .ThenByDescending(x => x.LatestActivityAt)
                .ThenBy(x => x.ConversationId, StringComparer.Ordinal)),
            "Los snapshots shadow deben ordenarse de forma determinística por prioridad y actividad.");
    }

    private static MailAccountEntity Account(
        Guid id,
        Guid userId,
        string email,
        bool isActive,
        DateTimeOffset createdAt) => new()
    {
        Id = id,
        UserId = userId,
        Provider = MailProviderType.Gmail,
        EmailAddress = email,
        DisplayName = email,
        Color = "#123456",
        IsActive = isActive,
        CreatedAt = createdAt,
    };

    private static MailMessageIndexEntity Message(
        Guid userId,
        Guid accountId,
        string messageId,
        string threadId,
        string direction,
        string from,
        string to,
        DateTimeOffset occurredAt,
        bool isUnread = false) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        AccountId = accountId,
        ProviderMessageId = messageId,
        ThreadId = threadId,
        Direction = direction,
        FromAddress = from,
        ToAddresses = to,
        Subject = "Generic subject",
        OccurredAt = occurredAt,
        IndexedAt = occurredAt,
        GmailLabels = direction == "sent" ? "SENT" : "INBOX",
        IsInbox = direction == "received",
        IsUnread = isUnread,
        AutoSubmitted = string.Empty,
        Precedence = string.Empty,
        HasListUnsubscribe = false,
    };

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ShadowReaderUserContext(Guid userId, string email) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid UserId { get; } = userId;
        public string Email { get; } = email;
        public string DisplayName => "Shadow Reader Test";
    }
}
