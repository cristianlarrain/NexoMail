using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;
using NexoMail.Infrastructure.Microsoft;

internal static class GmailIsolationSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"nexomail-gmail-isolation-{Guid.NewGuid():N}.db");
        try
        {
            var dbOptions = new DbContextOptionsBuilder<NexoMailDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;
            await using var database = new NexoMailDbContext(dbOptions);
            await database.Database.EnsureCreatedAsync();

            var userId = Guid.NewGuid();
            var gmailAccountId = Guid.NewGuid();
            var microsoftAccountId = Guid.NewGuid();
            database.Users.Add(new UserEntity
            {
                Id = userId,
                DisplayName = "Gmail Isolation Smoke",
                Email = "gmail-isolation@nexomail.local",
                PlanCode = "Freemium",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
            database.MailAccounts.AddRange(
                new MailAccountEntity
                {
                    Id = gmailAccountId,
                    UserId = userId,
                    Provider = MailProviderType.Gmail,
                    EmailAddress = "gmail@empresa.test",
                    DisplayName = "Gmail Test",
                    Color = "#c6524b",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new MailAccountEntity
                {
                    Id = microsoftAccountId,
                    UserId = userId,
                    Provider = MailProviderType.MicrosoftGraph,
                    EmailAddress = "microsoft@empresa.test",
                    DisplayName = "Microsoft 365",
                    Color = "#0078d4",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            database.OAuthCredentials.AddRange(
                new OAuthCredentialEntity
                {
                    Id = Guid.NewGuid(),
                    MailAccountId = gmailAccountId,
                    EncryptedRefreshToken = "protected:gmail-refresh",
                    UpdatedAt = DateTimeOffset.UtcNow
                },
                new OAuthCredentialEntity
                {
                    Id = Guid.NewGuid(),
                    MailAccountId = microsoftAccountId,
                    EncryptedRefreshToken = "protected:microsoft-refresh",
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            await database.SaveChangesAsync();

            var userContext = new TestUserContext(userId);
            var tokenProtector = new PassThroughTokenProtector();
            var gmailOptions = Options.Create(new GmailOptions
            {
                ClientId = "google-client-id",
                ClientSecret = "google-client-secret",
                RedirectUri = "http://localhost:5052/api/oauth/google/callback",
                FrontendUrl = "http://localhost:5173/settings/accounts"
            });

            var contactsHandler = new IsolationQueueHttpMessageHandler();
            var contactsFactory = new IsolationHttpClientFactory(contactsHandler);
            var contacts = new GoogleContactsService(contactsFactory, database, tokenProtector, gmailOptions, userContext);
            await CaptureThrowsAsync<InvalidOperationException>(
                () => contacts.GetContactsAsync(microsoftAccountId, null, CancellationToken.None),
                "GoogleContactsService debe rechazar cuentas Microsoft.");
            Ensure(contactsHandler.Requests.Count == 0,
                "Contactos debe rechazar Microsoft antes de cualquier llamada HTTP a Google.");

            var rulesHandler = new IsolationQueueHttpMessageHandler();
            rulesHandler.EnqueueJson(HttpStatusCode.OK, "{\"access_token\":\"google-access\",\"expires_in\":3600}");
            rulesHandler.EnqueueJson(HttpStatusCode.OK, "{\"labels\":[]}");
            rulesHandler.EnqueueJson(HttpStatusCode.OK, "{\"filter\":[]}");
            var rulesFactory = new IsolationHttpClientFactory(rulesHandler);
            var rules = new GmailRuleService(rulesFactory, database, tokenProtector, gmailOptions);
            await CaptureThrowsAsync<InvalidOperationException>(
                () => rules.ListAsync(microsoftAccountId, CancellationToken.None),
                "GmailRuleService debe rechazar cuentas Microsoft antes de consultar Gmail.");
            Ensure(rulesHandler.Requests.Count == 0,
                "Reglas debe rechazar Microsoft antes de cualquier llamada HTTP a Google.");

            var controlHandler = new IsolationQueueHttpMessageHandler();
            var controlFactory = new IsolationHttpClientFactory(controlHandler);
            var controlCenter = new GmailControlCenterService(controlFactory, database, tokenProtector, gmailOptions, userContext);
            _ = await controlCenter.GetSnapshotAsync(microsoftAccountId, CancellationToken.None);
            Ensure(controlHandler.Requests.Count == 0,
                "Centro de Control debe excluir una cuenta Microsoft antes de cualquier llamada HTTP a Google.");

            var activityHandler = new IsolationQueueHttpMessageHandler();
            var activityFactory = new IsolationHttpClientFactory(activityHandler);
            var activity = new GmailControlCenterActivityService(activityFactory, database, tokenProtector, gmailOptions, userContext);
            _ = await activity.GetActivityAsync(microsoftAccountId, 7, 0, CancellationToken.None);
            Ensure(activityHandler.Requests.Count == 0,
                "Actividad del Centro de Control debe excluir una cuenta Microsoft antes de cualquier llamada HTTP a Google.");

            var metadataHandler = new IsolationQueueHttpMessageHandler();
            metadataHandler.EnqueueJson(HttpStatusCode.OK, "{\"access_token\":\"google-access\",\"expires_in\":3600}");
            metadataHandler.EnqueueJson(HttpStatusCode.OK, "{}");
            var metadataFactory = new IsolationHttpClientFactory(metadataHandler);
            var metadata = new GmailMetadataIndexService(metadataFactory, database, tokenProtector, gmailOptions, userContext);
            _ = await metadata.SyncAsync(30, 25, CancellationToken.None);
            Ensure(metadataHandler.Requests.Count == 2,
                "Metadata debe procesar sólo la cuenta Gmail activa del fixture.");
            Ensure(metadataHandler.Requests.All(request => !request.Body.Contains("microsoft-refresh", StringComparison.Ordinal)),
                "Metadata nunca debe enviar el refresh token Microsoft a Google.");
            Ensure(metadataHandler.Requests.Any(request => request.Body.Contains("gmail-refresh", StringComparison.Ordinal)),
                "Metadata debe renovar únicamente la credencial Gmail del fixture.");

            var unsupportedHandler = new IsolationQueueHttpMessageHandler();
            var unsupportedFactory = new IsolationHttpClientFactory(unsupportedHandler);
            var microsoftTokenProvider = new MicrosoftGraphTokenProvider(
                unsupportedFactory,
                Options.Create(new MicrosoftGraphOptions
                {
                    ClientId = "microsoft-client-id",
                    ClientSecret = "microsoft-client-secret"
                }),
                database,
                tokenProtector);
            var microsoftProvider = new MicrosoftGraphMailProvider(unsupportedFactory, microsoftTokenProvider);
            await CaptureThrowsAsync<NotSupportedException>(
                () => microsoftProvider.MoveToTrashAsync(microsoftAccountId, "message-1", CancellationToken.None),
                "MoveToTrashAsync debe seguir explícitamente no soportado en Microsoft Phase 1.");
            Ensure(unsupportedHandler.Requests.Count == 0,
                "Una operación Microsoft no soportada no debe realizar una escritura Graph.");
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task<TException> CaptureThrowsAsync<TException>(Func<Task> action, string message)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException(message);
    }
}

internal sealed class IsolationHttpClientFactory(IsolationQueueHttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient(handler, disposeHandler: false);
        if (string.Equals(name, "Gmail", StringComparison.Ordinal))
            client.BaseAddress = new Uri("https://gmail.googleapis.com/gmail/v1/");
        else if (string.Equals(name, "GooglePeople", StringComparison.Ordinal))
            client.BaseAddress = new Uri("https://people.googleapis.com/v1/");
        return client;
    }
}

internal sealed class IsolationQueueHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    public List<IsolationCapturedRequest> Requests { get; } = [];

    public void EnqueueJson(HttpStatusCode statusCode, string json) =>
        _responses.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new IsolationCapturedRequest(
            request.Method,
            request.RequestUri?.ToString() ?? string.Empty,
            body));

        if (_responses.Count == 0)
            throw new InvalidOperationException("El smoke de aislamiento Gmail no configuró respuesta HTTP.");
        return _responses.Dequeue();
    }
}

internal sealed record IsolationCapturedRequest(HttpMethod Method, string Uri, string Body);
