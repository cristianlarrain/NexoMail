using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;

namespace NexoMail.ControlCenterSmokeTests;

internal static class GoogleOAuthReconnectRegressionTests
{
    [ModuleInitializer]
    public static void Initialize() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        const string accountEmail = "duoc@nexomail.test";

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite(connection).Options;
        await using var database = new NexoMailDbContext(dbOptions);
        await database.Database.EnsureCreatedAsync();

        database.Users.Add(new UserEntity
        {
            Id = userId,
            DisplayName = "Reconnect regression",
            Email = "owner@nexomail.test",
            CreatedAt = now,
            IsActive = true,
            IsEmailVerified = true,
        });
        database.MailAccounts.Add(new MailAccountEntity
        {
            Id = accountId,
            UserId = userId,
            Provider = MailProviderType.Gmail,
            EmailAddress = accountEmail,
            DisplayName = "Duoc",
            Color = "#445566",
            IsActive = true,
            CreatedAt = now,
        });
        database.OAuthCredentials.Add(new OAuthCredentialEntity
        {
            Id = Guid.NewGuid(),
            MailAccountId = accountId,
            EncryptedRefreshToken = "old-refresh-token",
            UpdatedAt = now,
        });
        await database.SaveChangesAsync();

        var factory = new ReconnectHttpClientFactory();
        var service = new GoogleOAuthService(
            factory,
            Options.Create(new GmailOptions
            {
                ClientId = "test-client",
                ClientSecret = "test-secret",
                RedirectUri = "https://nexomail.test/api/oauth/google/callback",
                FrontendUrl = "https://nexomail.test/",
            }),
            database,
            new PassthroughProtector(),
            new EphemeralDataProtectionProvider(),
            new ReconnectUserContext(userId));

        var reconnectMethod = typeof(GoogleOAuthService).GetMethod(
            "BeginReauthorizationAsync",
            [typeof(Guid), typeof(CancellationToken)]);
        Ensure(reconnectMethod is not null, "GoogleOAuthService debe exponer una reconexión explícita por accountId.");

        var repoRoot = FindRepositoryRoot();
        var apiSource = await File.ReadAllTextAsync(Path.Combine(repoRoot, "src", "backend", "NexoMail.Api", "Program.cs"));
        Ensure(apiSource.Contains("/google/reconnect/{accountId:guid}", StringComparison.Ordinal), "La API debe exponer un endpoint de reconexión Gmail por accountId.");
        var accountsSource = await File.ReadAllTextAsync(Path.Combine(repoRoot, "src", "frontend", "src", "pages", "AccountsPage.tsx"));
        Ensure(accountsSource.Contains("Reconectar", StringComparison.Ordinal) && accountsSource.Contains("/api/oauth/google/reconnect/", StringComparison.Ordinal), "La pantalla de cuentas debe ofrecer una acción visible Reconectar para Gmail.");

        factory.ProfileEmail = "otra@nexomail.test";
        var mismatchUrl = await InvokeReconnectStartAsync(reconnectMethod!, service, accountId);
        var mismatchState = QueryValue(mismatchUrl, "state");
        var mismatchRejected = false;
        try
        {
            await service.CompleteAuthorizationAsync("test-code", mismatchState, CancellationToken.None);
        }
        catch (InvalidOperationException exception)
        {
            mismatchRejected = exception.Message.Contains("misma cuenta", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("no corresponde", StringComparison.OrdinalIgnoreCase);
        }
        Ensure(mismatchRejected, "La reconexión debe rechazar una cuenta Google distinta de la cuenta seleccionada.");

        database.ChangeTracker.Clear();
        var credentialAfterMismatch = await database.OAuthCredentials.AsNoTracking().SingleAsync(x => x.MailAccountId == accountId);
        Ensure(credentialAfterMismatch.EncryptedRefreshToken == "old-refresh-token", "Una reconexión con correo distinto no debe reemplazar la credencial existente.");
        Ensure(await database.MailAccounts.AsNoTracking().CountAsync(x => x.UserId == userId) == 1, "Reconectar no debe crear otra cuenta ni consumir otro cupo.");

        factory.ProfileEmail = accountEmail;
        var reconnectUrl = await InvokeReconnectStartAsync(reconnectMethod!, service, accountId);
        var reconnectState = QueryValue(reconnectUrl, "state");
        await service.CompleteAuthorizationAsync("test-code", reconnectState, CancellationToken.None);

        database.ChangeTracker.Clear();
        var credentialAfterReconnect = await database.OAuthCredentials.AsNoTracking().SingleAsync(x => x.MailAccountId == accountId);
        Ensure(credentialAfterReconnect.EncryptedRefreshToken == "new-refresh-token", "La reconexión válida debe reemplazar el refresh token de la cuenta existente.");
        Ensure(await database.MailAccounts.AsNoTracking().CountAsync(x => x.UserId == userId) == 1, "La reconexión válida debe conservar una sola cuenta.");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "backend", "NexoMail.Api", "Program.cs")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("No fue posible localizar la raíz del repositorio para validar el cableado de reconexión.");
    }

    private static async Task<string> InvokeReconnectStartAsync(System.Reflection.MethodInfo method, GoogleOAuthService service, Guid accountId)
    {
        var value = method.Invoke(service, [accountId, CancellationToken.None]);
        Ensure(value is Task<string>, "La reconexión debe devolver una URL OAuth de forma asíncrona.");
        return await (Task<string>)value!;
    }

    private static string QueryValue(string url, string key)
    {
        var query = new Uri(url).Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), key, StringComparison.Ordinal))
                return Uri.UnescapeDataString(parts[1]);
        }
        throw new InvalidOperationException($"La URL OAuth no contiene {key}.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ReconnectUserContext(Guid userId) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid UserId { get; } = userId;
        public string Email => "owner@nexomail.test";
        public string DisplayName => "Reconnect regression";
    }

    private sealed class PassthroughProtector : ITokenProtector
    {
        public string Protect(string value) => value;
        public string Unprotect(string protectedValue) => protectedValue;
    }

    private sealed class ReconnectHttpClientFactory : IHttpClientFactory
    {
        public string ProfileEmail { get; set; } = "duoc@nexomail.test";

        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(new ReconnectHttpHandler(this));
            if (string.Equals(name, "Gmail", StringComparison.Ordinal))
                client.BaseAddress = new Uri("https://gmail.googleapis.com/gmail/v1/");
            return client;
        }
    }

    private sealed class ReconnectHttpHandler(ReconnectHttpClientFactory owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (uri.Contains("oauth2.googleapis.com/token", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"access_token\":\"new-access-token\",\"refresh_token\":\"new-refresh-token\",\"expires_in\":3600}"));
            if (uri.Contains("/users/me/profile", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Json(HttpStatusCode.OK, $"{{\"emailAddress\":\"{owner.ProfileEmail}\"}}"));
            return Task.FromResult(Json(HttpStatusCode.NotFound, "{}"));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }
}
