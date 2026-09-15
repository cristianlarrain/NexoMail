using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Mail;

static void Ensure(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var ct = CancellationToken.None;
var now = DateTimeOffset.UtcNow;
var userId = Guid.NewGuid();
var otherUserId = Guid.NewGuid();
var accountA = Guid.NewGuid();
var accountB = Guid.NewGuid();
var foreignAccount = Guid.NewGuid();
var userContext = new TestUserContext(userId);

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync(ct);
var options = new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite(connection).Options;
await using var db = new NexoMailDbContext(options);
await db.Database.EnsureCreatedAsync(ct);

db.Users.AddRange(
    new UserEntity { Id = userId, DisplayName = "Cristian", Email = "owner@nexomail.test", CreatedAt = now, IsActive = true, IsEmailVerified = true },
    new UserEntity { Id = otherUserId, DisplayName = "Otro", Email = "other@nexomail.test", CreatedAt = now, IsActive = true, IsEmailVerified = true });

db.MailAccounts.AddRange(
    new MailAccountEntity { Id = accountA, UserId = userId, Provider = MailProviderType.Gmail, EmailAddress = "a@nexomail.test", DisplayName = "Cuenta A", IsActive = true, CreatedAt = now },
    new MailAccountEntity { Id = accountB, UserId = userId, Provider = MailProviderType.Gmail, EmailAddress = "b@nexomail.test", DisplayName = "Cuenta B", IsActive = true, CreatedAt = now },
    new MailAccountEntity { Id = foreignAccount, UserId = otherUserId, Provider = MailProviderType.Gmail, EmailAddress = "foreign@nexomail.test", DisplayName = "Ajena", IsActive = true, CreatedAt = now });

db.IgnoredSenders.Add(new IgnoredSenderEntity
{
    Id = Guid.NewGuid(), UserId = userId, AccountId = accountA, SenderAddress = "noise@nexomail.test", CreatedAt = now
});

MailMessageIndexEntity Msg(
    Guid accountId,
    Guid ownerId,
    string id,
    DateTimeOffset at,
    string from,
    string subject,
    string snippet,
    bool inbox = false,
    bool sent = false,
    bool draft = false,
    bool spam = false,
    bool trash = false,
    bool unread = false,
    bool attachment = false) => new()
{
    Id = Guid.NewGuid(), UserId = ownerId, AccountId = accountId, ProviderMessageId = id, ThreadId = $"thread-{id}",
    Direction = sent ? "sent" : "received", FromName = from.Split('@')[0], FromAddress = from,
    ToAddresses = "Cristian\towner@nexomail.test", Subject = subject, Snippet = snippet, OccurredAt = at,
    HasAttachments = attachment, IndexedAt = now, IsInbox = inbox, IsSent = sent, IsDraft = draft, IsSpam = spam, IsTrash = trash, IsUnread = unread
};

db.MailMessageIndex.AddRange(
    Msg(accountA, userId, "a-inbox-1", now.AddMinutes(-1), "alice@nexomail.test", "Alpha urgente", "primer mensaje", inbox: true, unread: true, attachment: true),
    Msg(accountB, userId, "b-inbox-1", now.AddMinutes(-2), "bob@nexomail.test", "Beta", "segundo mensaje", inbox: true),
    Msg(accountA, userId, "a-inbox-2", now.AddMinutes(-3), "carol@nexomail.test", "Gamma", "tercer mensaje", inbox: true),
    Msg(accountB, userId, "b-inbox-2", now.AddMinutes(-4), "diego@nexomail.test", "Delta", "cuarto mensaje", inbox: true, unread: true),
    Msg(accountA, userId, "ignored-1", now.AddMinutes(-5), "noise@nexomail.test", "Ruido", "ignorado", inbox: true),
    Msg(accountA, userId, "sent-1", now.AddMinutes(-6), "a@nexomail.test", "Enviado", "salida", sent: true),
    Msg(accountA, userId, "draft-1", now.AddMinutes(-7), "a@nexomail.test", "Borrador", "draft", draft: true),
    Msg(accountA, userId, "spam-1", now.AddMinutes(-8), "spam@nexomail.test", "Spam", "spam", spam: true),
    Msg(accountA, userId, "trash-1", now.AddMinutes(-9), "trash@nexomail.test", "Papelera", "trash", trash: true),
    Msg(accountA, userId, "archive-1", now.AddMinutes(-10), "archive@nexomail.test", "Archivado", "archive"),
    Msg(foreignAccount, otherUserId, "foreign-1", now.AddSeconds(-10), "foreign@nexomail.test", "NO VISIBLE", "otro usuario", inbox: true));
await db.SaveChangesAsync(ct);

var service = new UnifiedInboxQueryService(db, userContext);

// Global inbox order + ignored sender exclusion + multitenant isolation.
var firstPage = await service.GetMessagesAsync(new MailQuery(null, "inbox", 2), ct);
var firstPageItems = firstPage.Items.ToArray();
Ensure(firstPageItems.Length == 2, "La primera página debe respetar take=2.");
Ensure(firstPageItems[0].ProviderMessageId == "a-inbox-1" && firstPageItems[1].ProviderMessageId == "b-inbox-1", "La bandeja unificada debe ordenar globalmente por fecha descendente.");
Ensure(firstPageItems.All(x => x.AccountId == accountA || x.AccountId == accountB), "La bandeja no puede exponer cuentas de otro usuario.");
Ensure(firstPage.NextCursor is not null, "La primera página debe entregar cursor cuando quedan mensajes.");

var allInbox = new List<MailSummary>();
string? cursor = null;
do
{
    var page = await service.GetMessagesAsync(new MailQuery(null, "inbox", 2, cursor), ct);
    allInbox.AddRange(page.Items);
    cursor = page.NextCursor;
} while (cursor is not null);
var inboxKeys = allInbox.Select(x => $"{x.AccountId:N}:{x.ProviderMessageId}").ToArray();
Ensure(inboxKeys.Length == 4, "La bandeja debe contener exactamente cuatro mensajes normales; ignorado y usuario ajeno quedan fuera.");
Ensure(inboxKeys.Distinct(StringComparer.Ordinal).Count() == inboxKeys.Length, "La paginación no puede repetir mensajes.");
Ensure(!allInbox.Any(x => x.ProviderMessageId is "ignored-1" or "foreign-1"), "La bandeja ordinaria no puede incluir ignorados ni mensajes ajenos.");

// Account filter.
var accountPage = await service.GetMessagesAsync(new MailQuery(accountA, "inbox", 20), ct);
Ensure(accountPage.Items.All(x => x.AccountId == accountA), "El filtro por cuenta debe aislar exactamente la cuenta seleccionada.");
Ensure(accountPage.Items.Count == 2, "Cuenta A debe tener dos mensajes normales en inbox.");

// Folder semantics.
async Task AssertFolder(string folder, string expectedId)
{
    var page = await service.GetMessagesAsync(new MailQuery(null, folder, 20), ct);
    Ensure(page.Items.Count == 1 && page.Items.Single().ProviderMessageId == expectedId, $"La carpeta {folder} no coincide con el índice normalizado.");
}
await AssertFolder("sent", "sent-1");
await AssertFolder("drafts", "draft-1");
await AssertFolder("spam", "spam-1");
await AssertFolder("trash", "trash-1");
await AssertFolder("archive", "archive-1");
await AssertFolder("ignored", "ignored-1");

// Search + unread.
var search = await service.GetMessagesAsync(new MailQuery(null, "inbox", 20, null, "Alpha"), ct);
Ensure(search.Items.Count == 1 && search.Items.Single().ProviderMessageId == "a-inbox-1", "La búsqueda debe operar sobre el índice unificado.");
var unread = await service.GetMessagesAsync(new MailQuery(null, "inbox", 20, null, "is:unread"), ct);
Ensure(unread.Items.Select(x => x.ProviderMessageId).Order().SequenceEqual(new[] { "a-inbox-1", "b-inbox-2" }), "is:unread debe usar el estado normalizado del índice.");

// Exact reference resolution: no synthetic fallback and no cross-user access.
var resolved = await service.ResolveAsync([
    new MailMessageReference(accountA, "a-inbox-1"),
    new MailMessageReference(accountB, "b-inbox-1"),
    new MailMessageReference(accountA, "missing-id"),
    new MailMessageReference(foreignAccount, "foreign-1")
], ct);
Ensure(resolved.Count == 2, "Resolver referencias debe devolver sólo filas reales del usuario autenticado.");
Ensure(resolved.Any(x => x.ProviderMessageId == "a-inbox-1") && resolved.Any(x => x.ProviderMessageId == "b-inbox-1"), "Resolver referencias perdió mensajes existentes.");

Console.WriteLine("PASS: unified inbox query -> order -> pagination -> folders -> ignored -> search -> unread -> resolve -> tenant isolation");

sealed class TestUserContext(Guid userId) : IUserContext
{
    public bool IsAuthenticated => true;
    public Guid UserId { get; } = userId;
    public string Email => "owner@nexomail.test";
    public string DisplayName => "Cristian";
}
