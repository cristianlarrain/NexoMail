using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;

static void Ensure(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var cancellationToken = CancellationToken.None;
var userId = Guid.NewGuid();
var accountId = Guid.Parse("4f03b9de-31a5-43dd-a446-f0dcae434002");
var ownAddress = "cristian.nexo@gmail.com";
var userContext = new TestUserContext(userId, ownAddress, "Cristian");

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
    CreatedAt = DateTimeOffset.UtcNow,
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
    CreatedAt = DateTimeOffset.UtcNow,
});
database.OAuthCredentials.Add(new OAuthCredentialEntity
{
    Id = Guid.NewGuid(),
    MailAccountId = accountId,
    EncryptedRefreshToken = "test-refresh-token",
    UpdatedAt = DateTimeOffset.UtcNow,
});
await database.SaveChangesAsync(cancellationToken);

var httpFactory = new TestHttpClientFactory();
var protector = new PassthroughTokenProtector();
var gmailOptions = Options.Create(new GmailOptions { ClientId = "test", ClientSecret = "test" });
var controlCenter = new GmailControlCenterService(httpFactory, database, protector, gmailOptions, userContext);

var initialSnapshot = await controlCenter.GetSnapshotAsync(accountId, cancellationToken);
Ensure(initialSnapshot.ReceivedWithoutReply == 1, "La métrica Recibidos sin responder no coincide con el escenario de prueba.");
Ensure(initialSnapshot.SentWithoutResponse == 1, "La métrica Enviados sin respuesta no coincide con el escenario de prueba.");
Ensure(initialSnapshot.Unread == 2, "La métrica de correos sin leer no coincide con Gmail.");
Ensure(initialSnapshot.Overdue == 1, "La métrica de pendientes de más de 48 horas no coincide.");
Ensure(initialSnapshot.PendingItems.Count == 2, "Los avisos transaccionales deberían quedar excluidos de los pendientes.");

var receivedPending = initialSnapshot.PendingItems.Single(item => item.Direction == "received");
var resolved = await controlCenter.UpdateStateAsync(accountId, receivedPending.ConversationId, receivedPending.MessageId, "resolved", null, cancellationToken);
Ensure(resolved, "No fue posible marcar como resuelta una conversación válida.");
var afterResolve = await controlCenter.GetSnapshotAsync(accountId, cancellationToken);
Ensure(afterResolve.ReceivedWithoutReply == 0, "Marcar como resuelto no retiró el pendiente recibido.");
Ensure(afterResolve.SentWithoutResponse == 1, "Resolver un recibido alteró indebidamente los enviados pendientes.");
Ensure(afterResolve.Overdue == 0, "Resolver el único pendiente vencido no actualizó la métrica de 48 horas.");

var sentPending = afterResolve.PendingItems.Single(item => item.Direction == "sent");
var snoozed = await controlCenter.UpdateStateAsync(accountId, sentPending.ConversationId, sentPending.MessageId, "snoozed", 24, cancellationToken);
Ensure(snoozed, "No fue posible posponer una conversación válida.");
var afterSnooze = await controlCenter.GetSnapshotAsync(accountId, cancellationToken);
Ensure(afterSnooze.PendingItems.Count == 0, "Posponer no ocultó temporalmente el pendiente.");
Ensure(afterSnooze.SentWithoutResponse == 0, "Posponer no actualizó la métrica de enviados pendientes.");

var demoProvider = new DemoMailProvider();
IMailGateway demoGateway = new DemoMailGateway(new IMailProvider[] { demoProvider });
var tracking = new ControlCenterTrackingService(database, userContext, demoGateway);
Ensure(!await tracking.IsTrackedAsync(accountId, "demo-2", cancellationToken), "El correo de prueba apareció marcado antes de solicitar seguimiento.");
Ensure(await tracking.TrackAsync(accountId, "demo-2", cancellationToken), "No fue posible activar seguimiento manual.");
Ensure(await tracking.IsTrackedAsync(accountId, "demo-2", cancellationToken), "El estado de seguimiento manual no quedó almacenado.");
var trackedItems = await tracking.GetTrackedItemsAsync(accountId, cancellationToken);
Ensure(trackedItems.Count == 1 && trackedItems.Single().MessageId == "demo-2", "Seguimiento prioritario no devolvió el correo marcado manualmente.");
Ensure(await tracking.UntrackAsync(accountId, "demo-2", cancellationToken), "No fue posible quitar seguimiento manual.");
Ensure(!await tracking.IsTrackedAsync(accountId, "demo-2", cancellationToken), "El seguimiento manual siguió activo después de quitarlo.");

var now = DateTimeOffset.UtcNow;
database.MailMessageIndex.AddRange(
    new MailMessageIndexEntity
    {
        Id = Guid.NewGuid(), UserId = userId, AccountId = accountId, ProviderMessageId = "idx-sent-1", ThreadId = "idx-thread-1",
        Direction = "sent", FromName = "Cristian", FromAddress = ownAddress, ToAddresses = "Ana\tana@nexomail.test",
        Subject = "Informe mensual", Snippet = "Adjunto informe", OccurredAt = now.AddHours(-2), HasAttachments = false, IndexedAt = now,
    },
    new MailMessageIndexEntity
    {
        Id = Guid.NewGuid(), UserId = userId, AccountId = accountId, ProviderMessageId = "idx-received-1", ThreadId = "idx-thread-1",
        Direction = "received", FromName = "Ana", FromAddress = "ana@nexomail.test", ToAddresses = $"Cristian\t{ownAddress}",
        Subject = "Re: Informe mensual", Snippet = "Gracias, recibido", OccurredAt = now.AddHours(-1), HasAttachments = true, IndexedAt = now,
    });
