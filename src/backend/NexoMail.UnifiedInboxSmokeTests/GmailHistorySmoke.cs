using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;

internal static class GmailHistorySmoke
{
    public static async Task RunAsync(CancellationToken ct)
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
            DisplayName = "History User",
            Email = "history@nexomail.test",
            CreatedAt = now,
            IsActive = true,
            IsEmailVerified = true,
        });
        database.MailAccounts.Add(new MailAccountEntity
        {
            Id = accountId,
            UserId = userId,
            Provider = MailProviderType.Gmail,
            EmailAddress = "history@nexomail.test",
            DisplayName = "History Gmail",
            Color = "#556677",
            IsActive = true,
            CreatedAt = now,
        });
        database.OAuthCredentials.Add(new OAuthCredentialEntity
        {
            Id = Guid.NewGuid(),
            MailAccountId = accountId,
            EncryptedRefreshToken = "history-refresh-token",
            UpdatedAt = now,
        });
        database.MailMessageIndex.AddRange(
            Indexed(userId, accountId, "old-label", now.AddDays(-20), isInbox: true),
            Indexed(userId, accountId, "deleted-1", now.AddDays(-30), isInbox: true));
        database.MailAttachmentIndex.Add(new MailAttachmentIndexEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AccountId = accountId,
            ProviderMessageId = "deleted-1",
            AttachmentId = "att-deleted",
            FileName = "deleted.pdf",
            ContentType = "application/pdf",
            Size = 1024,
            IndexedAt = now,
        });
        database.MailIndexStates.Add(new MailIndexStateEntity
        {
            AccountId = accountId,
            UserId = userId,
            LastIndexedAt = now.AddMinutes(-10),
            LastSyncAttemptAt = now.AddMinutes(-10),
            WindowDays = 90,
            IndexedMessageCount = 2,
            BackfillStartedAt = now.AddHours(-2),
            BackfillCompletedAt = now.AddHours(-1),
            GmailHistoryId = "100",
        });
        await database.SaveChangesAsync(ct);

        var factory = new HistoryHttpClientFactory();
        var service = new GmailMetadataIndexService(
            factory,
            database,
            new HistoryTokenProtector(),
            Options.Create(new GmailOptions { ClientId = "test", ClientSecret = "test" }),
            new TestUserContext(userId));

        await service.SyncForUserAsync(userId, 90, 25, ct);
        database.ChangeTracker.Clear();

        var state = await database.MailIndexStates.AsNoTracking().SingleAsync(x => x.AccountId == accountId, ct);
        EnsureHistory(state.GmailHistoryId == "102", "History debe avanzar hasta el último historyId procesado.");
        EnsureHistory(state.LastSyncErrorCode is null, "Una sincronización History exitosa debe quedar sin error.");

        var rows = await database.MailMessageIndex.AsNoTracking()
            .Where(x => x.UserId == userId && x.AccountId == accountId)
            .ToDictionaryAsync(x => x.ProviderMessageId, ct);
        EnsureHistory(rows.ContainsKey("new-1"), "History debe incorporar mensajes nuevos sin reescanear el buzón completo.");
        EnsureHistory(rows.TryGetValue("old-label", out var relabeled) && !relabeled.IsInbox,
            "History debe refrescar metadatos cuando cambian las etiquetas de un mensaje existente.");
        EnsureHistory(!rows.ContainsKey("deleted-1"), "History debe eliminar del índice un mensaje cuya eliminación confirmó Gmail.");

        var deletedAttachmentExists = await database.MailAttachmentIndex.AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.AccountId == accountId && x.ProviderMessageId == "deleted-1", ct);
        EnsureHistory(!deletedAttachmentExists, "La eliminación confirmada por Gmail debe retirar también los metadatos de adjuntos asociados.");

        EnsureHistory(factory.HistoryListCalls == 2, "El escenario debe consumir exactamente dos páginas de Gmail History.");
        EnsureHistory(factory.FullMailboxListCalls == 0, "La sincronización incremental no debe volver a listar el buzón completo.");
    }

    private static MailMessageIndexEntity Indexed(Guid userId, Guid accountId, string id, DateTimeOffset at, bool isInbox) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        AccountId = accountId,
        ProviderMessageId = id,
        ThreadId = $"thread-{id}",
        Direction = "received",
        FromName = "Sender",
        FromAddress = "sender@nexomail.test",
        ToAddresses = "History\thistory@nexomail.test",
        Subject = $"Subject {id}",
        Snippet = $"Snippet {id}",
        OccurredAt = at,
        IndexedAt = at,
        IsInbox = isInbox,
    };

    private static void EnsureHistory(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

sealed class HistoryTokenProtector : ITokenProtector
{
    public string Protect(string value) => value;
    public string Unprotect(string protectedValue) => protectedValue;
}

sealed class HistoryHttpClientFactory : IHttpClientFactory
{
    private readonly HistoryHttpHandler _handler = new();
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

sealed class HistoryHttpHandler : HttpMessageHandler
{
    public int HistoryListCalls { get; private set; }
    public int FullMailboxListCalls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;

        if (uri.Contains("oauth2.googleapis.com/token", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(Json(HttpStatusCode.OK, "{\"access_token\":\"history-access-token\",\"expires_in\":3600}"));

        if (uri.Contains("/users/me/history?", StringComparison.OrdinalIgnoreCase))
        {
            HistoryListCalls++;
            var pageToken = QueryValue(request.RequestUri!, "pageToken");
            return Task.FromResult(pageToken switch
            {
                null => Json(HttpStatusCode.OK,
                    "{\"history\":[{\"id\":\"101\",\"messagesAdded\":[{\"message\":{\"id\":\"new-1\",\"threadId\":\"thread-new-1\"}}],\"labelsRemoved\":[{\"message\":{\"id\":\"old-label\",\"threadId\":\"thread-old-label\"},\"labelIds\":[\"INBOX\"]}]}],\"nextPageToken\":\"h2\",\"historyId\":\"101\"}"),
                "h2" => Json(HttpStatusCode.OK,
                    "{\"history\":[{\"id\":\"102\",\"messagesDeleted\":[{\"message\":{\"id\":\"deleted-1\",\"threadId\":\"thread-deleted-1\"}}]}],\"historyId\":\"102\"}"),
                _ => Json(HttpStatusCode.BadRequest, "{\"error\":\"unexpected_page_token\"}"),
            });
        }

        if (uri.Contains("/users/me/messages?", StringComparison.OrdinalIgnoreCase))
        {
            if (uri.Contains("includeSpamTrash=true", StringComparison.OrdinalIgnoreCase) && !uri.Contains("&q=", StringComparison.OrdinalIgnoreCase))
                FullMailboxListCalls++;
            return Task.FromResult(Json(HttpStatusCode.OK, "{\"messages\":[]}"));
        }

        if (uri.Contains("/users/me/messages/", StringComparison.OrdinalIgnoreCase))
        {
            var marker = "/users/me/messages/";
            var start = uri.IndexOf(marker, StringComparison.OrdinalIgnoreCase) + marker.Length;
            var tail = uri[start..];
            var id = Uri.UnescapeDataString(tail.Split('?', 2)[0]);
            return Task.FromResult(id switch
            {
                "new-1" => Metadata("new-1", ["INBOX", "UNREAD"]),
                "old-label" => Metadata("old-label", []),
                _ => Json(HttpStatusCode.NotFound, "{}"),
            });
        }

        return Task.FromResult(Json(HttpStatusCode.NotFound, "{}"));
    }

    private static HttpResponseMessage Metadata(string id, IReadOnlyCollection<string> labels)
    {
        var millis = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();
        var labelJson = string.Join(',', labels.Select(x => $"\"{x}\""));
        var body = $"{{\"id\":\"{id}\",\"threadId\":\"thread-{id}\",\"labelIds\":[{labelJson}],\"internalDate\":\"{millis}\",\"snippet\":\"Snippet {id}\",\"payload\":{{\"headers\":[{{\"name\":\"From\",\"value\":\"Sender <sender@nexomail.test>\"}},{{\"name\":\"To\",\"value\":\"History <history@nexomail.test>\"}},{{\"name\":\"Subject\",\"value\":\"Subject {id}\"}}],\"filename\":\"\",\"mimeType\":\"text/plain\",\"body\":{{}}}}}}";
        return Json(HttpStatusCode.OK, body);
    }

    private static string? QueryValue(Uri uri, string name)
    {
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in query)
        {
            var parts = pair.Split('=', 2);
            if (parts.Length > 0 && string.Equals(Uri.UnescapeDataString(parts[0]), name, StringComparison.OrdinalIgnoreCase))
                return parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        }
        return null;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };
}
