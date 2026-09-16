using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;

internal static class GmailReconciliationSmoke
{
    public static async Task RunAsync(CancellationToken ct)
    {
        await RunMismatchAndRepairAsync(ct);
        await RunAuthorizationGuardAsync(ct);
        await RunProviderFailureSafetyAsync(ct);
    }

    private static async Task RunMismatchAndRepairAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var dbOptions = new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite(connection).Options;
        await using var database = new NexoMailDbContext(dbOptions);
        await database.Database.EnsureCreatedAsync(ct);

        database.Users.Add(new UserEntity
        {
            Id = userId,
            DisplayName = "Reconciliation User",
            Email = "reconcile@nexomail.test",
            CreatedAt = now,
            IsActive = true,
            IsEmailVerified = true,
        });
        database.MailAccounts.Add(new MailAccountEntity
        {
            Id = accountId,
            UserId = userId,
            Provider = MailProviderType.Gmail,
            EmailAddress = "reconcile@nexomail.test",
            DisplayName = "Reconciliation Gmail",
            Color = "#445566",
            IsActive = true,
            CreatedAt = now,
        });
        database.OAuthCredentials.Add(new OAuthCredentialEntity
        {
            Id = Guid.NewGuid(),
            MailAccountId = accountId,
            EncryptedRefreshToken = "reconcile-refresh-token",
            UpdatedAt = now,
        });
        database.MailIndexStates.Add(new MailIndexStateEntity
        {
            AccountId = accountId,
            UserId = userId,
            LastIndexedAt = now,
            WindowDays = 90,
            IndexedMessageCount = 3,
            BackfillStartedAt = now.AddMinutes(-10),
            BackfillCompletedAt = now.AddMinutes(-5),
            GmailHistoryId = "700",
        });
        database.MailMessageIndex.AddRange(
            Indexed(userId, accountId, "A", now.AddMinutes(-3)),
            Indexed(userId, accountId, "B", now.AddMinutes(-2)),
            Indexed(userId, accountId, "ORPHAN", now.AddMinutes(-1)));
        await database.SaveChangesAsync(ct);

        var factory = new ReconciliationHttpClientFactory();
        var metadata = MetadataService(factory, database, userId);
        var service = new GmailIndexReconciliationService(database, metadata, new TestUserContext(userId));

        var diagnostic = await service.ReconcileAsync(accountId, repairMissing: false, ct);
        Ensure(diagnostic.ProviderCount == 3, "La reconciliación debe enumerar los tres mensajes del proveedor.");
        Ensure(diagnostic.IndexedCount == 3, "El diagnóstico inicial debe observar tres filas indexadas.");
        Ensure(diagnostic.MissingProviderIds.SequenceEqual(new[] { "C" }), "Debe detectar C como faltante del índice.");
        Ensure(diagnostic.OrphanIndexedIds.SequenceEqual(new[] { "ORPHAN" }), "Debe detectar ORPHAN como fila no presente en Gmail.");
        Ensure(diagnostic.DuplicateIndexedIds == 0, "El índice no debe contener IDs duplicados.");
        Ensure(diagnostic.Repaired == 0 && !diagnostic.IsHealthy, "Un diagnóstico sin reparación debe permanecer no saludable.");
        Ensure(factory.MetadataRequests == 0, "repairMissing=false no debe cargar metadata de mensajes faltantes.");

        var repaired = await service.ReconcileAsync(accountId, repairMissing: true, ct);
        Ensure(repaired.MissingProviderIds.Count == 0, "La reparación debe incorporar el ID C faltante.");
        Ensure(repaired.OrphanIndexedIds.SequenceEqual(new[] { "ORPHAN" }), "La reconciliación no puede borrar automáticamente un huérfano.");
        Ensure(repaired.Repaired == 1, "Debe informar exactamente un mensaje reparado.");
        Ensure(!repaired.IsHealthy, "Mientras exista un huérfano inexplicado, la cuenta no puede declararse saludable.");
        Ensure(factory.MetadataRequests == 1, "La reparación debe cargar sólo el metadata del ID faltante C.");
        Ensure(await database.MailMessageIndex.AsNoTracking().AnyAsync(x => x.AccountId == accountId && x.ProviderMessageId == "C", ct), "C debe quedar persistido en el índice.");
        Ensure(await database.MailMessageIndex.AsNoTracking().AnyAsync(x => x.AccountId == accountId && x.ProviderMessageId == "ORPHAN", ct), "ORPHAN debe conservarse hasta una eliminación confirmada por el proveedor.");