database.MailAttachmentIndex.Add(new MailAttachmentIndexEntity
{
    Id = Guid.NewGuid(), UserId = userId, AccountId = accountId, ProviderMessageId = "idx-received-1", AttachmentId = "idx-att-1",
    FileName = "informe.pdf", ContentType = "application/pdf", Size = 128_000, IndexedAt = now,
});
database.MailIndexStates.Add(new MailIndexStateEntity
{
    AccountId = accountId, UserId = userId, LastIndexedAt = now, WindowDays = 90, IndexedMessageCount = 2,
});
await database.SaveChangesAsync(cancellationToken);

var metadata = new GmailMetadataIndexService(httpFactory, database, protector, gmailOptions, userContext);
var contacts = await metadata.GetContactsAsync(30, cancellationToken);
var ana = contacts.Contacts.Single(item => item.Email == "ana@nexomail.test");
Ensure(ana.Sent == 1, "Contactos no contó el correo enviado.");
Ensure(ana.Received == 1, "Contactos no contó la respuesta recibida.");
Ensure(ana.Replies == 1, "Contactos no reconoció la respuesta dentro del hilo.");
Ensure(ana.Awaiting == 0, "Contactos dejó una respuesta recibida como pendiente.");
Ensure(ana.AverageResponseMinutes is >= 59 and <= 61, "El tiempo medio de respuesta no coincide con el escenario de una hora.");

var documents = await metadata.GetDocumentsAsync("informe", "PDF", 50, 0, cancellationToken);
Ensure(documents.Total == 1, "Documentos no encontró el PDF indexado.");
var document = documents.Items.Single();
Ensure(document.FileName == "informe.pdf", "El documento recuperado no corresponde al adjunto indexado.");
Ensure(document.MessageId == "idx-received-1", "El documento perdió el vínculo con su correo de origen.");
Ensure(document.SenderAddress == "ana@nexomail.test", "El documento perdió el remitente del correo de origen.");

Console.WriteLine("PASS: métricas -> resolver -> posponer -> seguimiento manual -> contactos -> documentos");

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

sealed class TestHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        if (string.Equals(name, "Gmail", StringComparison.Ordinal))
        {
            return new HttpClient(new GmailHandler()) { BaseAddress = new Uri("https://gmail.googleapis.com/gmail/v1/") };
        }
        return new HttpClient(new TokenHandler());
    }
}

sealed class TokenHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(Json(HttpStatusCode.OK, "{\"access_token\":\"test-access-token\",\"expires_in\":3600}"));

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}

sealed class GmailHandler : HttpMessageHandler
{
    private readonly long _receivedAt = DateTimeOffset.UtcNow.AddDays(-3).ToUnixTimeMilliseconds();
    private readonly long _sentAt = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
    private readonly long _infoAt = DateTimeOffset.UtcNow.AddHours(-4).ToUnixTimeMilliseconds();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        if (path.EndsWith("/users/me/labels/INBOX", StringComparison.Ordinal))
            return Task.FromResult(Json(HttpStatusCode.OK, "{\"messagesUnread\":2}"));
        if (path.EndsWith("/users/me/threads", StringComparison.Ordinal))
            return Task.FromResult(Json(HttpStatusCode.OK, "{\"threads\":[{\"id\":\"thread-received\"},{\"id\":\"thread-sent\"},{\"id\":\"thread-info\"}]}"));
        if (path.EndsWith("/users/me/threads/thread-received", StringComparison.Ordinal))
            return Task.FromResult(Json(HttpStatusCode.OK, ThreadJson("recv-1", _receivedAt, "INBOX,UNREAD", "Ana <ana@nexomail.test>", "Cristian <cristian.nexo@gmail.com>", "Necesito respuesta")));
        if (path.EndsWith("/users/me/threads/thread-sent", StringComparison.Ordinal))
            return Task.FromResult(Json(HttpStatusCode.OK, ThreadJson("sent-1", _sentAt, "SENT", "Cristian <cristian.nexo@gmail.com>", "Pedro <pedro@nexomail.test>", "Seguimiento pendiente")));
        if (path.EndsWith("/users/me/threads/thread-info", StringComparison.Ordinal))
            return Task.FromResult(Json(HttpStatusCode.OK, ThreadJson("info-1", _infoAt, "INBOX,CATEGORY_UPDATES", "notificaciones@banco.test", "Cristian <cristian.nexo@gmail.com>", "Comprobante de pago")));
        return Task.FromResult(Json(HttpStatusCode.NotFound, "{}"));
    }

    private static string ThreadJson(string id, long internalDate, string labels, string from, string to, string subject)
    {
        var labelIds = labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return JsonSerializer.Serialize(new
        {
            messages = new[]
            {
                new
                {
                    id,
                    labelIds,
                    internalDate = internalDate.ToString(),
                    payload = new
                    {
                        headers = new[]
                        {
                            new { name = "From", value = from },
                            new { name = "To", value = to },
                            new { name = "Subject", value = subject },
                        }
                    }
                }
            }
        });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
