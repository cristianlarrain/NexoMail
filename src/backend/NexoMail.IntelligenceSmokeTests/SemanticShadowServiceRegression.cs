using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Application.Intelligence;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Intelligence;

namespace NexoMail.IntelligenceSmokeTests;

internal static class SemanticShadowServiceRegression
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var now = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);
        var userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var accountOne = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var accountTwo = Guid.Parse("22222222-2222-2222-2222-222222222222");
        const string own = "owner@example.test";

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite(connection).Options;
        await using var database = new NexoMailDbContext(dbOptions);
        await database.Database.EnsureCreatedAsync();

        database.Users.Add(new UserEntity
        {
            Id = userId, DisplayName = "Owner", Email = own, CreatedAt = now,
            IsActive = true, IsEmailVerified = true
        });
        database.MailAccounts.AddRange(
            Account(accountOne, userId, "one@example.test", now),
            Account(accountTwo, userId, "two@example.test", now));
        database.MailMessageIndex.AddRange(
            Message(userId, accountOne, "m1", "t1", now.AddMinutes(-1)),
            Message(userId, accountOne, "m2", "t2", now.AddMinutes(-2)),
            Message(userId, accountTwo, "m3", "t3", now.AddMinutes(-3)),
            Message(userId, accountTwo, "m4", "t4", now.AddMinutes(-4)),
            Message(userId, accountOne, "deterministic", "t5", now.AddMinutes(-5)));
        await database.SaveChangesAsync();

        var reader = new RecordingIntelligenceReader([
            SemanticSnapshot(accountOne, "t1", "m1", now.AddMinutes(-1)),
            SemanticSnapshot(accountOne, "t2", "m2", now.AddMinutes(-2)),
            SemanticSnapshot(accountTwo, "t3", "m3", now.AddMinutes(-3)),
            SemanticSnapshot(accountTwo, "t4", "m4", now.AddMinutes(-4)),
            DeterministicSnapshot(accountOne, "t5", "deterministic", now.AddMinutes(-5))
        ]);
        var source = new SemanticCommunicationCandidateSource(
            database,
            new TestUserContext(userId, own),
            reader);
        var analyzer = new RecordingSemanticAnalyzer();

        var disabled = new SemanticIntelligenceShadowService(
            source,
            analyzer,
            Options.Create(new SemanticIntelligenceOptions { Enabled = false, MaxCandidatesPerRequest = 4 }));
        var disabledResult = await disabled.AnalyzeAsync(null, 500, now, CancellationToken.None);
        Ensure(!disabledResult.Enabled && disabledResult.Items.Count == 0,
            "Con el gate desactivado el shadow debe responder vacío y disabled.");
        Ensure(reader.CallCount == 0 && analyzer.CallCount == 0,
            "Con el gate desactivado no debe leerse el índice ni invocarse el proveedor.");

        var beforeMessageCount = await database.MailMessageIndex.CountAsync();
        var beforeAccountCount = await database.MailAccounts.CountAsync();
        var enabled = new SemanticIntelligenceShadowService(
            source,
            analyzer,
            Options.Create(new SemanticIntelligenceOptions { Enabled = true, MaxCandidatesPerRequest = 4 }));

        var result = await enabled.AnalyzeAsync(null, 500, now, CancellationToken.None);

        Ensure(result.Enabled && result.RequestedLimit == 4,
            "El límite solicitado debe quedar acotado por MaxCandidatesPerRequest.");
        Ensure(analyzer.CallCount == 1 && analyzer.LastCandidates.Count == 4,
            "Sólo los cuatro candidatos semánticos permitidos deben llegar al analizador.");
        Ensure(analyzer.LastCandidates.All(x => x.StructuralReasonCodes.Contains("SEMANTIC_REVIEW_REQUIRED")),
            "Una conversación determinística nunca debe llegar al analizador semántico.");
        Ensure(result.Items.Count == 4 && result.Diagnostics.CandidateCount == 4,
            "El resultado debe conservar exactamente los candidatos analizados.");
        Ensure(result.Diagnostics.ClassifiedActionCount == 1
               && result.Diagnostics.ClassifiedNoActionCount == 1
               && result.Diagnostics.UncertainCount == 2,
            "Los agregados acción/no-acción/incertidumbre deben reflejar las evaluaciones.");
        Ensure(result.Diagnostics.ConfidenceBuckets["0.00-0.49"] == 1
               && result.Diagnostics.ConfidenceBuckets["0.50-0.74"] == 1
               && result.Diagnostics.ConfidenceBuckets["0.75-0.89"] == 1
               && result.Diagnostics.ConfidenceBuckets["0.90-1.00"] == 1,
            "Los cuatro buckets de confianza deben calcularse de forma determinística.");
        Ensure(result.Diagnostics.ProviderFailureCount == 1
               && result.Diagnostics.ParseFailureCount == 1,
            "Los fallos de proveedor y parseo deben quedar agregados por reason code.");
        Ensure(result.Diagnostics.DeadlineDetectedCount == 1,
            "Debe contabilizarse una fecha límite semántica explícita.");
        Ensure(await database.MailMessageIndex.CountAsync() == beforeMessageCount
               && await database.MailAccounts.CountAsync() == beforeAccountCount,
            "El shadow semántico no debe insertar, actualizar ni eliminar datos operacionales.");

        var filtered = await enabled.AnalyzeAsync(accountTwo, 500, now, CancellationToken.None);
        Ensure(filtered.Items.Count == 2 && filtered.Items.All(x => x.AccountId == accountTwo),
            "El filtro de cuenta debe propagarse a la fuente semántica.");
    }

    private static CommunicationIntelligenceSnapshot SemanticSnapshot(
        Guid accountId, string threadId, string messageId, DateTimeOffset at) =>
        new(
            accountId,
            $"{accountId:N}:{threadId}",
            messageId,
            at,
            new CommunicationIntelligenceResult(
                new ActionabilityAssessment(false, CommunicationActionType.Unknown, 0.35, null,
                    ["LATEST_RECEIVED", "DIRECT_RECIPIENT", "SEMANTIC_REVIEW_REQUIRED"], true),
                new ConversationStateAssessment(ConversationWorkState.New, ["SEMANTIC_REVIEW_REQUIRED"]),
                new PriorityAssessment(0, PriorityBand.Low, ["SEMANTIC_REVIEW_REQUIRED"], new Dictionary<string, int>()),
                "nexo-intelligence/0.1-deterministic"),
            new CommunicationSnapshotSignals(false, true, false, false, false, false,
                false, false, false, false, null, null, 1));

    private static CommunicationIntelligenceSnapshot DeterministicSnapshot(
        Guid accountId, string threadId, string messageId, DateTimeOffset at) =>
        new(
            accountId,
            $"{accountId:N}:{threadId}",
            messageId,
            at,
            new CommunicationIntelligenceResult(
                new ActionabilityAssessment(true, CommunicationActionType.WaitForExternal, 0.9, null,
                    ["LATEST_SENT_EXTERNAL"]),
                new ConversationStateAssessment(ConversationWorkState.WaitingExternal, ["WAITING_EXTERNAL"]),
                new PriorityAssessment(20, PriorityBand.Low, ["WAITING_EXTERNAL"], new Dictionary<string, int>()),
                "nexo-intelligence/0.1-deterministic"));

    private static MailAccountEntity Account(Guid id, Guid userId, string email, DateTimeOffset createdAt) => new()
    {
        Id = id, UserId = userId, Provider = MailProviderType.Gmail, EmailAddress = email,
        DisplayName = email, Color = "#123456", IsActive = true, CreatedAt = createdAt
    };

    private static MailMessageIndexEntity Message(
        Guid userId, Guid accountId, string messageId, string threadId, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, AccountId = accountId,
        ProviderMessageId = messageId, ThreadId = threadId, Direction = "received",
        FromAddress = "sender@external.test", ToAddresses = "Owner\towner@example.test",
        Subject = $"Subject {messageId}", Snippet = $"Snippet {messageId}",
        OccurredAt = at, IndexedAt = at, GmailLabels = "INBOX", IsInbox = true,
        IsUnread = false, AutoSubmitted = string.Empty, Precedence = string.Empty
    };

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RecordingIntelligenceReader(IReadOnlyList<CommunicationIntelligenceSnapshot> snapshots)
        : ICommunicationIntelligenceReader
    {
        public int CallCount { get; private set; }
        public Task<IReadOnlyList<CommunicationIntelligenceSnapshot>> AnalyzeAsync(
            DateTimeOffset evaluatedAt,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(snapshots);
        }
    }

    private sealed class RecordingSemanticAnalyzer : ISemanticCommunicationAnalyzer
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<SemanticCommunicationCandidate> LastCandidates { get; private set; } = [];

        public Task<IReadOnlyList<SemanticAnalysisResult>> AnalyzeAsync(
            IReadOnlyList<SemanticCommunicationCandidate> candidates,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastCandidates = candidates;
            var results = candidates.Select((candidate, index) => index switch
            {
                0 => Result(candidate, CommunicationActionType.Reply, true, 0.95, false,
                    ["SEMANTIC_PROVIDER_RESULT"], DateTimeOffset.UtcNow.AddDays(1)),
                1 => Result(candidate, CommunicationActionType.None, false, 0.80, false,
                    ["SEMANTIC_PROVIDER_RESULT"], null),
                2 => Result(candidate, CommunicationActionType.Unknown, false, 0.60, true,
                    ["SEMANTIC_PARSE_FAILURE", "SEMANTIC_UNCERTAIN"], null),
                _ => Result(candidate, CommunicationActionType.Unknown, false, 0.40, true,
                    ["SEMANTIC_PROVIDER_FAILURE", "SEMANTIC_UNCERTAIN"], null)
            }).ToArray();
            return Task.FromResult<IReadOnlyList<SemanticAnalysisResult>>(results);
        }

        private static SemanticAnalysisResult Result(
            SemanticCommunicationCandidate candidate,
            CommunicationActionType action,
            bool requiresAction,
            double confidence,
            bool uncertain,
            IReadOnlyList<string> reasons,
            DateTimeOffset? deadline) =>
            new(candidate.CorrelationId,
                new SemanticActionabilityAssessment(action, requiresAction, confidence, deadline, reasons, uncertain),
                "fake/1");
    }

    private sealed class TestUserContext(Guid userId, string email) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid UserId { get; } = userId;
        public string Email { get; } = email;
        public string DisplayName => "Shadow Semantic Test";
    }
}
