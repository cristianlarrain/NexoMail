using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;

internal static class GmailBackfillSmoke
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
            DisplayName = "Backfill User",
            Email = "backfill@nexomail.test",
            CreatedAt = now,
            IsActive = true,
            IsEmailVerified = true,
        });
        database.MailAccounts.Add(new MailAccountEntity
        {
            Id = accountId,
            UserId = userId,
            Provider = MailProviderType.Gmail,
            EmailAddress = "backfill@nexomail.test",
            DisplayName = "Backfill Gmail",
            Color = "#334455",
            IsActive = true,
            CreatedAt = now,
        });
        database.OAuthCredentials.Add(new OAuthCredentialEntity
        {
            Id = Guid.NewGuid(),
            MailAccountId = accountId,
            EncryptedRefreshToken = "backfill-refresh-token",
            UpdatedAt = now,
        });
        await database.SaveChangesAsync(ct);

        var factory = new PagedBackfillHttpClientFactory();
        var service = new GmailMetadataIndexService(
            factory,
            database,
            new BackfillTokenProtector(),
            Options.Create(new GmailOptions { ClientId = "test", ClientSecret = "test" }),
            new TestUserContext(userId));

        await service.SyncForUserAsync(userId, 90, 25, ct);
        database.ChangeTracker.Clear();
        var state1 = await database.MailIndexStates.AsNoTracking().SingleAsync(x => x.AccountId == accountId, ct);
        EnsureBackfill(state1.BackfillStartedAt is not null, "El primer ciclo debe registrar el inicio del backfill completo.");
        EnsureBackfill(state1.BackfillCompletedAt is null, "El backfill no puede declararse completo tras consumir sólo la primera página.");
        EnsureBackfill(state1.BackfillPageToken == "p2", "El primer ciclo debe persistir el token de la segunda página.");
        EnsureBackfill(state1.GmailHistoryId is null, "Durante un backfill incompleto no debe persistirse un checkpoint History que pueda expirar antes de completar el barrido.");
        EnsureBackfill(await IndexedCount(database, userId, accountId, ct) == 25, "El primer ciclo debe indexar exactamente la primera página de 25 mensajes.");

        await service.SyncForUserAsync(userId, 90, 25, ct);
        database.ChangeTracker.Clear();
        var state2 = await database.MailIndexStates.AsNoTracking().SingleAsync(x => x.AccountId == accountId, ct);
        EnsureBackfill(state2.BackfillCompletedAt is null, "El segundo ciclo todavía no debe declarar completitud.");
        EnsureBackfill(state2.BackfillPageToken == "p3", "El segundo ciclo debe persistir el token de la tercera página.");
        EnsureBackfill(state2.GmailHistoryId is null, "El checkpoint History debe seguir vacío hasta completar la última página del backfill.");
        EnsureBackfill(await IndexedCount(database, userId, accountId, ct) == 50, "El segundo ciclo debe acumular 50 mensajes sin duplicarlos.");

        await service.SyncForUserAsync(userId, 90, 25, ct);
        database.ChangeTracker.Clear();
        var state3 = await database.MailIndexStates.AsNoTracking().SingleAsync(x => x.AccountId == accountId, ct);
        EnsureBackfill(state3.BackfillCompletedAt is not null, "El tercer ciclo debe declarar la finalización del backfill.");
        EnsureBackfill(state3.BackfillPageToken is null, "Al completar el backfill no debe quedar page token pendiente.");
        EnsureBackfill(state3.GmailHistoryId == "500", "Al completar el backfill debe persistirse un checkpoint History fresco.");

        var indexedIds = (await database.MailMessageIndex
                .AsNoTracking()
                .Where(x => x.UserId == userId && x.AccountId == accountId)
                .Select(x => x.ProviderMessageId)
                .ToArrayAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        EnsureBackfill(indexedIds.SetEquals(factory.AllProviderIds), "El backfill completo debe contener exactamente todos los IDs del proveedor.");
        EnsureBackfill(factory.FullBackfillListCalls == 3, "El escenario debe consumir exactamente tres páginas completas del proveedor.");
        EnsureBackfill(factory.ProfileCalls == 1, "El checkpoint History debe capturarse una sola vez y únicamente al completar el backfill.");
        EnsureBackfill(factory.HistoryListCalls == 1, "History debe reproducirse al completar el backfill sin reescanearlo.");
    }

    private static Task<int> IndexedCount(NexoMailDbContext db, Guid userId, Guid accountId, CancellationToken ct) =>
        db.MailMessageIndex.AsNoTracking().CountAsync(x => x.UserId == userId && x.AccountId == accountId, ct);

    private static void EnsureBackfill(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

sealed class BackfillTokenProtector : ITokenProtector
{
    public string Protect(string value) => value;
    public string Unprotect(string protectedValue) => protectedValue;
}

sealed class PagedBackfillHttpClientFactory : IHttpClientFactory
{
    private readonly PagedBackfillHttpHandler _handler = new();
    public IReadOnlySet<string> AllProviderIds => _handler.AllProviderIds;
    public int FullBackfillListCalls => _handler.FullBackfillListCalls;
    public int ProfileCalls => _handler.ProfileCalls;
    public int HistoryListCalls => _handler.HistoryListCalls;

    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient(_handler, disposeHandler: false);
        if (string.Equals(name, "Gmail", StringComparison.Ordinal))
            client.BaseAddress = new Uri("https://gmail.googleapis.com/gmail/v1/");
        return client;
    }
}

