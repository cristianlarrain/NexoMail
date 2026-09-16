using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;

internal static class GmailHistoryExpirySmoke
{
    public static async Task RunAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite(connection).Options;
        await using var database = new NexoMailDbContext(options);
        await database.Database.EnsureCreatedAsync(ct);

        database.Users.Add(new UserEntity
        {
            Id = userId,
            DisplayName = "Expired History User",
            Email = "expired@nexomail.test",
            CreatedAt = now,
            IsActive = true,
            IsEmailVerified = true,
        });
        database.MailAccounts.Add(new MailAccountEntity
        {
            Id = accountId,
            UserId = userId,
            Provider = MailProviderType.Gmail,
            EmailAddress = "expired@nexomail.test",
            DisplayName = "Expired History Gmail",
            Color = "#778899",
            IsActive = true,
            CreatedAt = now,
        });
        database.OAuthCredentials.Add(new OAuthCredentialEntity
        {
            Id = Guid.NewGuid(),
            MailAccountId = accountId,
            EncryptedRefreshToken = "expired-refresh-token",
            UpdatedAt = now,
        });
        database.MailMessageIndex.Add(new MailMessageIndexEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AccountId = accountId,
            ProviderMessageId = "keep-1",
            ThreadId = "thread-keep-1",
            Direction = "received",
            FromName = "Keep",
            FromAddress = "keep@nexomail.test",
            ToAddresses = "Expired\texpired@nexomail.test",
            Subject = "Debe conservarse",
            Snippet = "No borrar si History expiró",
            OccurredAt = now.AddDays(-40),
            IndexedAt = now.AddDays(-1),
            IsInbox = true,
        });
        database.MailAttachmentIndex.Add(new MailAttachmentIndexEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AccountId = accountId,
            ProviderMessageId = "keep-1",
            AttachmentId = "keep-att",
            FileName = "keep.pdf",
            ContentType = "application/pdf",
            Size = 2048,
            IndexedAt = now.AddDays(-1),
        });
        database.MailIndexStates.Add(new MailIndexStateEntity
        {
            AccountId = accountId,
            UserId = userId,
            LastIndexedAt = now.AddMinutes(-30),
            LastSyncAttemptAt = now.AddMinutes(-30),
            WindowDays = 90,
            IndexedMessageCount = 1,
            BackfillStartedAt = now.AddDays(-2),
            BackfillCompletedAt = now.AddDays(-1),
            GmailHistoryId = "200",
        });
        await database.SaveChangesAsync(ct);

        var factory = new ExpiredHistoryHttpClientFactory();
        var service = new GmailMetadataIndexService(
            factory,
            database,
            new ExpiredHistoryTokenProtector(),
            Options.Create(new GmailOptions { ClientId = "test", ClientSecret = "test" }),
            new TestUserContext(userId));

        await service.SyncForUserAsync(userId, 90, 25, ct);
        database.ChangeTracker.Clear();

        var state = await database.MailIndexStates.AsNoTracking().SingleAsync(x => x.AccountId == accountId, ct);
        EnsureExpired(state.GmailHistoryId is null, "Un historyId expirado debe invalidarse para impedir saltos de eventos.");
        EnsureExpired(state.BackfillStartedAt is null && state.BackfillCompletedAt is null && state.BackfillPageToken is null,
            "Al expirar History la cuenta debe requerir una reconciliación completa desde cero.");
        EnsureExpired(state.LastSyncErrorCode == "history_expired", "La cuenta debe quedar marcada explícitamente como history_expired.");

        EnsureExpired(await database.MailMessageIndex.AsNoTracking().AnyAsync(x => x.AccountId == accountId && x.ProviderMessageId == "keep-1", ct),
            "La expiración del checkpoint no autoriza a borrar mensajes ya indexados.");
        EnsureExpired(await database.MailAttachmentIndex.AsNoTracking().AnyAsync(x => x.AccountId == accountId && x.ProviderMessageId == "keep-1", ct),
            "La expiración del checkpoint no autoriza a borrar metadatos de adjuntos existentes.");
        EnsureExpired(factory.HistoryListCalls == 1, "El escenario debe detectar la expiración en la primera llamada a History.");
        EnsureExpired(factory.FullMailboxListCalls == 0, "La misma ejecución que detecta History expirado no debe borrar ni reescanear de forma implícita.");
    }

    private static void EnsureExpired(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

sealed class ExpiredHistoryTokenProtector : ITokenProtector
{
    public string Protect(string value) => value;
    public string Unprotect(string protectedValue) => protectedValue;
}

sealed class ExpiredHistoryHttpClientFactory : IHttpClientFactory
{
    private readonly ExpiredHistoryHttpHandler _handler = new();
    public int HistoryListCalls => _handler.HistoryListCalls;
    public int FullMailboxListCalls => _handler.FullMailboxListCalls;

    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient(_handler, disposeHandler: false);
        if (string.Equals(name, "Gmail", StringComparison.Ordinal))
            client.BaseAddress = new Uri("https://gmail.googleapis.com/gmail/v1/");
        return client;
    }
}

sealed class ExpiredHistoryHttpHandler : HttpMessageHandler
{
    public int HistoryListCalls { get; private set; }
    public int FullMailboxListCalls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
        if (uri.Contains("oauth2.googleapis.com/token", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(Json(HttpStatusCode.OK, "{\"access_token\":\"expired-access-token\",\"expires_in\":3600}"));

        if (uri.Contains("/users/me/history?", StringComparison.OrdinalIgnoreCase))
        {
            HistoryListCalls++;
            return Task.FromResult(Json(HttpStatusCode.NotFound, "{\"error\":{\"code\":404,\"message\":\"HistoryId 200 not found\"}}"));
        }

        if (uri.Contains("/users/me/messages?", StringComparison.OrdinalIgnoreCase))
        {
            if (uri.Contains("includeSpamTrash=true", StringComparison.OrdinalIgnoreCase) && !uri.Contains("&q=", StringComparison.OrdinalIgnoreCase))
                FullMailboxListCalls++;
            return Task.FromResult(Json(HttpStatusCode.OK, "{\"messages\":[]}"));
        }

        return Task.FromResult(Json(HttpStatusCode.NotFound, "{}"));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };
}
