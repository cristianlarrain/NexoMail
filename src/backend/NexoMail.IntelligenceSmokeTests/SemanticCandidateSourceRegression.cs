using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Application.Intelligence;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Intelligence;

namespace NexoMail.IntelligenceSmokeTests;

internal static class SemanticCandidateSourceRegression
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var now = new DateTimeOffset(2026, 9, 14, 7, 30, 0, TimeSpan.Zero);
        var userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var otherUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var active = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var inactive = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var otherAccount = Guid.Parse("33333333-3333-3333-3333-333333333333");
        const string own = "owner@example.test";

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite(connection).Options;
        await using var database = new NexoMailDbContext(options);
        await database.Database.EnsureCreatedAsync();

        database.Users.AddRange(
            User(userId, own, now),
            User(otherUserId, "other@example.test", now));
        database.MailAccounts.AddRange(
            Account(active, userId, own, true, now),
            Account(inactive, userId, "inactive@example.test", false, now),
            Account(otherAccount, otherUserId, "other@example.test", true, now));

        database.MailMessageIndex.AddRange(
            Message(userId, active, "semantic-1", "thread-semantic-1", "received",
                "sender@external.test", $"Owner\t{own}", new string('S', 800),
                new string('N', 1300), now.AddHours(-1), true),
            Message(userId, active, "sent-external", "thread-sent", "sent",
                own, "External\texternal@external.test", "Sent subject", "Sent snippet",
                now.AddMinutes(-50), false),
            Message(userId, active, "multi-old", "thread-multi", "sent",
                own, "External\texternal@external.test", "Earlier", new string('E', 900),
                now.AddMinutes(-40), false),
            Message(userId, active, "multi-latest", "thread-multi", "received",
                "person@external.test", $"Owner\t{own}", "Need review", new string('M', 1200),
                now.AddMinutes(-30), false),
            Message(userId, inactive, "inactive", "thread-inactive", "received",
                "sender@external.test", "Inactive\tinactive@example.test", "Inactive", "Inactive snippet",
                now.AddMinutes(-20), true),
            Message(otherUserId, otherAccount, "other", "thread-other", "received",
                "sender@external.test", "Other\tother@example.test", "Other", "Other snippet",
                now.AddMinutes(-10), true));
        await database.SaveChangesAsync();

        var userContext = new TestUserContext(userId, own);
        var intelligence = new CommunicationIntelligenceService(
            new DeterministicActionabilityAnalyzer(),
            new DeterministicConversationStateResolver(),
            new DeterministicPriorityScorer());
        var reader = new LocalIndexCommunicationIntelligenceReader(
            database, userContext, new MailMessageIndexConversationAdapter(), intelligence);
        var source = new SemanticCommunicationCandidateSource(database, userContext, reader);

        var candidates = await source.ReadAsync(now, null, 50, CancellationToken.None);

        Ensure(candidates.Count == 2,
            "Sólo conversaciones RequiresSemanticReview de cuentas activas del usuario actual deben convertirse en candidatos.");
        Ensure(candidates.All(x => x.AccountId == active),
            "La fuente no debe exponer cuentas inactivas ni de otros usuarios.");
        Ensure(candidates.All(x => x.Candidate.Subject.Length <= 600),
            "El asunto debe quedar acotado.");
        Ensure(candidates.All(x => x.Candidate.Snippet.Length <= 1000),
            "El snippet debe quedar acotado.");
        Ensure(candidates.All(x => x.Candidate.RecentExcerpts.Count <= 6),
            "El hilo reciente debe quedar acotado.");
        Ensure(candidates.SelectMany(x => x.Candidate.RecentExcerpts).All(x => x.Snippet.Length <= 800),
            "Cada extracto debe quedar acotado.");
        Ensure(candidates.All(x => !x.Candidate.Subject.Contains('@') && !x.Candidate.Snippet.Contains('@')),
            "El payload semántico no debe introducir identidades de correo fuera del contenido indexado.");
        Ensure(candidates.All(x => x.Candidate.CorrelationId == $"{x.AccountId:N}:{x.LatestMessageId}"),
            "CorrelationId debe ser determinístico por cuenta y último mensaje.");
        Ensure(candidates.Select(x => x.Candidate.CorrelationId).Distinct(StringComparer.Ordinal).Count() == candidates.Count,
            "Cada candidato debe tener correlación única.");
        Ensure(candidates.All(x => x.Candidate.StructuralReasonCodes.Contains("SEMANTIC_REVIEW_REQUIRED")),
            "Cada candidato debe conservar la razón estructural de abstención.");

        var multi = candidates.Single(x => x.LatestMessageId == "multi-latest");
        Ensure(multi.Candidate.RecentExcerpts.Count == 2,
            "Un hilo multi-mensaje debe conservar extractos recientes indexados.");
        Ensure(multi.Candidate.RecentExcerpts[0].Direction == CommunicationDirection.Sent
               && multi.Candidate.RecentExcerpts[1].Direction == CommunicationDirection.Received,
            "Los extractos deben conservar orden cronológico y dirección.");

        var onlyActive = await source.ReadAsync(now, active, 1, CancellationToken.None);
        Ensure(onlyActive.Count == 1 && onlyActive[0].AccountId == active,
            "El filtro de cuenta y el límite deben respetarse.");
    }

    private static UserEntity User(Guid id, string email, DateTimeOffset createdAt) => new()
    {
        Id = id, DisplayName = email, Email = email, CreatedAt = createdAt,
        IsActive = true, IsEmailVerified = true
    };

    private static MailAccountEntity Account(Guid id, Guid userId, string email, bool active, DateTimeOffset createdAt) => new()
    {
        Id = id, UserId = userId, Provider = MailProviderType.Gmail, EmailAddress = email,
        DisplayName = email, Color = "#123456", IsActive = active, CreatedAt = createdAt
    };

    private static MailMessageIndexEntity Message(
        Guid userId, Guid accountId, string messageId, string threadId, string direction,
        string from, string to, string subject, string snippet, DateTimeOffset occurredAt, bool unread) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, AccountId = accountId,
        ProviderMessageId = messageId, ThreadId = threadId, Direction = direction,
        FromAddress = from, ToAddresses = to, Subject = subject, Snippet = snippet,
        OccurredAt = occurredAt, IndexedAt = occurredAt,
        GmailLabels = direction == "sent" ? "SENT" : "INBOX",
        IsInbox = direction == "received", IsUnread = unread,
        AutoSubmitted = string.Empty, Precedence = string.Empty, HasListUnsubscribe = false
    };

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TestUserContext(Guid userId, string email) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid UserId { get; } = userId;
        public string Email { get; } = email;
        public string DisplayName => "Semantic Candidate Test";
    }
}
