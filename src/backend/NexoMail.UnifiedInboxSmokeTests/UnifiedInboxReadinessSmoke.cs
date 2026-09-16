using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Mail;

internal static class UnifiedInboxReadinessSmoke
{
    public static async Task RunAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var gmailId = Guid.NewGuid();
        var microsoftId = Guid.NewGuid();
        var imapId = Guid.NewGuid();
        var foreignMicrosoftId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var dbOptions = new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite(connection).Options;
        await using var database = new NexoMailDbContext(dbOptions);
        await database.Database.EnsureCreatedAsync(ct);

        database.Users.AddRange(
            new UserEntity { Id = userId, DisplayName = "Readiness User", Email = "readiness@nexomail.test", CreatedAt = now, IsActive = true, IsEmailVerified = true },
            new UserEntity { Id = otherUserId, DisplayName = "Foreign Readiness", Email = "foreign-readiness@nexomail.test", CreatedAt = now, IsActive = true, IsEmailVerified = true });
        database.MailAccounts.AddRange(
            Account(gmailId, userId, MailProviderType.Gmail, "gmail@nexomail.test", now),
            Account(microsoftId, userId, MailProviderType.MicrosoftGraph, "m365@nexomail.test", now),
            Account(imapId, userId, MailProviderType.Imap, "imap@nexomail.test", now, isActive: false),
            Account(foreignMicrosoftId, otherUserId, MailProviderType.MicrosoftGraph, "foreign-m365@nexomail.test", now));
        await database.SaveChangesAsync(ct);

        var options = Options.Create(new UnifiedInboxOptions
        {
            IndexReadEnabled = true,
            RequireCompleteBackfill = true,
        });
        var service = new UnifiedInboxReadinessService(database, new ReadinessUserContext(userId), options);

        var initial = await service.GetAsync(ct);
        EnsureReadiness(!initial.IsReady, "Index-read no puede declararse listo con Gmail incompleto o proveedores sin ingesta normalizada.");
        EnsureReadiness(initial.IncompleteAccountIds.SequenceEqual(new[] { gmailId }), "Gmail sin BackfillCompletedAt debe quedar marcado como incompleto.");
        EnsureReadiness(initial.UnsupportedProviderAccountIds.SequenceEqual(new[] { microsoftId }), "Microsoft Graph activo debe bloquear el cutover Gmail-only.");
        EnsureReadiness(!initial.UnsupportedProviderAccountIds.Contains(foreignMicrosoftId), "Readiness no puede inspeccionar cuentas de otro usuario.");
        EnsureReadiness(!initial.UnsupportedProviderAccountIds.Contains(imapId), "Una cuenta IMAP inactiva no debe bloquear el cutover.");
        var initialGmailState = initial.GmailAccountStates.Single(x => x.AccountId == gmailId);
        EnsureReadiness(initialGmailState.BackfillCompletedAt is null && initialGmailState.IndexedMessageCount == 0,
            "Readiness debe exponer el estado diagnóstico de Gmail sin inventar progreso inexistente.");

        database.MailIndexStates.Add(new MailIndexStateEntity
        {
            AccountId = gmailId,
            UserId = userId,
            LastIndexedAt = now,
            WindowDays = 90,
            IndexedMessageCount = 10,
            BackfillStartedAt = now.AddMinutes(-10),
            BackfillCompletedAt = now.AddMinutes(-1),
        });
        await database.SaveChangesAsync(ct);

        var gmailComplete = await service.GetAsync(ct);
        EnsureReadiness(!gmailComplete.IsReady, "Completar Gmail no puede ocultar un proveedor todavía no soportado por el índice.");
        EnsureReadiness(gmailComplete.IncompleteAccountIds.Count == 0, "Gmail completo ya no debe figurar como incompleto.");
        EnsureReadiness(gmailComplete.UnsupportedProviderAccountIds.SequenceEqual(new[] { microsoftId }), "Microsoft Graph debe seguir bloqueando el cutover.");
        var completedGmailState = gmailComplete.GmailAccountStates.Single(x => x.AccountId == gmailId);
        EnsureReadiness(completedGmailState.BackfillCompletedAt.HasValue && completedGmailState.IndexedMessageCount == 10,
            "Readiness debe exponer progreso y finalización del backfill para diagnóstico.");

        var microsoft = await database.MailAccounts.SingleAsync(x => x.Id == microsoftId, ct);
        microsoft.IsActive = false;
        var imap = await database.MailAccounts.SingleAsync(x => x.Id == imapId, ct);
        imap.IsActive = true;
        await database.SaveChangesAsync(ct);

        var imapBlocking = await service.GetAsync(ct);
        EnsureReadiness(!imapBlocking.IsReady && imapBlocking.UnsupportedProviderAccountIds.SequenceEqual(new[] { imapId }),
            "IMAP activo debe bloquear el cutover hasta tener ingesta normalizada equivalente.");

        imap.IsActive = false;
        await database.SaveChangesAsync(ct);
        var ready = await service.GetAsync(ct);
        EnsureReadiness(ready.IsReady, "Sólo Gmail completo debe permitir el cutover al índice.");
        EnsureReadiness(ready.IncompleteAccountIds.Count == 0 && ready.UnsupportedProviderAccountIds.Count == 0,
            "Readiness lista no puede conservar bloqueadores.");

        var incompleteGmailId = Guid.NewGuid();
        database.MailAccounts.Add(Account(incompleteGmailId, userId, MailProviderType.Gmail, "gmail2@nexomail.test", now));
        await database.SaveChangesAsync(ct);
        var relaxed = new UnifiedInboxReadinessService(
            database,
            new ReadinessUserContext(userId),
            Options.Create(new UnifiedInboxOptions { IndexReadEnabled = true, RequireCompleteBackfill = false }));
        var relaxedResult = await relaxed.GetAsync(ct);
        EnsureReadiness(relaxedResult.IsReady, "RequireCompleteBackfill=false debe permitir Gmail aún sin backfill completo cuando no hay proveedores no soportados.");
    }

    private static MailAccountEntity Account(Guid id, Guid userId, MailProviderType provider, string email, DateTimeOffset now, bool isActive = true) => new()
    {
        Id = id,
        UserId = userId,
        Provider = provider,
        EmailAddress = email,
        DisplayName = email,
        IsActive = isActive,
        CreatedAt = now,
    };

    private static void EnsureReadiness(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ReadinessUserContext(Guid userId) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid UserId { get; } = userId;
        public string Email => "readiness@nexomail.test";
        public string DisplayName => "Readiness User";
    }
}
