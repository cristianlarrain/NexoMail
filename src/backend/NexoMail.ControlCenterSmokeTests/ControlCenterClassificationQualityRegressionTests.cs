using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Domain;
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
        await RunMojibakeDisplayRegressionAsync();
        await RunPriorityScoringRegressionAsync();
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
        Ensure(NonActionable("paula.serrano@duoc.cl", "RE: 👨‍🍳 ¡¡uuuuuultimos cupos!! 👨‍🍳"), "Las campañas internas de cupos no deben quedar pendientes.");
        Ensure(NonActionable("paula.serrano@duoc.cl", "RE: 👨‍🍳 ¡¡Asegura tu reserva!! 👨‍🍳"), "Las campañas internas de reserva no deben quedar pendientes.");
        Ensure(NonActionable("personas@duoc.cl", "Comunicado Fallecimiento Familiar Daniela Munita"), "Los comunicados institucionales de fallecimiento no deben quedar pendientes.");
        Ensure(NonActionable("personas@duoc.cl", "Bienvenida nueva Directora de Carrera Informática"), "Las comunicaciones institucionales de bienvenida no deben quedar pendientes.");
        Ensure(NonActionable("direccion@duoc.cl", "Información Dirección de Carrera"), "Los informativos generales de dirección de carrera no deben quedar pendientes.");
        Ensure(NonActionable("coordinacion@duoc.cl", "Concurso: Póngale nombre a nuestra FONDA de coordinación Docente"), "Los concursos institucionales no deben quedar pendientes.");
        Ensure(NonActionable("direccion@duoc.cl", "Información Clases Remotas | Viernes 11 Septiembre"), "Los avisos generales de modalidad de clases no deben quedar pendientes.");
        Ensure(NonActionable("coordinacion@duoc.cl", "IMPORTANTE: Horario de atención Coordinación Docente"), "Los avisos generales de horario de atención no deben quedar pendientes.");
        Ensure(NonActionable("coordinacion@duoc.cl", "Estudiante acreditado(a) como cuidador(a) - información para toma de conocimiento RUT: 20404693K"), "Las notificaciones de toma de conocimiento no deben quedar pendientes.");

        Ensure(!NonActionable("info@nic.cl", "Dominio dlarrain.cl a punto de expirar"), "Un vencimiento de dominio debe seguir visible como accionable.");
        Ensure(!NonActionable("german.barrientos@duoc.cl", "Coordinación Línea Base Datos 2026-2 BDY1101 - Semana 6"), "Una coordinación directa de trabajo debe seguir visible.");
        Ensure(!NonActionable("jorge.soto@duoc.cl", "Re: Problema con la asistencia."), "Un problema directo de trabajo debe seguir visible.");
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

    private static async Task RunMojibakeDisplayRegressionAsync()
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
        database.MailIndexStates.Add(new MailIndexStateEntity { AccountId = accountId, UserId = userId, LastIndexedAt = now, WindowDays = 90, IndexedMessageCount = 1 });
        database.MailMessageIndex.Add(new MailMessageIndexEntity
        {
            Id = Guid.NewGuid(), UserId = userId, AccountId = accountId, ProviderMessageId = "m1", ThreadId = "t1",
            Direction = "received", FromName = "Persona", FromAddress = "persona@example.com", ToAddresses = "Encoding\tencoding@nexomail.test",
            Subject = "Re: ResoluciÃƒÂ³n", OccurredAt = now.AddMinutes(-10), IndexedAt = now, GmailLabels = "INBOX", IsInbox = true
        });
        await database.SaveChangesAsync();

        var snapshot = await new GmailControlCenterService(database, new QualityUserContext(userId)).GetSnapshotAsync(null, CancellationToken.None);
        var item = snapshot.PendingItems.Single(x => x.MessageId == "m1");
        Ensure(item.Subject == "Re: Resolución", $"El Centro de Control debe reparar mojibake UTF-8 al mostrar asuntos. Valor obtenido: {item.Subject}");
    }

    private static async Task RunPriorityScoringRegressionAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite(connection).Options;
        await using var database = new NexoMailDbContext(options);
        await database.Database.EnsureCreatedAsync();

        database.Users.Add(new UserEntity { Id = userId, DisplayName = "Priority", Email = "owner@nexomail.test", CreatedAt = now, IsActive = true, IsEmailVerified = true });
        database.MailAccounts.Add(new MailAccountEntity { Id = accountId, UserId = userId, Provider = MailProviderType.Gmail, EmailAddress = "priority@nexomail.test", DisplayName = "Priority", Color = "#444444", IsActive = true, CreatedAt = now });
        database.MailIndexStates.Add(new MailIndexStateEntity { AccountId = accountId, UserId = userId, LastIndexedAt = now, WindowDays = 90, IndexedMessageCount = 8 });

        database.MailMessageIndex.AddRange(
            Received("old-empty", "old-empty", "persona1@example.com", "", now.AddDays(-12), isUnread: false),
            Sent("old-generic", "old-generic", "destino1@example.com", "NexoMail", now.AddDays(-11)),
            Received("coord", "coord", "german.barrientos@duoc.cl", "Coordinación Línea Base Datos 2026-2 BDY1101 - Semana 6", now.AddDays(-4), isUnread: false),
            Received("problem", "problem", "jorge.soto@duoc.cl", "Re: Problema con la asistencia.", now.AddDays(-3), isUnread: false),
            Sent("proposal", "proposal", "cliente@example.com", "Propuesta de capacitación en generación de imágenes y videos con IA", now.AddDays(-2)),
            Received("expiry", "expiry", "info@nic.cl", "Dominio dlarrain.cl a punto de expirar", now.AddDays(-1), isUnread: true),
            Received("confirm", "confirm", "persona2@example.com", "Confirmación requerida para inscripción", now.AddHours(-18), isUnread: true),
            Sent("followup", "followup", "persona3@example.com", "Solicitud de antecedentes pendiente", now.AddHours(-12)));
        await database.SaveChangesAsync();

        var snapshot = await new GmailControlCenterService(database, new QualityUserContext(userId)).GetSnapshotAsync(null, CancellationToken.None);
        var priorityIds = snapshot.PriorityItems.Select(x => x.MessageId).ToArray();

        Ensure(snapshot.PendingItems.Length == 8, "La priorización no debe eliminar elementos de la cola pendiente.");
        Ensure(priorityIds.Length == 6, "La vista de prioridad debe seguir mostrando seis elementos como máximo.");
        Ensure(priorityIds[0] == "expiry", "Un vencimiento no leído y accionable debe quedar por encima de pendientes antiguos genéricos.");
        Ensure(priorityIds.Contains("problem") && priorityIds.Contains("coord"), "Los problemas y coordinaciones directas de trabajo deben quedar dentro de las prioridades.");
        Ensure(!priorityIds.Contains("old-empty"), "Un mensaje antiguo sin asunto no debe ganar prioridad solo por antigüedad.");
        Ensure(!priorityIds.SequenceEqual(snapshot.PendingItems.Take(6).Select(x => x.MessageId)), "PriorityItems no debe ser simplemente los seis pendientes más antiguos.");

        MailMessageIndexEntity Received(string id, string threadId, string from, string subject, DateTimeOffset occurredAt, bool isUnread) => new()
        {
            Id = Guid.NewGuid(), UserId = userId, AccountId = accountId, ProviderMessageId = id, ThreadId = threadId,
            Direction = "received", FromName = from.Split('@')[0], FromAddress = from, ToAddresses = "Priority\tpriority@nexomail.test",
            Subject = subject, OccurredAt = occurredAt, IndexedAt = now, GmailLabels = isUnread ? "INBOX,UNREAD" : "INBOX", IsInbox = true, IsUnread = isUnread
        };

        MailMessageIndexEntity Sent(string id, string threadId, string to, string subject, DateTimeOffset occurredAt) => new()
        {
            Id = Guid.NewGuid(), UserId = userId, AccountId = accountId, ProviderMessageId = id, ThreadId = threadId,
            Direction = "sent", FromName = "Priority", FromAddress = "priority@nexomail.test", ToAddresses = $"Destino\t{to}",
            Subject = subject, OccurredAt = occurredAt, IndexedAt = now, GmailLabels = "SENT"
        };
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
}