        var confirmedDeletedOrphan = await database.MailMessageIndex.SingleAsync(
            x => x.AccountId == accountId && x.ProviderMessageId == "ORPHAN", ct);
        database.MailMessageIndex.Remove(confirmedDeletedOrphan);
        await database.SaveChangesAsync(ct);
        var healthy = await service.ReconcileAsync(accountId, repairMissing: false, ct);
        Ensure(healthy.MissingProviderIds.Count == 0, "La paridad final no puede dejar mensajes faltantes.");
        Ensure(healthy.DuplicateIndexedIds == 0, "La paridad final no puede contener duplicados.");
        Ensure(healthy.OrphanIndexedIds.Count == 0, "La paridad final no puede contener huérfanos no explicados.");
        Ensure(healthy.IsHealthy, "La paridad final proveedor/índice debe quedar saludable.");

        database.ChangeTracker.Clear();
        var state = await database.MailIndexStates.AsNoTracking().SingleAsync(x => x.AccountId == accountId, ct);
        Ensure(state.LastReconciledAt is not null, "Una reconciliación completada debe persistir LastReconciledAt.");
        Ensure(state.LastReconciliationErrorCode is null, "Una reconciliación completada debe limpiar errores previos.");
    }

    private static async Task RunAuthorizationGuardAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var foreignAccountId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite(connection).Options;
        await using var database = new NexoMailDbContext(options);
        await database.Database.EnsureCreatedAsync(ct);
        database.Users.AddRange(
            new UserEntity { Id = ownerId, DisplayName = "Owner", Email = "owner-reconcile@nexomail.test", CreatedAt = now, IsActive = true, IsEmailVerified = true },
            new UserEntity { Id = otherUserId, DisplayName = "Other", Email = "other-reconcile@nexomail.test", CreatedAt = now, IsActive = true, IsEmailVerified = true });
        database.MailAccounts.Add(new MailAccountEntity
        {
            Id = foreignAccountId,
            UserId = otherUserId,
            Provider = MailProviderType.Gmail,
            EmailAddress = "foreign-reconcile@nexomail.test",
            DisplayName = "Foreign Gmail",
            Color = "#556677",
            IsActive = true,
            CreatedAt = now,
        });
        database.OAuthCredentials.Add(new OAuthCredentialEntity
        {
            Id = Guid.NewGuid(), MailAccountId = foreignAccountId, EncryptedRefreshToken = "foreign-token", UpdatedAt = now,
        });
        await database.SaveChangesAsync(ct);

        var factory = new ReconciliationHttpClientFactory();
        var metadata = MetadataService(factory, database, ownerId);
        var service = new GmailIndexReconciliationService(database, metadata, new TestUserContext(ownerId));
        var rejected = false;
        try
        {
            await service.ReconcileAsync(foreignAccountId, repairMissing: true, ct);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Ensure(rejected, "La reconciliación debe rechazar una cuenta que pertenece a otro usuario.");
        Ensure(factory.TotalRequests == 0, "Una cuenta ajena debe rechazarse antes de cualquier acceso al proveedor.");
    }

    private static async Task RunProviderFailureSafetyAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite(connection).Options;
        await using var database = new NexoMailDbContext(options);
        await database.Database.EnsureCreatedAsync(ct);
        database.Users.Add(new UserEntity { Id = userId, DisplayName = "Failure", Email = "failure@nexomail.test", CreatedAt = now, IsActive = true, IsEmailVerified = true });
        database.MailAccounts.Add(new MailAccountEntity
        {
            Id = accountId, UserId = userId, Provider = MailProviderType.Gmail, EmailAddress = "failure@nexomail.test",
            DisplayName = "Failure Gmail", Color = "#667788", IsActive = true, CreatedAt = now,
        });
        database.OAuthCredentials.Add(new OAuthCredentialEntity
        {
            Id = Guid.NewGuid(), MailAccountId = accountId, EncryptedRefreshToken = "failure-token", UpdatedAt = now,
        });
        database.MailIndexStates.Add(new MailIndexStateEntity
        {
            AccountId = accountId, UserId = userId, LastIndexedAt = now, WindowDays = 90, IndexedMessageCount = 1,
            BackfillStartedAt = now.AddMinutes(-10), BackfillCompletedAt = now.AddMinutes(-5), GmailHistoryId = "900",
        });
        database.MailMessageIndex.Add(Indexed(userId, accountId, "KEEP", now.AddMinutes(-1)));
        await database.SaveChangesAsync(ct);

        var factory = new FailingReconciliationHttpClientFactory();
        var metadata = MetadataService(factory, database, userId);
        var service = new GmailIndexReconciliationService(database, metadata, new TestUserContext(userId));
        var failed = false;
        try
        {
            await service.ReconcileAsync(accountId, repairMissing: true, ct);
        }
        catch (HttpRequestException)
        {
            failed = true;
        }

        Ensure(failed, "Un error de listado Gmail debe propagarse como fallo de reconciliación.");
        database.ChangeTracker.Clear();
        Ensure(await database.MailMessageIndex.AsNoTracking().AnyAsync(x => x.AccountId == accountId && x.ProviderMessageId == "KEEP", ct), "Un fallo del proveedor no puede borrar correo ya indexado.");
        var state = await database.MailIndexStates.AsNoTracking().SingleAsync(x => x.AccountId == accountId, ct);
        Ensure(state.LastReconciliationErrorCode == "reconcile_error", "El fallo del proveedor debe persistirse como reconcile_error.");
        Ensure(state.LastReconciledAt is null, "Una reconciliación fallida no puede aparentar una reconciliación exitosa.");
    }

    private static GmailMetadataIndexService MetadataService(IHttpClientFactory factory, NexoMailDbContext database, Guid userId) =>
        new(factory, database, new ReconciliationTokenProtector(), Options.Create(new GmailOptions { ClientId = "test", ClientSecret = "test" }), new TestUserContext(userId));

    private static MailMessageIndexEntity Indexed(Guid userId, Guid accountId, string id, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        AccountId = accountId,
        ProviderMessageId = id,
        ThreadId = $"thread-{id}",
        Direction = "received",
        FromName = "Sender",
        FromAddress = "sender@nexomail.test",
        ToAddresses = "Owner\treconcile@nexomail.test",
        Subject = $"Subject {id}",
        Snippet = $"Snippet {id}",
        OccurredAt = at,
        IndexedAt = DateTimeOffset.UtcNow,
        IsInbox = true,
    };

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