sealed class PagedBackfillHttpHandler : HttpMessageHandler
{
    private readonly string[] _allProviderIds = Enumerable.Range(1, 60).Select(i => $"bf-{i:00}").ToArray();
    public IReadOnlySet<string> AllProviderIds => _allProviderIds.ToHashSet(StringComparer.Ordinal);
    public int FullBackfillListCalls { get; private set; }
    public int ProfileCalls { get; private set; }
    public int HistoryListCalls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;

        if (uri.Contains("oauth2.googleapis.com/token", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(Json(HttpStatusCode.OK, "{\"access_token\":\"backfill-access-token\",\"expires_in\":3600}"));

        if (uri.Contains("/users/me/profile", StringComparison.OrdinalIgnoreCase))
        {
            ProfileCalls++;
            return Task.FromResult(Json(HttpStatusCode.OK, "{\"emailAddress\":\"backfill@nexomail.test\",\"messagesTotal\":60,\"threadsTotal\":60,\"historyId\":\"500\"}"));
        }

        if (uri.Contains("/users/me/history?", StringComparison.OrdinalIgnoreCase))
        {
            HistoryListCalls++;
            return Task.FromResult(Json(HttpStatusCode.OK, "{\"history\":[],\"historyId\":\"500\"}"));
        }

        if (uri.Contains("/users/me/messages?", StringComparison.OrdinalIgnoreCase))
        {
            // Existing recent/unread refresh paths are deliberately empty in this scenario.
            // The complete backfill path is identified by includeSpamTrash=true and no q= filter.
            if (!uri.Contains("includeSpamTrash=true", StringComparison.OrdinalIgnoreCase) || uri.Contains("&q=", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"messages\":[]}"));

            FullBackfillListCalls++;
            var token = QueryValue(request.RequestUri!, "pageToken");
            return Task.FromResult(token switch
            {
                null => MessagePage(_allProviderIds[..25], "p2"),
                "p2" => MessagePage(_allProviderIds[25..50], "p3"),
                "p3" => MessagePage(_allProviderIds[50..], null),
                _ => Json(HttpStatusCode.BadRequest, "{\"error\":\"unexpected_page_token\"}"),
            });
        }

        if (uri.Contains("/users/me/messages/", StringComparison.OrdinalIgnoreCase))
        {
            var marker = "/users/me/messages/";
            var start = uri.IndexOf(marker, StringComparison.OrdinalIgnoreCase) + marker.Length;
            var tail = uri[start..];
            var id = Uri.UnescapeDataString(tail.Split('?', 2)[0]);
            return Task.FromResult(AllProviderIds.Contains(id)
                ? Metadata(id)
                : Json(HttpStatusCode.NotFound, "{}"));
        }

        return Task.FromResult(Json(HttpStatusCode.NotFound, "{}"));
    }

    private static HttpResponseMessage MessagePage(IEnumerable<string> ids, string? nextPageToken)
    {
        var messages = string.Join(',', ids.Select(id => $"{{\"id\":\"{id}\",\"threadId\":\"thread-{id}\"}}"));
        var next = nextPageToken is null ? string.Empty : $",\"nextPageToken\":\"{nextPageToken}\"";
        return Json(HttpStatusCode.OK, $"{{\"messages\":[{messages}]{next}}}");
    }

    private static HttpResponseMessage Metadata(string id)
    {
        var millis = DateTimeOffset.UtcNow.AddMinutes(-int.Parse(id[3..])).ToUnixTimeMilliseconds();
        var body = $"{{\"id\":\"{id}\",\"threadId\":\"thread-{id}\",\"labelIds\":[\"INBOX\"],\"internalDate\":\"{millis}\",\"snippet\":\"Snippet {id}\",\"payload\":{{\"headers\":[{{\"name\":\"From\",\"value\":\"Sender <sender@nexomail.test>\"}},{{\"name\":\"To\",\"value\":\"Backfill <backfill@nexomail.test>\"}},{{\"name\":\"Subject\",\"value\":\"Subject {id}\"}}],\"filename\":\"\",\"mimeType\":\"text/plain\",\"body\":{{}}}}}}";
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
