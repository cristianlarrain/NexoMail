using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;
using NexoMail.Infrastructure.Intelligence;

namespace NexoMail.IntelligenceSmokeTests;

internal static class ShadowStructuralDiagnosticsRegression
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var evaluatedAt = DateTimeOffset.UtcNow;
        var userId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var accountId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        const string ownAddress = "user@example.test";

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
            DisplayName = "Diagnostic User",
            Email = ownAddress,
            CreatedAt = evaluatedAt,
            IsActive = true,
            IsEmailVerified = true,
        });
        database.MailAccounts.Add(new MailAccountEntity
        {
            Id = accountId,
            UserId = userId,
            Provider = MailProviderType.Gmail,
            EmailAddress = ownAddress,
            DisplayName = "Diagnostic Gmail",
            Color = "#123456",
            IsActive = true,
            CreatedAt = evaluatedAt,
        });
        database.MailIndexStates.Add(new MailIndexStateEntity
        {
            AccountId = accountId,
            UserId = userId,
            LastIndexedAt = evaluatedAt,
            WindowDays = 90,
            IndexedMessageCount = 3,
        });
        database.MailMessageIndex.AddRange(
            Received(
                userId,
                accountId,
                "agreement-message",
                "agreement-thread",
                "person@external.test",
                ownAddress,
                "Need your input",
                "INBOX",
                isUnread: false,
                evaluatedAt.AddHours(-1)),
            Received(
                userId,
                accountId,
                "promotion-message",
                "promotion-thread",
                "offers@external.test",
                ownAddress,
                "General information",
                "INBOX,UNREAD,CATEGORY_PROMOTIONS",
                isUnread: true,
                evaluatedAt.AddHours(-2)),
            Received(
                userId,
                accountId,
                "noreply-message",
                "noreply-thread",
                "no-reply@external.test",
                ownAddress,
                "Account information",
                "INBOX",
                isUnread: false,
                evaluatedAt.AddHours(-3)));
        await database.SaveChangesAsync();

        var userContext = new DiagnosticUserContext(userId, ownAddress);
        var legacy = new GmailControlCenterService(database, userContext);
        var intelligence = new CommunicationIntelligenceService(
            new DeterministicActionabilityAnalyzer(),
            new DeterministicConversationStateResolver(),
            new DeterministicPriorityScorer());
        var reader = new LocalIndexCommunicationIntelligenceReader(
            database,
            userContext,
            new MailMessageIndexConversationAdapter(),
            intelligence);
        var service = new IntelligenceShadowComparisonService(
            legacy,
            reader,
            new IntelligenceShadowComparator());

        var comparison = await service.CompareAsync(null, evaluatedAt, CancellationToken.None);

        Ensure(comparison.LegacyPendingCount == 1,
            "Legacy debe conservar solo el recibido humano genérico en este escenario ficticio.");
        Ensure(comparison.IntelligencePendingCount == 0,
            "El núcleo determinista no debe declarar pendientes los recibidos directos ambiguos.");
        Ensure(comparison.AgreementPendingCount == 0
               && comparison.LegacyOnlyCount == 1
               && comparison.IntelligenceOnlyCount == 0,
            "Los recibidos ambiguos deben salir de la comparación de pendientes hasta tener decisión semántica.");
        Ensure(comparison.SemanticReviewCandidateCount == 3,
            "Los tres recibidos directos del escenario deben quedar explícitamente como candidatos semánticos.");

        var pendingDiagnostics = comparison.IntelligenceOnlyReceivedDiagnostics;
        Ensure(pendingDiagnostics is not null && pendingDiagnostics.Count == 0,
            "No deben quedar falsos positivos recibidos en IntelligenceOnly tras la abstención determinista.");

        var property = comparison.GetType().GetProperty(
            "SemanticReviewCandidateDiagnostics",
            BindingFlags.Instance | BindingFlags.Public);
        Ensure(property is not null,
            "El resultado del comparador debe exponer diagnósticos agregados de candidatos semánticos.");

        var diagnostics = property!.GetValue(comparison);
        Ensure(diagnostics is not null,
            "El diagnóstico de candidatos semánticos no debe ser nulo.");
        Ensure(ReadInt(diagnostics!, "Count") == 3,
            "El diagnóstico debe contar los tres recibidos enviados a revisión semántica.");
        Ensure(ReadInt(diagnostics!, "UnreadCount") == 1 && ReadInt(diagnostics!, "ReadCount") == 2,
            "El diagnóstico debe separar leídos y no leídos sin exponer contenido.");
        Ensure(ReadInt(diagnostics!, "PromotionsCategoryCount") == 1,
            "El diagnóstico debe conservar la categoría promocional como señal estructural.");
        Ensure(ReadInt(diagnostics!, "ReplyDiscouragedSenderCount") == 1,
            "El diagnóstico debe conservar remitentes técnicos no-reply como señal estructural.");
        Ensure(ReadInt(diagnostics!, "SingleMessageThreadCount") == 3,
            "El diagnóstico debe distinguir hilos de un solo mensaje.");
    }

    private static MailMessageIndexEntity Received(
        Guid userId,
        Guid accountId,
        string messageId,
        string threadId,
        string fromAddress,
        string ownAddress,
        string subject,
        string labels,
        bool isUnread,
        DateTimeOffset occurredAt) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AccountId = accountId,
            ProviderMessageId = messageId,
            ThreadId = threadId,
            Direction = "received",
            FromName = "External Sender",
            FromAddress = fromAddress,
            ToAddresses = $"Diagnostic User\t{ownAddress}",
            Subject = subject,
            Snippet = "Fictitious diagnostic snippet",
            OccurredAt = occurredAt,
            IndexedAt = occurredAt,
            GmailLabels = labels,
            IsInbox = true,
            IsUnread = isUnread,
        };

    private static int ReadInt(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Ensure(property is not null, $"Falta la métrica diagnóstica {propertyName}.");
        var value = property!.GetValue(instance);
        Ensure(value is int, $"La métrica diagnóstica {propertyName} debe ser entera.");
        return (int)value!;
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class DiagnosticUserContext(Guid userId, string email) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid UserId { get; } = userId;
        public string Email { get; } = email;
        public string DisplayName => "Diagnostic User";
    }
}
