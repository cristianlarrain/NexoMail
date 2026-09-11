using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;
using NexoMail.Infrastructure.Microsoft;

internal static class MicrosoftGraphProviderErrorsSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"nexomail-msgraph-errors-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<NexoMailDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;
            await using var database = new NexoMailDbContext(options);
            await database.Database.EnsureCreatedAsync();

            var userId = Guid.NewGuid();
            var accountId = Guid.NewGuid();
            database.Users.Add(new UserEntity
            {
                Id = userId,
                DisplayName = "Graph Error Smoke",
                Email = "graph-errors@nexomail.local",
                PlanCode = "Freemium",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
            database.MailAccounts.Add(new MailAccountEntity
            {
                Id = accountId,
                UserId = userId,
                Provider = MailProviderType.MicrosoftGraph,
                EmailAddress = "errors@empresa.test",
                DisplayName = "Microsoft 365",
                Color = "#0078d4",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
            database.OAuthCredentials.Add(new OAuthCredentialEntity
            {
                Id = Guid.NewGuid(),
                MailAccountId = accountId,
                EncryptedRefreshToken = "protected:error-refresh",
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await database.SaveChangesAsync();

            var handler = new ErrorQueueHttpMessageHandler();
            var factory = new ErrorHttpClientFactory(handler);
            var tokenProvider = new MicrosoftGraphTokenProvider(
                factory,
                Options.Create(new MicrosoftGraphOptions
                {
                    ClientId = "nexomail-client-id",
                    ClientSecret = "nexomail-client-secret"
                }),
                database,
                new ErrorTokenProtector());
            var provider = new MicrosoftGraphMailProvider(factory, tokenProvider);

            handler.EnqueueJson(HttpStatusCode.OK,
                "{\"access_token\":\"error-access\",\"refresh_token\":\"error-refresh-rotated\",\"expires_in\":3600}");

            const string sensitiveMarker = "SENSITIVE-TOKEN-DO-NOT-ECHO";

            handler.EnqueueJson(HttpStatusCode.Unauthorized, $"{{\"error\":\"{sensitiveMarker}\"}}");
            var unauthorized = await CaptureThrowsAsync<InvalidOperationException>(
                () => provider.GetMessagesAsync(new MailQuery(accountId, "inbox", 10), CancellationToken.None),
                "401 de Graph debe convertirse en InvalidOperationException segura.");
            Ensure(unauthorized.Message == "Microsoft 365 rechazó el acceso al buzón. Vuelve a conectar la cuenta o revisa los permisos de la organización.",
                "401 debe usar el mensaje seguro acordado.");
            Ensure(!unauthorized.Message.Contains(sensitiveMarker, StringComparison.Ordinal),
                "401 no debe filtrar el cuerpo de respuesta de Graph.");

            handler.EnqueueJson(HttpStatusCode.Forbidden, $"{{\"error\":\"{sensitiveMarker}\"}}");
            var forbidden = await CaptureThrowsAsync<InvalidOperationException>(
                () => provider.GetMessagesAsync(new MailQuery(accountId, "inbox", 10), CancellationToken.None),
                "403 de Graph debe convertirse en InvalidOperationException segura.");
            Ensure(forbidden.Message == "Microsoft 365 rechazó el acceso al buzón. Vuelve a conectar la cuenta o revisa los permisos de la organización.",
                "403 debe usar el mensaje seguro acordado.");
            Ensure(!forbidden.Message.Contains(sensitiveMarker, StringComparison.Ordinal),
                "403 no debe filtrar el cuerpo de respuesta de Graph.");

            handler.EnqueueJson(HttpStatusCode.TooManyRequests, $"{{\"error\":\"{sensitiveMarker}\"}}");
            var throttled = await CaptureThrowsAsync<InvalidOperationException>(
                () => provider.GetMessagesAsync(new MailQuery(accountId, "inbox", 10), CancellationToken.None),
                "429 de Graph debe convertirse en InvalidOperationException segura.");
            Ensure(throttled.Message == "Microsoft 365 está limitando temporalmente las solicitudes. Inténtalo nuevamente en unos minutos.",
                "429 debe usar el mensaje seguro acordado.");
            Ensure(!throttled.Message.Contains(sensitiveMarker, StringComparison.Ordinal),
                "429 no debe filtrar el cuerpo de respuesta de Graph.");

            handler.EnqueueJson(HttpStatusCode.ServiceUnavailable, $"{{\"error\":\"{sensitiveMarker}\"}}");
            var unavailable = await CaptureThrowsAsync<HttpRequestException>(
                () => provider.GetMessagesAsync(new MailQuery(accountId, "inbox", 10), CancellationToken.None),
                "5xx de Graph debe convertirse en HttpRequestException segura.");
            Ensure(unavailable.Message == "Microsoft 365 no está disponible temporalmente.",
                "5xx debe usar el mensaje seguro acordado.");
            Ensure(!unavailable.Message.Contains(sensitiveMarker, StringComparison.Ordinal),
                "5xx no debe filtrar el cuerpo de respuesta de Graph.");

            handler.EnqueueJson(HttpStatusCode.BadRequest, $"{{\"error\":\"{sensitiveMarker}\"}}");
            var generic = await CaptureThrowsAsync<HttpRequestException>(
                () => provider.GetMessagesAsync(new MailQuery(accountId, "inbox", 10), CancellationToken.None),
                "Otros errores Graph deben convertirse en HttpRequestException segura.");
            Ensure(generic.Message == "Microsoft Graph no pudo completar la operación.",
                "Otros errores deben usar el mensaje seguro acordado.");
            Ensure(!generic.Message.Contains(sensitiveMarker, StringComparison.Ordinal),
                "Los errores Graph no deben filtrar el cuerpo de respuesta.");

            var requestCountBeforeMinimalMethods = handler.Requests.Count;
            var thread = await provider.GetThreadAsync(accountId, "msg-1", CancellationToken.None);
            Ensure(thread.Count == 0, "Phase 1 debe devolver hilo vacío para Microsoft 365.");

            var folders = await provider.GetFoldersAsync(accountId, CancellationToken.None);
            Ensure(folders.Count == 1, "Phase 1 debe exponer sólo inbox para Microsoft 365.");
            var inbox = folders.Single();
            Ensure(inbox.Id == "inbox", "La única carpeta Microsoft Phase 1 debe ser inbox.");
            Ensure(handler.Requests.Count == requestCountBeforeMinimalMethods,
                "GetThreadAsync y GetFoldersAsync mínimos no deben llamar a Graph.");

            var unsupported = await CaptureThrowsAsync<NotSupportedException>(
                () => provider.GetAttachmentAsync(accountId, "msg-1", "att-1", CancellationToken.None),
                "Descargar adjuntos debe seguir no soportado en Phase 1.");
            Ensure(unsupported.Message == "Esta operación aún no está disponible para Microsoft 365 en esta fase.",
                "Las operaciones no soportadas deben usar un mensaje consistente.");
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

internal sealed class ErrorHttpClientFactory(ErrorQueueHttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal sealed class ErrorQueueHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    public List<ErrorCapturedRequest> Requests { get; } = [];

    public void EnqueueJson(HttpStatusCode statusCode, string json) =>
        _responses.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new ErrorCapturedRequest(
            request.Method,
            request.RequestUri?.ToString() ?? string.Empty,
            body,
            request.Headers.Authorization?.ToString() ?? string.Empty));

        if (_responses.Count == 0)
            throw new InvalidOperationException("El smoke de errores Graph no configuró respuesta HTTP.");
        return _responses.Dequeue();
    }
}

internal sealed record ErrorCapturedRequest(HttpMethod Method, string Uri, string Body, string Authorization);

internal sealed class ErrorTokenProtector : ITokenProtector
{
    public string Protect(string value) => "protected:" + value;
    public string Unprotect(string protectedValue) => protectedValue.StartsWith("protected:", StringComparison.Ordinal)
        ? protectedValue[10..]
        : protectedValue;
}
