using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.ControlCenterSmokeTests;
using NexoMail.Domain;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;

static void Ensure(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

ControlCenterIndexRegressionTests.RunClassifierRegressionTests();
ControlCenterIndexRegressionTests.RunEntityMetadataContractTests();

var cancellationToken = CancellationToken.None;
var userId = Guid.NewGuid();
var accountId = Guid.Parse("4f03b9de-31a5-43dd-a446-f0dcae434002");
var ownAddress = "cristian.nexo@gmail.com";
var userContext = new TestUserContext(userId, ownAddress, "Cristian");
var now = DateTimeOffset.UtcNow;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync(cancellationToken);
var dbOptions = new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite(connection).Options;
await using var database = new NexoMailDbContext(dbOptions);
await database.Database.EnsureCreatedAsync(cancellationToken);

database.Users.Add(new UserEntity
{
    Id = userId,
    DisplayName = "Cristian",
    Email = ownAddress,
    CreatedAt = now,
    IsActive = true,
    IsEmailVerified = true,
});
database.MailAccounts.Add(new MailAccountEntity
{
    Id = accountId,
    UserId = userId,
    Provider = MailProviderType.Gmail,
    EmailAddress = ownAddress,
    DisplayName = "Gmail",
    Color = "#c6524b",
    IsActive = true,
    CreatedAt = now,
});
database.MailIndexStates.Add(new MailIndexStateEntity
{
    AccountId = accountId,
    UserId = userId,
    LastIndexedAt = now,
    WindowDays = 90,
    IndexedMessageCount = 4,
});
database.MailMessageIndex.AddRange(
    new MailMessageIndexEntity
    {
        Id = Guid.NewGuid(), UserId = userId, AccountId = accountId, ProviderMessageId = "recv-1", ThreadId = "thread-received",
        Direction = "received", FromName = "Laura", FromAddress = "laura@nexomail.test", ToAddresses = $"Cristian\t{ownAddress}",
        Subject = "Necesito respuesta", OccurredAt = now.AddDays(-3), IndexedAt = now, GmailLabels = "INBOX,UNREAD", IsInbox = true, IsUnread = true,
    },
    new MailMessageIndexEntity
    {
        Id = Guid.NewGuid(), UserId = userId, AccountId = accountId, ProviderMessageId = "sent-1", ThreadId = "thread-sent",
        Direction = "sent", FromName = "Cristian", FromAddress = ownAddress, ToAddresses = "Pedro\tpedro@nexomail.test",
        Subject = "Seguimiento pendiente", OccurredAt = now.AddDays(-1), IndexedAt = now, GmailLabels = "SENT",
    },
    new MailMessageIndexEntity
    {
        Id = Guid.NewGuid(), UserId = userId, AccountId = accountId, ProviderMessageId = "info-1", ThreadId = "thread-info",
        Direction = "received", FromAddress = "notificaciones@banco.test", ToAddresses = $"Cristian\t{ownAddress}",
        Subject = "Comprobante de pago", OccurredAt = now.AddHours(-4), IndexedAt = now, GmailLabels = "INBOX,UNREAD,CATEGORY_UPDATES", IsInbox = true, IsUnread = true,
    },
    new MailMessageIndexEntity
    {
        Id = Guid.NewGuid(), UserId = userId, AccountId = accountId, ProviderMessageId = "old-unread-1", ThreadId = "thread-old-unread",
        Direction = "received", FromAddress = "noreply@archivo.test", ToAddresses = $"Cristian\t{ownAddress}",
        Subject = "Aviso antiguo", OccurredAt = now.AddDays(-40), IndexedAt = now, GmailLabels = "INBOX,UNREAD", IsInbox = true, IsUnread = true,
    });
await database.SaveChangesAsync(cancellationToken);

var controlCenter = new GmailControlCenterService(database, userContext);
var initialSnapshot = await controlCenter.GetSnapshotAsync(accountId, cancellationToken);
Ensure(initialSnapshot.ReceivedWithoutReply == 1, "La métrica Recibidos sin responder no coincide con el índice.");
Ensure(initialSnapshot.SentWithoutResponse == 1, "La métrica Enviados sin respuesta no coincide con el índice.");
Ensure(initialSnapshot.Unread == 3, "La métrica de correos sin leer debe incluir no leídos indexados fuera de la ventana operativa de 14 días.");
Ensure(initialSnapshot.Overdue == 1, "La métrica de pendientes de más de 48 horas no coincide.");
Ensure(initialSnapshot.PendingItems.Count == 2, "Los avisos transaccionales y los no leídos antiguos no deben entrar como pendientes operacionales.");
Ensure(initialSnapshot.UnavailableAccounts == 0, "Una cuenta con índice recién actualizado no debe figurar como no disponible.");

var receivedPending = initialSnapshot.PendingItems.Single(item => item.Direction == "received");
Ensure(await controlCenter.UpdateStateAsync(accountId, receivedPending.ConversationId, receivedPending.MessageId, "resolved", null, cancellationToken), "No fue posible marcar como resuelta una conversación válida.");
var afterResolve = await controlCenter.GetSnapshotAsync(accountId, cancellationToken);
Ensure(afterResolve.ReceivedWithoutReply == 0, "Marcar como resuelto no retiró el pendiente recibido.");
Ensure(afterResolve.SentWithoutResponse == 1, "Resolver un recibido alteró indebidamente los enviados pendientes.");
Ensure(afterResolve.Overdue == 0, "Resolver el único pendiente vencido no actualizó la métrica de 48 horas.");

var sentPending = afterResolve.PendingItems.Single(item => item.Direction == "sent");
Ensure(await controlCenter.UpdateStateAsync(accountId, sentPending.ConversationId, sentPending.MessageId, "snoozed", 24, cancellationToken), "No fue posible posponer una conversación válida.");
var afterSnooze = await controlCenter.GetSnapshotAsync(accountId, cancellationToken);
Ensure(afterSnooze.PendingItems.Count == 0, "Posponer no ocultó temporalmente el pendiente.");
Ensure(afterSnooze.SentWithoutResponse == 0, "Posponer no actualizó la métrica de enviados pendientes.");

var activityService = new GmailControlCenterActivityService(database, userContext);
var activity = await activityService.GetActivityAsync(accountId, 7, 0, cancellationToken);
Ensure(activity.Accounts.Count == 1 && activity.Accounts.Single().IsAvailable, "Actividad debe usar el estado del índice para disponibilidad.");
Ensure(activity.Activity.Sum(x => x.Received) == 2, "Actividad debe contar los dos mensajes INBOX indexados dentro de la ventana de 7 días.");
Ensure(activity.Activity.Sum(x => x.Sent) == 1, "Actividad debe contar el mensaje enviado indexado.");

var demoProvider = new DemoMailProvider();
IMailGateway demoGateway = new DemoMailGateway(new IMailProvider[] { demoProvider });
var tracking = new ControlCenterTrackingService(database, userContext, demoGateway);
Ensure(!await tracking.IsTrackedAsync(accountId, "demo-2", cancellationToken), "El correo de prueba apareció marcado antes de solicitar seguimiento.");
Ensure(await tracking.TrackAsync(accountId, "demo-2", cancellationToken), "No fue posible activar seguimiento manual.");
Ensure(await tracking.IsTrackedAsync(accountId, "demo-2", cancellationToken), "El estado de seguimiento manual no quedó almacenado.");
Ensure((await tracking.GetTrackedItemsAsync(accountId, cancellationToken)).Single().MessageId == "demo-2", "Seguimiento prioritario no devolvió el correo marcado manualmente.");
Ensure(await tracking.UntrackAsync(accountId, "demo-2", cancellationToken), "No fue posible quitar seguimiento manual.");

var contactTime = DateTimeOffset.UtcNow;
database.MailMessageIndex.AddRange(
    new MailMessageIndexEntity
    {
        Id = Guid.NewGuid(), UserId = userId, AccountId = accountId, ProviderMessageId = "idx-sent-1", ThreadId = "idx-thread-1",
        Direction = "sent", FromName = "Cristian", FromAddress = ownAddress, ToAddresses = "Ana\tana@nexomail.test",
        Subject = "Informe mensual", Snippet = "Adjunto informe", OccurredAt = contactTime.AddHours(-2), HasAttachments = false, IndexedAt = contactTime, GmailLabels = "SENT",
    },
    new MailMessageIndexEntity
    {
        Id = Guid.NewGuid(), UserId = userId, AccountId = accountId, ProviderMessageId = "idx-received-1", ThreadId = "idx-thread-1",
        Direction = "received", FromName = "Ana", FromAddress = "ana@nexomail.test", ToAddresses = $"Cristian\t{ownAddress}",
        Subject = "Re: Informe mensual", Snippet = "Gracias, recibido", OccurredAt = contactTime.AddHours(-1), HasAttachments = true, IndexedAt = contactTime, GmailLabels = "INBOX", IsInbox = true,
    });
database.MailAttachmentIndex.Add(new MailAttachmentIndexEntity
{
    Id = Guid.NewGuid(), UserId = userId, AccountId = accountId, ProviderMessageId = "idx-received-1", AttachmentId = "idx-att-1",
    FileName = "informe.pdf", ContentType = "application/pdf", Size = 128_000, IndexedAt = contactTime,
});
await database.SaveChangesAsync(cancellationToken);

var metadata = new GmailMetadataIndexService(new ThrowingHttpClientFactory(), database, new PassthroughTokenProtector(), Options.Create(new GmailOptions { ClientId = "test", ClientSecret = "test" }), userContext);
var contacts = await metadata.GetContactsAsync(30, cancellationToken);
var ana = contacts.Contacts.Single(item => item.Email == "ana@nexomail.test");
Ensure(ana.Sent == 1 && ana.Received == 1 && ana.Replies == 1 && ana.Awaiting == 0, "Contactos perdió la relación enviado/respuesta del índice.");
Ensure(ana.AverageResponseMinutes is >= 59 and <= 61, "El tiempo medio de respuesta no coincide con el escenario de una hora.");

var documents = await metadata.GetDocumentsAsync("informe", "PDF", 50, 0, cancellationToken);
Ensure(documents.Total == 1 && documents.Items.Single().FileName == "informe.pdf", "Documentos no encontró el PDF indexado.");

Console.WriteLine("PASS: índice -> clasificación -> métricas -> actividad -> resolver -> posponer -> seguimiento -> contactos -> documentos");

sealed class TestUserContext(Guid userId, string email, string displayName) : IUserContext
{
    public bool IsAuthenticated => true;
    public Guid UserId { get; } = userId;
    public string Email { get; } = email;
    public string DisplayName { get; } = displayName;
}

sealed class PassthroughTokenProtector : ITokenProtector
{
    public string Protect(string value) => value;
    public string Unprotect(string protectedValue) => protectedValue;
}

sealed class ThrowingHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => throw new InvalidOperationException($"El camino de lectura no debe solicitar HttpClient: {name}");
}
