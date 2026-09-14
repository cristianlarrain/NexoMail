using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;

namespace NexoMail.ControlCenterSmokeTests;

internal static class ControlCenterIndexRegressionTests
{
    public static void RunClassifierRegressionTests()
    {
        var personal = Message("persona@example.com", "Necesito tu respuesta");
        Ensure(!ControlCenterMessageClassifier.IsNonActionableReceived(personal), "Un correo personal no debe excluirse.");

        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("persona@example.com", "Oferta", labels: "INBOX,CATEGORY_PROMOTIONS")), "Promociones debe excluirse.");
        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("noreply@example.com", "Aviso")), "noreply debe excluirse.");
        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("persona@example.com", "Aviso", autoSubmitted: "auto-generated")), "Auto-Submitted debe excluirse.");
        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("persona@example.com", "Aviso", precedence: "bulk")), "Precedence bulk debe excluirse.");
        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("persona@example.com", "Aviso", hasListUnsubscribe: true)), "List-Unsubscribe debe excluirse.");
        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("persona@example.com", "Comprobante de pago")), "Asunto transaccional de alta confianza debe excluirse.");
        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("alertas@empresa.cl", "Resumen de actividad", labels: "INBOX,CATEGORY_UPDATES")), "CATEGORY_UPDATES con remitente de notificación debe excluirse.");

        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("notificaciones@email.comunidadfeliz.com", "Nueva encomienda recibida")), "Las notificaciones automáticas de plataforma no deben aparecer como pendientes de respuesta.");
        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("customer.service@ionos.com", "Order Confirmation")), "Las confirmaciones de pedido no deben aparecer como pendientes de respuesta.");
        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("security@ionos.com", "Your IONOS Login Code: 252610")), "Los códigos de acceso no deben aparecer como pendientes de respuesta.");
        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("hello@nexomail.test", "Verifica tu correo en NexoMail")), "Los mensajes de verificación no deben aparecer como pendientes de respuesta.");
        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("itay@creativeclaw.ai", "Your Creative Claw is running low")), "Los avisos automáticos de créditos no deben aparecer como pendientes de respuesta.");
        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("banco@santander.cl", "Actualiza tu App Santander y descubre lo nuevo")), "Los avisos promocionales de aplicación no deben aparecer como pendientes de respuesta.");
        Ensure(!ControlCenterMessageClassifier.IsNonActionableReceived(Message("info@nic.cl", "Dominio dlarrain.cl a punto de expirar")), "Un vencimiento de dominio debe mantenerse visible como potencialmente accionable.");

        RunAvailabilityRegressionTests().GetAwaiter().GetResult();
        RunSyncStatusRegressionTests().GetAwaiter().GetResult();
    }

    public static void RunEntityMetadataContractTests()
    {
        var entity = new MailMessageIndexEntity
        {
            GmailLabels = "INBOX,UNREAD,CATEGORY_UPDATES",
            AutoSubmitted = "auto-generated",
            Precedence = "bulk",
            HasListUnsubscribe = true,
            IsUnread = true,
            IsInbox = true
        };

        Ensure(entity.IsUnread, "El índice debe persistir estado UNREAD.");
        Ensure(entity.IsInbox, "El índice debe persistir estado INBOX.");
        Ensure(entity.GmailLabels.Contains("CATEGORY_UPDATES", StringComparison.Ordinal), "El índice debe persistir categorías Gmail.");
        Ensure(entity.AutoSubmitted == "auto-generated", "El índice debe persistir Auto-Submitted.");
        Ensure(entity.Precedence == "bulk", "El índice debe persistir Precedence.");
        Ensure(entity.HasListUnsubscribe, "El índice debe persistir presencia de List-Unsubscribe.");

        Ensure(typeof(MailIndexStateEntity).GetProperty("LastSyncAttemptAt") is not null, "El estado del índice debe registrar el último intento de sincronización.");
        Ensure(typeof(MailIndexStateEntity).GetProperty("LastSyncErrorCode") is not null, "El estado del índice debe registrar el tipo del último error de sincronización.");
        Ensure(typeof(ControlCenterAccountSummary).GetProperty("AvailabilityStatus") is not null, "El resumen de cuenta debe distinguir disponible, índice vencido y error OAuth.");
    }

    private static async Task RunAvailabilityRegressionTests()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var freshAccountId = Guid.NewGuid();
        var staleAccountId = Guid.NewGuid();
        var context = new RegressionUserContext(userId);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite(connection).Options;
        await using var database = new NexoMailDbContext(options);
        await database.Database.EnsureCreatedAsync();

        database.Users.Add(new UserEntity { Id = userId, DisplayName = "Regression", Email = "regression@nexomail.test", CreatedAt = now, IsActive = true, IsEmailVerified = true });
        database.MailAccounts.AddRange(
            new MailAccountEntity { Id = freshAccountId, UserId = userId, Provider = MailProviderType.Gmail, EmailAddress = "fresh@nexomail.test", DisplayName = "Fresh", Color = "#111111", IsActive = true, CreatedAt = now },
            new MailAccountEntity { Id = staleAccountId, UserId = userId, Provider = MailProviderType.Gmail, EmailAddress = "stale@nexomail.test", DisplayName = "Stale", Color = "#222222", IsActive = true, CreatedAt = now });
        database.MailIndexStates.AddRange(
            new MailIndexStateEntity { AccountId = freshAccountId, UserId = userId, LastIndexedAt = now, WindowDays = 90, IndexedMessageCount = 1 },
            new MailIndexStateEntity { AccountId = staleAccountId, UserId = userId, LastIndexedAt = now.AddHours(-1), WindowDays = 90, IndexedMessageCount = 1 });
        database.MailMessageIndex.AddRange(
            new MailMessageIndexEntity { Id = Guid.NewGuid(), UserId = userId, AccountId = freshAccountId, ProviderMessageId = "fresh-sent", ThreadId = "fresh-thread", Direction = "sent", FromAddress = "fresh@nexomail.test", ToAddresses = "Persona\tpersona@example.com", Subject = "Pendiente fresco", OccurredAt = now.AddHours(-3), IndexedAt = now, GmailLabels = "SENT" },
            new MailMessageIndexEntity { Id = Guid.NewGuid(), UserId = userId, AccountId = staleAccountId, ProviderMessageId = "stale-sent", ThreadId = "stale-thread", Direction = "sent", FromAddress = "stale@nexomail.test", ToAddresses = "Persona\tpersona@example.com", Subject = "Pendiente vencido", OccurredAt = now.AddHours(-3), IndexedAt = now.AddHours(-1), GmailLabels = "SENT" });
        await database.SaveChangesAsync();

        var snapshot = await new GmailControlCenterService(database, context).GetSnapshotAsync(null, CancellationToken.None);
        Ensure(snapshot.SentWithoutResponse == 1, "Las métricas globales deben excluir mensajes de cuentas con índice vencido.");
        Ensure(snapshot.PendingItems.Count == 1 && snapshot.PendingItems.Single().AccountId == freshAccountId, "La cola operativa debe excluir cuentas con índice vencido.");
        Ensure(snapshot.Activity.Sum(x => x.Sent) == 1, "La actividad agregada debe excluir cuentas con índice vencido.");
        Ensure(snapshot.Accounts.Single(x => x.AccountId == staleAccountId).SentWithoutResponse == 0, "Una cuenta vencida no debe aportar métricas al resumen operativo.");
        Ensure(snapshot.Accounts.Single(x => x.AccountId == staleAccountId).AvailabilityStatus == ControlCenterAvailabilityStatus.Stale, "Una cuenta con índice antiguo debe distinguirse como stale.");
        Ensure(snapshot.UnavailableAccounts == 1, "El resumen debe reportar exactamente la cuenta con índice vencido.");

        var activity = await new GmailControlCenterActivityService(database, context).GetActivityAsync(null, 7, 0, CancellationToken.None);
        Ensure(activity.Activity.Sum(x => x.Sent) == 1, "La actividad detallada debe excluir cuentas con índice vencido.");
        Ensure(activity.Accounts.Single(x => x.AccountId == staleAccountId).Activity.Sum(x => x.Sent) == 0, "La cuenta vencida debe quedar con actividad operativa en cero.");
    }

    private static async Task RunSyncStatusRegressionTests()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var brokenAccountId = Guid.NewGuid();
        var validAccountId = Guid.NewGuid();
        var context = new RegressionUserContext(userId);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite(connection).Options;
        await using var database = new NexoMailDbContext(options);
        await database.Database.EnsureCreatedAsync();

        database.Users.Add(new UserEntity { Id = userId, DisplayName = "Regression", Email = "regression-sync@nexomail.test", CreatedAt = now, IsActive = true, IsEmailVerified = true });
        database.MailAccounts.AddRange(
            new MailAccountEntity { Id = brokenAccountId, UserId = userId, Provider = MailProviderType.Gmail, EmailAddress = "broken@nexomail.test", DisplayName = "A Broken", Color = "#333333", IsActive = true, CreatedAt = now },
            new MailAccountEntity { Id = validAccountId, UserId = userId, Provider = MailProviderType.Gmail, EmailAddress = "valid@nexomail.test", DisplayName = "B Valid", Color = "#444444", IsActive = true, CreatedAt = now });
        database.OAuthCredentials.AddRange(
            new OAuthCredentialEntity { Id = Guid.NewGuid(), MailAccountId = brokenAccountId, EncryptedRefreshToken = "broken-refresh-token", UpdatedAt = now },
            new OAuthCredentialEntity { Id = Guid.NewGuid(), MailAccountId = validAccountId, EncryptedRefreshToken = "valid-refresh-token", UpdatedAt = now });
        await database.SaveChangesAsync();

        var factory = new SyncStatusHttpClientFactory();
        var service = new GmailMetadataIndexService(
            factory,
            database,
            new RegressionTokenProtector(),
            Options.Create(new GmailOptions { ClientId = "test", ClientSecret = "test" }),
            context);

        var result = await service.SyncForUserAsync(userId, 90, 25, CancellationToken.None);
        Ensure(result.Accounts == 2, "El fallo OAuth de una cuenta no debe impedir procesar la siguiente.");
        Ensure(factory.RefreshTokensSeen.Contains("broken-refresh-token"), "La regresión no ejercitó la credencial OAuth inválida.");
        Ensure(factory.RefreshTokensSeen.Contains("valid-refresh-token"), "La cuenta válida no se intentó sincronizar después del fallo OAuth.");

        database.ChangeTracker.Clear();
        var states = await database.MailIndexStates.AsNoTracking().ToDictionaryAsync(x => x.AccountId);
        Ensure(states[brokenAccountId].LastSyncAttemptAt.HasValue, "El fallo OAuth debe registrar el momento del intento.");
        Ensure(states[brokenAccountId].LastSyncErrorCode == ControlCenterAvailabilityStatus.AuthError, "Un rechazo OAuth debe persistirse como auth_error.");
        Ensure(states[validAccountId].LastSyncAttemptAt.HasValue, "La cuenta válida debe registrar su sincronización.");
        Ensure(states[validAccountId].LastSyncErrorCode is null, "Una sincronización exitosa debe limpiar el error persistido.");

        var snapshot = await new GmailControlCenterService(database, context).GetSnapshotAsync(null, CancellationToken.None);
        Ensure(snapshot.Accounts.Single(x => x.AccountId == brokenAccountId).AvailabilityStatus == ControlCenterAvailabilityStatus.AuthError, "El Centro de Control debe exponer auth_error para la cuenta OAuth inválida.");
        Ensure(snapshot.Accounts.Single(x => x.AccountId == validAccountId).AvailabilityStatus == ControlCenterAvailabilityStatus.Available, "La cuenta sincronizada debe quedar disponible.");
    }

    private static ControlCenterMessageMetadata Message(
        string from,
        string subject,
        string labels = "INBOX",
        string autoSubmitted = "",
        string precedence = "",
        bool hasListUnsubscribe = false) =>
        new(from, subject, labels, autoSubmitted, precedence, hasListUnsubscribe);

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RegressionUserContext(Guid userId) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid UserId { get; } = userId;
        public string Email => "regression@nexomail.test";
        public string DisplayName => "Regression";
    }

    private sealed class RegressionTokenProtector : ITokenProtector
    {
        public string Protect(string value) => value;
        public string Unprotect(string protectedValue) => protectedValue;
    }

    private sealed class SyncStatusHttpClientFactory : IHttpClientFactory
    {
        public List<string> RefreshTokensSeen { get; } = [];

        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(new SyncStatusHttpHandler(RefreshTokensSeen));
            if (string.Equals(name, "Gmail", StringComparison.Ordinal))
                client.BaseAddress = new Uri("https://gmail.googleapis.com/gmail/v1/");
            return client;
        }
    }

    private sealed class SyncStatusHttpHandler(List<string> refreshTokensSeen) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (uri.Contains("oauth2.googleapis.com/token", StringComparison.OrdinalIgnoreCase))
            {
                var form = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
                var token = form.Contains("broken-refresh-token", StringComparison.Ordinal) ? "broken-refresh-token" : "valid-refresh-token";
                refreshTokensSeen.Add(token);
                return token == "broken-refresh-token"
                    ? Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}")
                    : Json(HttpStatusCode.OK, "{\"access_token\":\"test-access-token\",\"expires_in\":3600}");
            }

            if (uri.Contains("/users/me/messages?", StringComparison.OrdinalIgnoreCase))
                return Json(HttpStatusCode.OK, "{\"messages\":[]}");

            if (uri.Contains("/users/me/messages/", StringComparison.OrdinalIgnoreCase))
                return Json(HttpStatusCode.NotFound, "{}");

            return Json(HttpStatusCode.NotFound, "{}");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }
}