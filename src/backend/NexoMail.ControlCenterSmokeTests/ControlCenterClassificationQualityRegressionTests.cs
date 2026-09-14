using System.Net;
using System.Runtime.CompilerServices;
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

internal static class ControlCenterClassificationQualityRegressionTests
{
    [ModuleInitializer]
    public static void Initialize() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        RunRealMessageClassificationCases();
        await RunOwnAccountSentRegressionAsync();
        await RunMojibakeIndexRegressionAsync();
    }

    private static void RunRealMessageClassificationCases()
    {
        Ensure(NonActionable("encuestas@duoc.cl", "Encuesta Cultura Institucional y Calidad 2026"), "Las encuestas institucionales no deben quedar pendientes de respuesta.");
        Ensure(NonActionable("comunicaciones@duoc.cl", "Agenda Semanal del 14 al 17 de septiembre"), "Las agendas semanales institucionales no deben quedar pendientes.");
        Ensure(NonActionable("personas@duoc.cl", "¡Muy feliz cumpleaños Ovallinos! Hoy está de cumpleaños... 🎂"), "Los saludos de cumpleaños institucionales no deben quedar pendientes.");
        Ensure(NonActionable("coordinacion@duoc.cl", "Celebración Fiestas Patrias"), "Las invitaciones a celebraciones institucionales no deben quedar pendientes.");
        Ensure(NonActionable("biblioteca@zonavirtual.uisek.cl", "Capacitación Biblioteca Digital eLibro"), "Las invitaciones generales a capacitación no deben quedar pendientes.");
        Ensure(NonActionable("biblioteca@zonavirtual.uisek.cl", "Fe de erratas - Capacitaciones eLibro"), "Las fe de erratas de capacitaciones generales no deben quedar pendientes.");
        Ensure(NonActionable("newsletter@twinkl.cl", "Alivia tu semana y Unidad 4"), "Los boletines educativos/promocionales no deben quedar pendientes.");
        Ensure(NonActionable("procesos@duoc.cl", "La solicitud de Crear Proceso Disponibilidad Docente para CRISTIAN MARCELO LARRAIN LARRAIN ha sido aprobada"), "Las aprobaciones automáticas de procesos no deben quedar pendientes.");
        Ensure(NonActionable("personas@duoc.cl", "Información sobre funcionamiento de los servicios de alimentación"), "Los comunicados generales de funcionamiento no deben quedar pendientes.");

        Ensure(!NonActionable("info@nic.cl", "Dominio dlarrain.cl a punto de expirar"), "Un vencimiento de dominio debe seguir visible como accionable.");
        Ensure(!NonActionable("german.barrientos@duoc.cl", "Coordinación Línea Base Datos 2026-2 BDY1101 - Semana 6"), "Una coordinación directa de trabajo debe seguir visible.");
    }

    private static bool NonActionable(string from, string subject) =>
        ControlCenterMessageClassifier.IsNonActionableReceived(new ControlCenterMessageMetadata(from, subject, "INBOX", "", "", false));

    private static async Task RunOwnAccountSentRegressionAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var firstAccountId = Guid.NewGuid();
        var secondAccountId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite(connection).Options;
        await using var database = new NexoMailDbContext(options);
        await database.Database.EnsureCreatedAsync();

        database.Users.Add(new UserEntity { Id = userId, DisplayName = "Quality", Email = "owner@nexomail.test", CreatedAt = now, IsActive = true, IsEmailVerified = true });
        database.MailAccounts.AddRange(
            new MailAccountEntity { Id = firstAccountId, UserId = userId, Provider = MailProviderType.Gmail, EmailAddress = "personal@nexomail.test", DisplayName = "Personal", Color = "#111111", IsActive = true, CreatedAt = now },
            new MailAccountEntity { Id = secondAccountId, UserId = userId, Provider = MailProviderType.Gmail, EmailAddress = "duoc@nexomail.test", DisplayName = "Duoc", Color = "#222222", IsActive = true, CreatedAt = now });
        database.MailIndexStates.AddRange(
            new MailIndexStateEntity { AccountId = firstAccountId, UserId = userId, LastIndexedAt = now, WindowDays = 90, IndexedMessageCount = 2 },
            new MailIndexStateEntity { AccountId = secondAccountId, UserId = userId, LastIndexedAt = now, WindowDays = 90, IndexedMessageCount = 0 });
        database.MailMessageIndex.AddRange(
            new MailMessageIndexEntity
            {
                Id = Guid.NewGuid(), UserId = userId, AccountId = firstAccountId, ProviderMessageId = "self-test", ThreadId = "self-test-thread",
                Direction = "sent", FromAddress = "personal@nexomail.test", ToAddresses = "Duoc\tduoc@nexomail.test", Subject = "Mensaje de prueba",
                OccurredAt = now.AddHours(-2), IndexedAt = now, GmailLabels = "SENT"
            },
            new MailMessageIndexEntity
            {
                Id = Guid.NewGuid(), UserId = userId, AccountId = firstAccountId, ProviderMessageId = "external", ThreadId = "external-thread",
                Direction = "sent", FromAddress = "personal@nexomail.test", ToAddresses = "Cliente\tcliente@example.com", Subject = "Propuesta pendiente",
                OccurredAt = now.AddHours(-1), IndexedAt = now, GmailLabels = "SENT"
            });
        await database.SaveChangesAsync();

        var snapshot = await new GmailControlCenterService(database, new QualityUserContext(userId)).GetSnapshotAsync(null, CancellationToken.None);
        Ensure(snapshot.SentWithoutResponse == 1, "Los envíos entre cuentas propias conectadas no deben contarse como esperando respuesta.");
        Ensure(snapshot.PendingItems.Count(x => x.Direction == "sent") == 1 && snapshot.PendingItems.Single(x => x.Direction == "sent").Subject == "Propuesta pendiente", "La cola enviada debe conservar el pendiente externo y excluir el envío entre cuentas propias.");
    }

    private static async Task RunMojibakeIndexRegressionAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite(connection).Options;
        await using var database = new NexoMailDbContext(options);
        await database.Database.EnsureCreatedAsync();

        database.Users.Add(new UserEntity { Id = userId, DisplayName = "Encoding", Email = "owner@nexomail.test", CreatedAt = now, IsActive = true, IsEmailVerified = true });
        database.MailAccounts.Add(new MailAccountEntity { Id = accountId, UserId = userId, Provider = MailProviderType.Gmail, EmailAddress = "encoding@nexomail.test", DisplayName = "Encoding", Color = "#333333", IsActive = true, CreatedAt = now });
        database.OAuthCredentials.Add(new OAuthCredentialEntity { Id = Guid.NewGuid(), MailAccountId = accountId, EncryptedRefreshToken = "valid-refresh-token", UpdatedAt = now });
        await database.SaveChangesAsync();

        var service = new GmailMetadataIndexService(
            new MojibakeHttpClientFactory(now),
            database,
            new QualityTokenProtector(),
            Options.Create(new GmailOptions { ClientId = "test-client", ClientSecret = "test-secret" }),
            new QualityUserContext(userId));

        await service.SyncForUserAsync(userId, 90, 25, CancellationToken.None);
        database.ChangeTracker.Clear();
        var indexed = await database.MailMessageIndex.AsNoTracking().SingleAsync(x => x.AccountId == accountId && x.ProviderMessageId == "m1");
        Ensure(indexed.Subject == "Re: Resolución", $"El índice debe reparar mojibake UTF-8 en asuntos. Valor obtenido: {indexed.Subject}");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class QualityUserContext(Guid userId) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid UserId { get; } = userId;
        public string Email => "owner@nexomail.test";
        public string DisplayName => "Quality";
    }

    private sealed class QualityTokenProtector : ITokenProtector
    {
        public string Protect(string value) => value;
        public string Unprotect(string protectedValue) => protectedValue;
    }

    private sealed class MojibakeHttpClientFactory(DateTimeOffset now) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(new MojibakeHttpHandler(now));
            if (string.Equals(name, "Gmail", StringComparison.Ordinal))
                client.BaseAddress = new Uri("https://gmail.googleapis.com/gmail/v1/");
            return client;
        }
    }

    private sealed class MojibakeHttpHandler(DateTimeOffset now) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (uri.Contains("oauth2.googleapis.com/token", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"access_token\":\"access-token\",\"expires_in\":3600}"));
            if (uri.Contains("/users/me/messages/m1", StringComparison.OrdinalIgnoreCase))
            {
                var timestamp = now.AddMinutes(-10).ToUnixTimeMilliseconds();
                var payload = $$"""
                {"id":"m1","threadId":"t1","labelIds":["INBOX"],"internalDate":"{{timestamp}}","snippet":"Prueba","payload":{"headers":[{"name":"From","value":"Persona <persona@example.com>"},{"name":"To","value":"encoding@nexomail.test"},{"name":"Subject","value":"Re: ResoluciÃƒÂ³n"}],"filename":"","mimeType":"text/plain","body":{"size":0}}}
                """;
                return Task.FromResult(Json(HttpStatusCode.OK, payload));
            }
            if (uri.Contains("/users/me/messages?", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"messages\":[{\"id\":\"m1\"}]}"));
            return Task.FromResult(Json(HttpStatusCode.NotFound, "{}"));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }
}