sealed class ReconciliationTokenProtector : ITokenProtector
{
    public string Protect(string value) => value;
    public string Unprotect(string protectedValue) => protectedValue;
}

sealed class ReconciliationHttpClientFactory : IHttpClientFactory
{
    private readonly ReconciliationHttpHandler _handler = new();
    public int MetadataRequests => _handler.MetadataRequests;
    public int TotalRequests => _handler.TotalRequests;

    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient(_handler, disposeHandler: false);
        if (string.Equals(name, "Gmail", StringComparison.Ordinal))
            client.BaseAddress = new Uri("https://gmail.googleapis.com/gmail/v1/");
        return client;
    }
}

sealed class ReconciliationHttpHandler : HttpMessageHandler
{
    public int MetadataRequests { get; private set; }
    public int TotalRequests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        TotalRequests++;
        var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
        if (uri.Contains("oauth2.googleapis.com/token", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(Json(HttpStatusCode.OK, "{\"access_token\":\"reconcile-access-token\",\"expires_in\":3600}"));

        if (uri.Contains("/users/me/messages?", StringComparison.OrdinalIgnoreCase))
        {
            var body = "{\"messages\":[{\"id\":\"A\",\"threadId\":\"thread-A\"},{\"id\":\"B\",\"threadId\":\"thread-B\"},{\"id\":\"C\",\"threadId\":\"thread-C\"}]}";
            return Task.FromResult(Json(HttpStatusCode.OK, body));
        }

        if (uri.Contains("/users/me/messages/C", StringComparison.OrdinalIgnoreCase))
        {
            MetadataRequests++;
            var millis = DateTimeOffset.UtcNow.AddMinutes(-4).ToUnixTimeMilliseconds();
            var body = $"{{\"id\":\"C\",\"threadId\":\"thread-C\",\"labelIds\":[\"INBOX\"],\"internalDate\":\"{millis}\",\"snippet\":\"Snippet C\",\"payload\":{{\"headers\":[{{\"name\":\"From\",\"value\":\"Sender <sender@nexomail.test>\"}},{{\"name\":\"To\",\"value\":\"Owner <reconcile@nexomail.test>\"}},{{\"name\":\"Subject\",\"value\":\"Subject C\"}}],\"filename\":\"\",\"mimeType\":\"text/plain\",\"body\":{{}}}}}}";
            return Task.FromResult(Json(HttpStatusCode.OK, body));
        }

        return Task.FromResult(Json(HttpStatusCode.NotFound, "{}"));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };
}

sealed class FailingReconciliationHttpClientFactory : IHttpClientFactory
{
    private readonly FailingReconciliationHttpHandler _handler = new();

    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient(_handler, disposeHandler: false);
        if (string.Equals(name, "Gmail", StringComparison.Ordinal))
            client.BaseAddress = new Uri("https://gmail.googleapis.com/gmail/v1/");
        return client;
    }
}

sealed class FailingReconciliationHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
        if (uri.Contains("oauth2.googleapis.com/token", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(Json(HttpStatusCode.OK, "{\"access_token\":\"failure-access-token\",\"expires_in\":3600}"));
        if (uri.Contains("/users/me/messages?", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(Json(HttpStatusCode.ServiceUnavailable, "{\"error\":\"provider_unavailable\"}"));
        return Task.FromResult(Json(HttpStatusCode.NotFound, "{}"));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };
}
