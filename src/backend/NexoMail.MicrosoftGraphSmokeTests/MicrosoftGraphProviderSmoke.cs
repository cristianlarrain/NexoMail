using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;
using NexoMail.Infrastructure.Microsoft;

internal static class MicrosoftGraphProviderSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"nexomail-msgraph-provider-{Guid.NewGuid():N}.db");
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
                DisplayName = "Graph Provider Smoke",
                Email = "provider-smoke@nexomail.local",
                PlanCode = "Freemium",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
            database.MailAccounts.Add(new MailAccountEntity
            {
                Id = accountId,
                UserId = userId,
                Provider = MailProviderType.MicrosoftGraph,
                EmailAddress = "provider@empresa.test",
                DisplayName = "Microsoft 365",
                Color = "#0078d4",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
            database.OAuthCredentials.Add(new OAuthCredentialEntity
            {
                Id = Guid.NewGuid(),
                MailAccountId = accountId,
                EncryptedRefreshToken = "protected:provider-refresh",
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await database.SaveChangesAsync();

            var handler = new ProviderQueueHttpMessageHandler();
            var factory = new ProviderHttpClientFactory(handler);
            var graphOptions = Options.Create(new MicrosoftGraphOptions
            {
                ClientId = "nexomail-client-id",
                ClientSecret = "nexomail-client-secret"
            });
            var tokenProvider = new MicrosoftGraphTokenProvider(
                factory,
                graphOptions,
                database,
                new ProviderTokenProtector());
            var provider = new MicrosoftGraphMailProvider(factory, tokenProvider);

            handler.EnqueueJson(HttpStatusCode.OK,
                "{\"access_token\":\"provider-access\",\"refresh_token\":\"provider-refresh-rotated\",\"expires_in\":3600}");
            handler.EnqueueJson(HttpStatusCode.OK,
                "{\"@odata.nextLink\":\"https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?$skiptoken=NEXT123\",\"value\":[{\"id\":\"msg-1\",\"from\":{\"emailAddress\":{\"name\":\"Ana Pérez\",\"address\":\"ana@empresa.test\"}},\"subject\":\"Informe\",\"bodyPreview\":\"Vista previa\",\"receivedDateTime\":\"2026-09-11T12:30:00Z\",\"isRead\":false,\"hasAttachments\":true}]}" );

            var page = await provider.GetMessagesAsync(
                new MailQuery(accountId, "inbox", 25),
                CancellationToken.None);

            Ensure(page.Items.Count == 1, "Graph debe mapear un mensaje de inbox.");
            var item = page.Items.Single();
            Ensure(item.ProviderMessageId == "msg-1", "Debe mapear el id Graph.");
            Ensure(item.AccountId == accountId, "Debe conservar el accountId de NexoMail.");
            Ensure(item.SenderName == "Ana Pérez" && item.SenderAddress == "ana@empresa.test", "Debe mapear el remitente.");
            Ensure(item.Subject == "Informe" && item.Preview == "Vista previa", "Debe mapear asunto y preview.");
            Ensure(!item.IsRead && item.HasAttachments && item.FolderId == "inbox", "Debe mapear estado y carpeta inbox.");
            Ensure(!string.IsNullOrWhiteSpace(page.NextCursor), "@odata.nextLink debe transformarse en cursor.");

            var firstGraphRequest = handler.Requests.Single(x => x.Uri.Contains("/mailFolders/inbox/messages", StringComparison.Ordinal));
            Ensure(firstGraphRequest.Authorization == "Bearer provider-access", "Graph debe recibir el access token transitorio.");
            Ensure(firstGraphRequest.Uri.Contains("$top=25", StringComparison.Ordinal), "La consulta debe respetar el tamaño de página.");
            Ensure(firstGraphRequest.Uri.Contains("$orderby=receivedDateTime%20desc", StringComparison.Ordinal) || firstGraphRequest.Uri.Contains("$orderby=receivedDateTime desc", StringComparison.Ordinal),
                "La bandeja debe solicitar orden descendente por fecha.");

            handler.EnqueueJson(HttpStatusCode.OK, "{\"value\":[]}");
            var requestBaseline = handler.Requests.Count;
            _ = await provider.GetMessagesAsync(
                new MailQuery(accountId, "inbox", 25, page.NextCursor),
                CancellationToken.None);
            Ensure(handler.Requests.Count == requestBaseline + 1, "Usar un cursor válido debe hacer exactamente una llamada Graph adicional.");
            Ensure(handler.Requests[^1].Uri == "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?$skiptoken=NEXT123",
                "El cursor debe recuperar exactamente el nextLink validado.");

            foreach (var hostileUrl in new[]
                     {
                         "https://evil.example/v1.0/me/mailFolders/inbox/messages?$skiptoken=x",
                         "http://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?$skiptoken=x",
                         "https://graph.microsoft.com/v1.0/users/otro/messages?$skiptoken=x"
                     })
            {
                var hostileCursor = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(hostileUrl));
                var beforeHostile = handler.Requests.Count;
                await EnsureThrowsAsync<InvalidOperationException>(
                    () => provider.GetMessagesAsync(new MailQuery(accountId, "inbox", 25, hostileCursor), CancellationToken.None),
                    "Un cursor Graph hostil debe rechazarse.");
                Ensure(handler.Requests.Count == beforeHostile, "Un cursor hostil debe fallar antes de cualquier HTTP.");
            }

            handler.EnqueueJson(HttpStatusCode.OK,
                "{\"id\":\"msg-detail\",\"from\":{\"emailAddress\":{\"name\":\"Ana Pérez\",\"address\":\"ana@empresa.test\"}},\"toRecipients\":[{\"emailAddress\":{\"name\":\"Cristián\",\"address\":\"cristian@empresa.test\"}}],\"ccRecipients\":[{\"emailAddress\":{\"name\":\"Equipo\",\"address\":\"equipo@empresa.test\"}}],\"subject\":\"Detalle\",\"body\":{\"contentType\":\"text\",\"content\":\"Hola <equipo>\\nSegunda línea\"},\"bodyPreview\":\"Hola equipo\",\"receivedDateTime\":\"2026-09-11T13:00:00Z\",\"isRead\":false,\"hasAttachments\":false}");

            var detail = await provider.GetMessageAsync(accountId, "msg-detail", CancellationToken.None);
            var requiredDetail = detail ?? throw new InvalidOperationException("Graph debe devolver el detalle del mensaje.");
            Ensure(requiredDetail.ProviderMessageId == "msg-detail" && requiredDetail.AccountId == accountId, "El detalle debe conservar identificadores.");
            Ensure(requiredDetail.From.Name == "Ana Pérez" && requiredDetail.From.Address == "ana@empresa.test", "El detalle debe mapear From.");
            Ensure(requiredDetail.To.Single().Address == "cristian@empresa.test", "El detalle debe mapear To.");
            Ensure(requiredDetail.Cc.Single().Address == "equipo@empresa.test", "El detalle debe mapear Cc.");
            Ensure(requiredDetail.Subject == "Detalle" && requiredDetail.Preview == "Hola equipo", "El detalle debe mapear asunto y preview.");
            Ensure(!requiredDetail.IsRead && requiredDetail.Attachments.Count == 0 && requiredDetail.FolderId == "inbox", "El detalle debe mapear estado sin persistir adjuntos.");
            Ensure(requiredDetail.HtmlBody.Contains("Hola &lt;equipo&gt;", StringComparison.Ordinal), "Un cuerpo text debe escaparse antes de exponerse como HtmlBody.");
            Ensure(requiredDetail.HtmlBody.Contains("<br", StringComparison.OrdinalIgnoreCase), "Los saltos de línea de un cuerpo text deben preservarse como HTML.");

            var detailRequest = handler.Requests[^1];
            Ensure(detailRequest.Method == HttpMethod.Get && detailRequest.Uri.Contains("/v1.0/me/messages/msg-detail", StringComparison.Ordinal),
                "El detalle debe consultarse en me/messages/{id}.");
            Ensure(detailRequest.Uri.Contains("$select=", StringComparison.Ordinal), "El detalle debe limitar campos con $select.");
            Ensure(detailRequest.Authorization == "Bearer provider-access", "El detalle debe usar el access token en memoria.");

            handler.EnqueueJson(HttpStatusCode.NoContent, "{}");
            await provider.MarkReadAsync(accountId, "msg-detail", true, CancellationToken.None);
            var markReadRequest = handler.Requests[^1];
            Ensure(markReadRequest.Method.Method == "PATCH", "Marcar leído debe usar PATCH de Graph.");
            Ensure(markReadRequest.Uri == "https://graph.microsoft.com/v1.0/me/messages/msg-detail", "Marcar leído debe actualizar el mensaje correcto.");
            Ensure(markReadRequest.Body.Contains("\"isRead\":true", StringComparison.OrdinalIgnoreCase), "Marcar leído debe enviar isRead=true.");

            handler.EnqueueJson(HttpStatusCode.NoContent, "{}");
            await provider.MarkReadAsync(accountId, "msg-detail", false, CancellationToken.None);
            var markUnreadRequest = handler.Requests[^1];
            Ensure(markUnreadRequest.Method.Method == "PATCH", "Marcar no leído debe usar PATCH de Graph.");
            Ensure(markUnreadRequest.Body.Contains("\"isRead\":false", StringComparison.OrdinalIgnoreCase), "Marcar no leído debe enviar isRead=false.");
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

    private static async Task EnsureThrowsAsync<TException>(Func<Task> action, string message) where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}

internal sealed class ProviderHttpClientFactory(ProviderQueueHttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal sealed class ProviderQueueHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    public List<ProviderCapturedRequest> Requests { get; } = [];

    public void EnqueueJson(HttpStatusCode statusCode, string json) =>
        _responses.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new ProviderCapturedRequest(
            request.Method,
            request.RequestUri?.ToString() ?? string.Empty,
            body,
            request.Headers.Authorization?.ToString() ?? string.Empty));

        if (_responses.Count == 0)
            throw new InvalidOperationException("El smoke Graph no configuró respuesta HTTP para esta solicitud.");
        return _responses.Dequeue();
    }
}

internal sealed record ProviderCapturedRequest(HttpMethod Method, string Uri, string Body, string Authorization);

internal sealed class ProviderTokenProtector : ITokenProtector
{
    public string Protect(string value) => "protected:" + value;
    public string Unprotect(string protectedValue) => protectedValue.StartsWith("protected:", StringComparison.Ordinal)
        ? protectedValue[10..]
        : protectedValue;
}
