using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Application.Intelligence;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;
using NexoMail.Infrastructure.Intelligence;

namespace NexoMail.IntelligenceSmokeTests;

internal static class ShadowComparisonComparableUniverseRegression
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var evaluatedAt = DateTimeOffset.UtcNow;
        var userId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
        var gmailAccountId = Guid.Parse("11111111-aaaa-bbbb-cccc-111111111111");
        var imapAccountId = Guid.Parse("22222222-aaaa-bbbb-cccc-222222222222");
        const string gmailAddress = "gmail-user@example.test";

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NexoMailDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var database = new NexoMailDbContext(options);
        await database.Database.EnsureCreatedAsync();

        database.Users.Add(new UserEntity
        {
            Id = userId,
            DisplayName = "Comparable User",
            Email = gmailAddress,
            CreatedAt = evaluatedAt,
            IsActive = true,
            IsEmailVerified = true,
        });
        database.MailAccounts.AddRange(
            new MailAccountEntity
            {
                Id = gmailAccountId,
                UserId = userId,
                Provider = MailProviderType.Gmail,
                EmailAddress = gmailAddress,
                DisplayName = "Gmail",
                Color = "#123456",
                IsActive = true,
                CreatedAt = evaluatedAt,
            },
            new MailAccountEntity
            {
                Id = imapAccountId,
                UserId = userId,
                Provider = MailProviderType.Imap,
                EmailAddress = "imap-user@example.test",
                DisplayName = "IMAP",
                Color = "#654321",
                IsActive = true,
                CreatedAt = evaluatedAt,
            });
        database.MailIndexStates.Add(new MailIndexStateEntity
        {
            AccountId = gmailAccountId,
            UserId = userId,
            LastIndexedAt = evaluatedAt.AddHours(-1),
            WindowDays = 90,
            IndexedMessageCount = 1,
        });
        database.MailMessageIndex.Add(new MailMessageIndexEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AccountId = gmailAccountId,
            ProviderMessageId = "gmail-received-1",
            ThreadId = "gmail-thread-1",
            Direction = "received",
            FromName = "External Sender",
            FromAddress = "sender@external.test",
            ToAddresses = $"Comparable User\t{gmailAddress}",
            Subject = "Please review",
            OccurredAt = evaluatedAt.AddHours(-2),
            IndexedAt = evaluatedAt.AddHours(-1),
            GmailLabels = "INBOX,UNREAD",
            IsInbox = true,
            IsUnread = true,
        });
        await database.SaveChangesAsync();

        var userContext = new ComparableUserContext(userId, gmailAddress);
        var legacy = new GmailControlCenterService(database, userContext);

        var visibleSnapshot = await legacy.GetSnapshotAsync(null, CancellationToken.None);
        Ensure(visibleSnapshot.PendingItems.Count == 0 && visibleSnapshot.UnavailableAccounts == 1,
            "El Control Center visible debe conservar su política actual y excluir una cuenta stale.");

        var diagnosticSnapshot = await legacy.GetDiagnosticSnapshotAsync(null, CancellationToken.None);
        Ensure(diagnosticSnapshot.PendingItems.Count == 1,
            "El snapshot diagnóstico debe evaluar el índice local stale sin ocultar su conversación.");
        Ensure(diagnosticSnapshot.Accounts.Count == 1 && !diagnosticSnapshot.Accounts.Single().IsAvailable,
            "El snapshot diagnóstico debe conservar la señal de disponibilidad aunque use datos cacheados.");

        var intelligenceResult = PendingUserResult();
        var reader = new FixedIntelligenceReader(
        [
            new CommunicationIntelligenceSnapshot(
                gmailAccountId,
                $"{gmailAccountId:N}:gmail-thread-1",
                "gmail-received-1",
                evaluatedAt.AddHours(-2),
                intelligenceResult),
            new CommunicationIntelligenceSnapshot(
                imapAccountId,
                $"{imapAccountId:N}:imap-thread-1",
                "imap-received-1",
                evaluatedAt.AddHours(-1),
                intelligenceResult),
        ]);

        var service = new IntelligenceShadowComparisonService(
            legacy,
            reader,
            new IntelligenceShadowComparator());
        var comparison = await service.CompareAsync(null, evaluatedAt, CancellationToken.None);

        Ensure(comparison.LegacyPendingCount == 1,
            "La comparación debe usar el snapshot diagnóstico legacy sobre caché local.");
        Ensure(comparison.IntelligencePendingCount == 1,
            "La comparación debe limitar Intelligence al mismo universo Gmail que Legacy.");
        Ensure(comparison.AgreementPendingCount == 1
               && comparison.LegacyOnlyCount == 0
               && comparison.IntelligenceOnlyCount == 0,
            "La misma conversación Gmail debe quedar como acuerdo y la cuenta IMAP no debe contaminar la comparación.");
    }

    private static CommunicationIntelligenceResult PendingUserResult() => new(
        new ActionabilityAssessment(
            true,
            CommunicationActionType.Reply,
            0.95,
            null,
            [IntelligenceReasonCodes.DirectRecipient]),
        new ConversationStateAssessment(
            ConversationWorkState.PendingUser,
            [IntelligenceReasonCodes.PendingUser]),
        new PriorityAssessment(
            50,
            PriorityBand.High,
            [IntelligenceReasonCodes.DirectRecipient],
            new Dictionary<string, int>()),
        "nexo-intelligence/0.1-deterministic");

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ComparableUserContext(Guid userId, string email) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid UserId { get; } = userId;
        public string Email { get; } = email;
        public string DisplayName => "Comparable User";
    }

    private sealed class FixedIntelligenceReader(IReadOnlyList<CommunicationIntelligenceSnapshot> snapshots)
        : ICommunicationIntelligenceReader
    {
        public Task<IReadOnlyList<CommunicationIntelligenceSnapshot>> AnalyzeAsync(
            DateTimeOffset evaluatedAt,
            CancellationToken cancellationToken = default) => Task.FromResult(snapshots);
    }
}
