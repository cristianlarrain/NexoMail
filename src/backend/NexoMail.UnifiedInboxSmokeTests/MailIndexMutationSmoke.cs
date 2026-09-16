using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Mail;

internal static class MailIndexMutationSmoke
{
    public static async Task RunAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var foreignAccountId = Guid.NewGuid();
        const string messageId = "mutation-message";

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite(connection).Options;
        await using var database = new NexoMailDbContext(options);
        await database.Database.EnsureCreatedAsync(ct);

        database.Users.AddRange(
            new UserEntity { Id = userId, DisplayName = "Mutation User", Email = "mutation@nexomail.test", CreatedAt = now, IsActive = true, IsEmailVerified = true },
            new UserEntity { Id = otherUserId, DisplayName = "Foreign User", Email = "foreign-mutation@nexomail.test", CreatedAt = now, IsActive = true, IsEmailVerified = true });
        database.MailAccounts.AddRange(
            new MailAccountEntity { Id = accountId, UserId = userId, Provider = MailProviderType.Gmail, EmailAddress = "mutation@nexomail.test", DisplayName = "Mutation Gmail", IsActive = true, CreatedAt = now },
            new MailAccountEntity { Id = foreignAccountId, UserId = otherUserId, Provider = MailProviderType.Gmail, EmailAddress = "foreign-mutation@nexomail.test", DisplayName = "Foreign Gmail", IsActive = true, CreatedAt = now });
        database.MailIndexStates.Add(new MailIndexStateEntity
        {
            AccountId = accountId,
            UserId = userId,
            LastIndexedAt = now,
            LastSyncAttemptAt = now,
            LastSyncErrorCode = "sync_error",
            WindowDays = 90,
            IndexedMessageCount = 1,
            BackfillCompletedAt = now.AddMinutes(-5),
        });
        database.MailMessageIndex.AddRange(
            Indexed(userId, accountId, messageId, now.AddHours(-1), inbox: true, unread: true),
            Indexed(otherUserId, foreignAccountId, "foreign-message", now.AddHours(-1), inbox: true, unread: true));
        await database.SaveChangesAsync(ct);

        var mutation = new MailIndexMutationService(database, new TestUserContext(userId));

        await mutation.MarkReadAsync(accountId, messageId, true, ct);
        database.ChangeTracker.Clear();
        var row = await Row(database, userId, accountId, messageId, ct);
        EnsureMutation(!row.IsUnread, "Marcar leído debe actualizar IsUnread=false.");
        EnsureMutation(row.IndexedAt > now.AddHours(-1), "Marcar leído debe renovar IndexedAt.");

        await mutation.MarkReadAsync(accountId, messageId, false, ct);
        database.ChangeTracker.Clear();
        row = await Row(database, userId, accountId, messageId, ct);
        EnsureMutation(row.IsUnread, "Marcar no leído debe actualizar IsUnread=true.");

        await mutation.MoveAsync(accountId, messageId, "archive", ct);
        database.ChangeTracker.Clear();
        row = await Row(database, userId, accountId, messageId, ct);
        EnsureMutation(!row.IsInbox, "Archivar debe sacar el mensaje de Inbox.");

        await mutation.MoveAsync(accountId, messageId, "spam", ct);
        database.ChangeTracker.Clear();
        row = await Row(database, userId, accountId, messageId, ct);
        EnsureMutation(row.IsSpam && !row.IsInbox && !row.IsTrash, "Mover a Spam debe normalizar Spam/Inbox/Trash.");

        await mutation.MoveAsync(accountId, messageId, "trash", ct);
        database.ChangeTracker.Clear();
        row = await Row(database, userId, accountId, messageId, ct);
        EnsureMutation(row.IsTrash && !row.IsInbox && !row.IsSpam, "Mover a Papelera debe normalizar Trash/Inbox/Spam.");

        await mutation.MoveAsync(accountId, messageId, "inbox", ct);
        database.ChangeTracker.Clear();
        row = await Row(database, userId, accountId, messageId, ct);
        EnsureMutation(row.IsInbox && !row.IsTrash && !row.IsSpam, "Restaurar a Inbox debe limpiar Trash y Spam.");

        var countBeforeSyncIntent = await database.MailMessageIndex.AsNoTracking().CountAsync(x => x.UserId == userId && x.AccountId == accountId, ct);
        await mutation.MarkAccountForImmediateSyncAsync(accountId, ct);
        database.ChangeTracker.Clear();
        var state = await database.MailIndexStates.AsNoTracking().SingleAsync(x => x.AccountId == accountId, ct);
        EnsureMutation(state.LastIndexedAt == DateTimeOffset.UnixEpoch, "La intención de sincronización inmediata debe invalidar LastIndexedAt.");
        EnsureMutation(state.LastSyncErrorCode is null, "La intención de sincronización inmediata debe limpiar el error transitorio de sync.");
        EnsureMutation(await database.MailMessageIndex.AsNoTracking().CountAsync(x => x.UserId == userId && x.AccountId == accountId, ct) == countBeforeSyncIntent,
            "La intención de sync no puede fabricar un mensaje enviado.");

        var trackedState = await database.MailIndexStates.SingleAsync(x => x.AccountId == accountId, ct);
        trackedState.LastIndexedAt = now;
        await database.SaveChangesAsync(ct);
        database.ChangeTracker.Clear();

        await mutation.MarkReadAsync(accountId, "missing-indexed-message", true, ct);
        database.ChangeTracker.Clear();
        state = await database.MailIndexStates.AsNoTracking().SingleAsync(x => x.AccountId == accountId, ct);
        EnsureMutation(state.LastIndexedAt == DateTimeOffset.UnixEpoch,
            "Si la fila local falta tras una mutación del proveedor, debe solicitarse sincronización inmediata.");
        EnsureMutation(!await database.MailMessageIndex.AsNoTracking().AnyAsync(
                x => x.UserId == userId && x.AccountId == accountId && x.ProviderMessageId == "missing-indexed-message", ct),
            "Una fila faltante nunca debe fabricarse sin metadata confirmada del proveedor.");

        await mutation.MarkReadAsync(foreignAccountId, "foreign-message", true, ct);
        database.ChangeTracker.Clear();
        var foreignRow = await Row(database, otherUserId, foreignAccountId, "foreign-message", ct);
        EnsureMutation(foreignRow.IsUnread, "Una mutación nunca puede modificar el índice de otro usuario.");
    }

    private static MailMessageIndexEntity Indexed(Guid userId, Guid accountId, string id, DateTimeOffset indexedAt, bool inbox, bool unread) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        AccountId = accountId,
        ProviderMessageId = id,
        ThreadId = $"thread-{id}",
        Direction = "received",
        FromName = "Sender",
        FromAddress = "sender@nexomail.test",
        ToAddresses = "Owner\tmutation@nexomail.test",
        Subject = $"Subject {id}",
        Snippet = $"Snippet {id}",
        OccurredAt = indexedAt,
        IndexedAt = indexedAt,
        IsInbox = inbox,
        IsUnread = unread,
    };

    private static Task<MailMessageIndexEntity> Row(NexoMailDbContext database, Guid userId, Guid accountId, string messageId, CancellationToken ct) =>
        database.MailMessageIndex.AsNoTracking().SingleAsync(
            x => x.UserId == userId && x.AccountId == accountId && x.ProviderMessageId == messageId,
            ct);

    private static void EnsureMutation(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
