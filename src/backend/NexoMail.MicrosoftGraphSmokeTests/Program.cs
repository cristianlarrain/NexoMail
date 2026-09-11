using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;
using NexoMail.Infrastructure.Microsoft;

var databasePath = Path.Combine(Path.GetTempPath(), $"nexomail-msgraph-{Guid.NewGuid():N}.db");
try
{
    var options = new DbContextOptionsBuilder<NexoMailDbContext>()
        .UseSqlite($"Data Source={databasePath};Pooling=False")
        .Options;

    await using var database = new NexoMailDbContext(options);
    await database.Database.EnsureCreatedAsync();

    var userId = Guid.NewGuid();
    database.Users.Add(new UserEntity
    {
        Id = userId,
        DisplayName = "Microsoft Graph Smoke",
        Email = "msgraph-smoke@nexomail.local",
        PlanCode = CommercialPlanCatalog.Freemium,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow
    });
    database.CommercialPlans.Add(new CommercialPlanEntity
    {
        Code = CommercialPlanCatalog.Freemium,
        Name = "Freemium",
        Price = "$0",
        Cadence = "Test",
        MaxAccounts = 1,
        Description = "Plan de prueba",
        FeaturesJson = "[]",
        EntitlementsJson = "[]",
        IsActive = true,
        SortOrder = 0,
        UpdatedAt = DateTimeOffset.UtcNow
    });
    database.MailAccounts.Add(new MailAccountEntity
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Provider = MailProviderType.Gmail,
        EmailAddress = "existing@nexomail.local",
        DisplayName = "Existing Gmail",
        Color = "#c6524b",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow
    });
    await database.SaveChangesAsync();

    var userContext = new TestUserContext(userId);
    var policy = new MailAccountConnectionPolicy(database, userContext);
    var blocked = false;
    try
    {
        await policy.EnsureCanConnectAnotherAccountAsync(CancellationToken.None);
    }
    catch (InvalidOperationException)
    {
        blocked = true;
    }

    Ensure(blocked, "El límite comercial debe bloquear una segunda cuenta.");

    var httpClientFactory = new FakeHttpClientFactory();
    var dataProtectionProvider = new EphemeralDataProtectionProvider();
    var oauth = new MicrosoftOAuthService(
        httpClientFactory,
        Options.Create(new MicrosoftGraphOptions
        {
            ClientId = "nexomail-client-id",
            ClientSecret = "nexomail-client-secret",
            RedirectUri = "http://localhost:5052/api/oauth/microsoft/callback",
            FrontendUrl = "http://localhost:5173/settings/accounts"
        }),
        database,
        new PassThroughTokenProtector(),
        dataProtectionProvider,
        userContext,
        policy);

    var authorizationUri = new Uri(oauth.BeginAuthorization());
    Ensure(authorizationUri.Scheme == Uri.UriSchemeHttps, "Microsoft OAuth debe usar HTTPS.");
    Ensure(authorizationUri.Host == "login.microsoftonline.com", "Microsoft OAuth debe usar login.microsoftonline.com.");
    Ensure(authorizationUri.AbsolutePath == "/organizations/oauth2/v2.0/authorize",
        "Microsoft OAuth debe usar la autoridad multitenant organizations.");

    var query = QueryHelpers.ParseQuery(authorizationUri.Query);
    Ensure(query["client_id"] == "nexomail-client-id", "La URL debe incluir el client_id configurado.");
    Ensure(query["redirect_uri"] == "http://localhost:5052/api/oauth/microsoft/callback",
        "La URL debe usar el callback de servidor configurado.");
    Ensure(query["response_type"] == "code", "Microsoft OAuth debe usar authorization code flow.");
    Ensure(query["scope"] == "openid profile email offline_access User.Read Mail.ReadWrite",
        "Phase 1 debe solicitar exactamente los scopes aprobados.");
    Ensure(!query["scope"].ToString().Contains("Mail.Send", StringComparison.Ordinal),
        "Phase 1 no debe solicitar Mail.Send.");
    Ensure(!string.IsNullOrWhiteSpace(query["state"]), "La URL de autorización debe incluir state protegido.");

    var validState = query["state"].ToString();
    await EnsureThrowsAsync<InvalidOperationException>(
        () => oauth.CompleteAuthorizationAsync("unused-code", validState + "tampered", CancellationToken.None),
        "Un state alterado debe rechazarse antes de llamar a Microsoft.");
    Ensure(httpClientFactory.CreateClientCalls == 0, "Un state alterado no debe realizar llamadas HTTP.");

    var stateProtector = dataProtectionProvider.CreateProtector("NexoMail.MicrosoftOAuth.State.v1");
    var wrongUserState = stateProtector.Protect(JsonSerializer.Serialize(new
    {
        UserId = Guid.NewGuid(),
        IssuedAt = DateTimeOffset.UtcNow,
        Nonce = "wrong-user-state"
    }));
    await EnsureThrowsAsync<InvalidOperationException>(
        () => oauth.CompleteAuthorizationAsync("unused-code", wrongUserState, CancellationToken.None),
        "Un state perteneciente a otro usuario debe rechazarse antes de llamar a Microsoft.");
    Ensure(httpClientFactory.CreateClientCalls == 0, "Un state de otro usuario no debe realizar llamadas HTTP.");

    var expiredState = stateProtector.Protect(JsonSerializer.Serialize(new
    {
        UserId = userId,
        IssuedAt = DateTimeOffset.UtcNow.AddMinutes(-11),
        Nonce = "expired-state"
    }));
    await EnsureThrowsAsync<InvalidOperationException>(
        () => oauth.CompleteAuthorizationAsync("unused-code", expiredState, CancellationToken.None),
        "Un state de más de diez minutos debe rechazarse antes de llamar a Microsoft.");
    Ensure(httpClientFactory.CreateClientCalls == 0, "Un state expirado no debe realizar llamadas HTTP.");

    Console.WriteLine("Microsoft Graph smoke: PASS");
}
finally
{
    if (File.Exists(databasePath)) File.Delete(databasePath);
}

static void Ensure(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static async Task EnsureThrowsAsync<TException>(Func<Task> action, string message) where TException : Exception
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

sealed class TestUserContext(Guid userId) : IUserContext
{
    public bool IsAuthenticated => true;
    public Guid UserId => userId;
    public string Email => "msgraph-smoke@nexomail.local";
    public string DisplayName => "Microsoft Graph Smoke";
}

sealed class FakeHttpClientFactory : IHttpClientFactory
{
    public int CreateClientCalls { get; private set; }

    public HttpClient CreateClient(string name)
    {
        CreateClientCalls++;
        return new HttpClient();
    }
}

sealed class PassThroughTokenProtector : ITokenProtector
{
    public string Protect(string value) => "protected:" + value;
    public string Unprotect(string protectedValue) => protectedValue.StartsWith("protected:", StringComparison.Ordinal)
        ? protectedValue[10..]
        : protectedValue;
}
