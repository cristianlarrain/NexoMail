using System.Net;
using System.Net.Http.Headers;
using System.Text;
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
    var freemiumPlan = new CommercialPlanEntity
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
    };
    database.CommercialPlans.Add(freemiumPlan);
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

    // El resto del fixture prueba OAuth, no límites comerciales.
    freemiumPlan.MaxAccounts = 10;
    await database.SaveChangesAsync();

    var httpHandler = new QueueHttpMessageHandler();
    var httpClientFactory = new FakeHttpClientFactory(httpHandler);
    var dataProtectionProvider = new EphemeralDataProtectionProvider();
    var tokenProtector = new PassThroughTokenProtector();
    var microsoftOptions = Options.Create(new MicrosoftGraphOptions
    {
        ClientId = "nexomail-client-id",
        ClientSecret = "nexomail-client-secret",
        RedirectUri = "http://localhost:5052/api/oauth/microsoft/callback",
        FrontendUrl = "http://localhost:5173/settings/accounts"
    });
    var oauth = new MicrosoftOAuthService(
        httpClientFactory,
        microsoftOptions,
        database,
        tokenProtector,
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

    httpHandler.EnqueueJson(HttpStatusCode.OK,
        "{\"access_token\":\"test-access-token\",\"refresh_token\":\"test-refresh-token\",\"expires_in\":3600}");
    httpHandler.EnqueueJson(HttpStatusCode.OK,
        "{\"id\":\"graph-test-user\",\"displayName\":\"Microsoft Test\",\"mail\":\"persona@empresa.test\",\"userPrincipalName\":\"persona@empresa.test\"}");

    var createState = QueryHelpers.ParseQuery(new Uri(oauth.BeginAuthorization()).Query)["state"].ToString();
    await oauth.CompleteAuthorizationAsync("create-code", createState, CancellationToken.None);

    var microsoftAccount = await database.MailAccounts.SingleAsync(
        x => x.UserId == userId && x.EmailAddress == "persona@empresa.test");
    Ensure(microsoftAccount.Provider == MailProviderType.MicrosoftGraph, "La cuenta creada debe ser MicrosoftGraph.");
    Ensure(microsoftAccount.DisplayName == "Microsoft 365", "La cuenta Microsoft debe mostrarse como Microsoft 365.");
    Ensure(microsoftAccount.Color == "#0078d4", "La cuenta Microsoft debe usar el color acordado.");
    Ensure(microsoftAccount.IsActive, "La cuenta Microsoft recién conectada debe quedar activa.");

    var credential = await database.OAuthCredentials.SingleAsync(x => x.MailAccountId == microsoftAccount.Id);
    Ensure(credential.EncryptedRefreshToken == "protected:test-refresh-token",
        "El refresh token debe persistirse protegido.");
    Ensure(!credential.EncryptedRefreshToken.Contains("test-access-token", StringComparison.Ordinal),
        "El access token no debe persistirse en la credencial.");
    Ensure(typeof(OAuthCredentialEntity).GetProperties().All(x =>
            !x.Name.Contains("AccessToken", StringComparison.OrdinalIgnoreCase)),
        "El modelo persistente no debe incorporar un campo de access token.");

    Ensure(httpHandler.Requests.Count >= 2, "El callback válido debe intercambiar código y consultar /me.");
    var tokenRequest = httpHandler.Requests[0];
    Ensure(tokenRequest.Method == HttpMethod.Post && tokenRequest.Uri == MicrosoftOAuthService.TokenEndpoint,
        "El código debe intercambiarse en el endpoint organizations/token.");
    Ensure(tokenRequest.Body.Contains("code=create-code", StringComparison.Ordinal), "El token request debe incluir el código.");
    Ensure(tokenRequest.Body.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A5052%2Fapi%2Foauth%2Fmicrosoft%2Fcallback", StringComparison.Ordinal),
        "El token request debe incluir el callback configurado.");
    Ensure(tokenRequest.Body.Contains("scope=openid+profile+email+offline_access+User.Read+Mail.ReadWrite", StringComparison.Ordinal),
        "El token request debe incluir exactamente los scopes de Phase 1.");
    var meRequest = httpHandler.Requests[1];
    Ensure(meRequest.Uri == MicrosoftOAuthService.GraphMeEndpoint, "El callback debe consultar Graph /me.");
    Ensure(meRequest.Authorization == "Bearer test-access-token", "Graph /me debe usar el access token sólo de forma transitoria.");

    microsoftAccount.IsActive = false;
    await database.SaveChangesAsync();
    httpHandler.EnqueueJson(HttpStatusCode.OK,
        "{\"access_token\":\"test-access-reconnect\",\"refresh_token\":\"test-refresh-reconnect\",\"expires_in\":3600}");
    httpHandler.EnqueueJson(HttpStatusCode.OK,
        "{\"id\":\"graph-test-user\",\"displayName\":\"Microsoft Test\",\"mail\":\"persona@empresa.test\",\"userPrincipalName\":\"persona@empresa.test\"}");
    var reconnectState = QueryHelpers.ParseQuery(new Uri(oauth.BeginAuthorization()).Query)["state"].ToString();
    await oauth.CompleteAuthorizationAsync("reconnect-code", reconnectState, CancellationToken.None);

    database.ChangeTracker.Clear();
    var reconnectedAccounts = await database.MailAccounts
        .Where(x => x.UserId == userId && x.EmailAddress == "persona@empresa.test")
        .ToArrayAsync();
    Ensure(reconnectedAccounts.Length == 1, "Reconectar Microsoft no debe duplicar la cuenta.");
    Ensure(reconnectedAccounts[0].IsActive, "Reconectar debe reactivar la cuenta existente.");
    var reconnectedCredential = await database.OAuthCredentials.SingleAsync(x => x.MailAccountId == reconnectedAccounts[0].Id);
    Ensure(reconnectedCredential.EncryptedRefreshToken == "protected:test-refresh-reconnect",
        "Reconectar debe reemplazar el refresh token protegido.");

    httpHandler.EnqueueJson(HttpStatusCode.OK,
        "{\"access_token\":\"test-access-upn\",\"refresh_token\":\"test-refresh-upn\",\"expires_in\":3600}");
    httpHandler.EnqueueJson(HttpStatusCode.OK,
        "{\"id\":\"graph-upn-user\",\"displayName\":\"UPN Test\",\"mail\":null,\"userPrincipalName\":\"fallback@empresa.test\"}");
    var fallbackState = QueryHelpers.ParseQuery(new Uri(oauth.BeginAuthorization()).Query)["state"].ToString();
    await oauth.CompleteAuthorizationAsync("fallback-code", fallbackState, CancellationToken.None);
    Ensure(await database.MailAccounts.AnyAsync(x =>
            x.UserId == userId && x.Provider == MailProviderType.MicrosoftGraph && x.EmailAddress == "fallback@empresa.test"),
        "Si mail viene nulo, Microsoft OAuth debe usar userPrincipalName.");

    database.MailAccounts.Add(new MailAccountEntity
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Provider = MailProviderType.Gmail,
        EmailAddress = "conflict@empresa.test",
        DisplayName = "Conflict Gmail",
        Color = "#c6524b",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow
    });
    await database.SaveChangesAsync();
    httpHandler.EnqueueJson(HttpStatusCode.OK,
        "{\"access_token\":\"test-access-conflict\",\"refresh_token\":\"test-refresh-conflict\",\"expires_in\":3600}");
    httpHandler.EnqueueJson(HttpStatusCode.OK,
        "{\"id\":\"graph-conflict\",\"displayName\":\"Conflict\",\"mail\":\"conflict@empresa.test\",\"userPrincipalName\":\"conflict@empresa.test\"}");
    var conflictState = QueryHelpers.ParseQuery(new Uri(oauth.BeginAuthorization()).Query)["state"].ToString();
    var conflict = await CaptureThrowsAsync<InvalidOperationException>(
        () => oauth.CompleteAuthorizationAsync("conflict-code", conflictState, CancellationToken.None),
        "Una dirección ya conectada con Gmail debe fallar de forma segura.");
    Ensure(conflict.Message == "Esta dirección ya está conectada en NexoMail mediante otro proveedor.",
        "El conflicto entre proveedores debe devolver el mensaje seguro acordado.");
    Ensure(await database.MailAccounts.CountAsync(x => x.UserId == userId && x.EmailAddress == "conflict@empresa.test") == 1,
        "El conflicto entre proveedores no debe crear un duplicado.");

    database.ChangeTracker.Clear();
    var tokenCredential = await database.OAuthCredentials.SingleAsync(x => x.MailAccountId == reconnectedAccounts[0].Id);
    tokenCredential.EncryptedRefreshToken = tokenProtector.Protect("test-refresh-old");
    tokenCredential.UpdatedAt = DateTimeOffset.UtcNow;
    await database.SaveChangesAsync();

    var tokenRequestBaseline = httpHandler.Requests.Count;
    httpHandler.EnqueueJson(HttpStatusCode.OK,
        "{\"access_token\":\"test-access-new\",\"refresh_token\":\"test-refresh-rotated\",\"expires_in\":3600}");
    var tokenProvider = new MicrosoftGraphTokenProvider(httpClientFactory, microsoftOptions, database, tokenProtector);
    var accessToken1 = await tokenProvider.GetAccessTokenAsync(reconnectedAccounts[0].Id, CancellationToken.None);
    var accessToken2 = await tokenProvider.GetAccessTokenAsync(reconnectedAccounts[0].Id, CancellationToken.None);

    Ensure(accessToken1 == "test-access-new" && accessToken2 == "test-access-new",
        "El proveedor debe devolver el access token renovado y reutilizarlo desde caché.");
    Ensure(httpHandler.Requests.Count == tokenRequestBaseline + 1,
        "Dos solicitudes inmediatas deben producir un solo refresh HTTP.");
    var refreshRequest = httpHandler.Requests[^1];
    Ensure(refreshRequest.Method == HttpMethod.Post && refreshRequest.Uri == MicrosoftOAuthService.TokenEndpoint,
        "La renovación debe usar el endpoint organizations/token.");
    Ensure(refreshRequest.Body.Contains("grant_type=refresh_token", StringComparison.Ordinal),
        "La renovación debe usar grant_type=refresh_token.");
    Ensure(refreshRequest.Body.Contains("refresh_token=test-refresh-old", StringComparison.Ordinal),
        "La renovación debe enviar el refresh token descifrado sólo al endpoint de Microsoft.");
    Ensure(refreshRequest.Body.Contains("scope=openid+profile+email+offline_access+User.Read+Mail.ReadWrite", StringComparison.Ordinal),
        "La renovación debe mantener exactamente los scopes de Phase 1.");

    database.ChangeTracker.Clear();
    var rotatedCredential = await database.OAuthCredentials.SingleAsync(x => x.MailAccountId == reconnectedAccounts[0].Id);
    Ensure(rotatedCredential.EncryptedRefreshToken == "protected:test-refresh-rotated",
        "Si Microsoft rota el refresh token, debe persistirse protegido antes de devolver el access token.");
    Ensure(!rotatedCredential.EncryptedRefreshToken.Contains("test-access-new", StringComparison.Ordinal),
        "El access token renovado no debe persistirse.");

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
    _ = await CaptureThrowsAsync<TException>(action, message);
}

static async Task<TException> CaptureThrowsAsync<TException>(Func<Task> action, string message) where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException ex)
    {
        return ex;
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

sealed class FakeHttpClientFactory(QueueHttpMessageHandler handler) : IHttpClientFactory
{
    public int CreateClientCalls { get; private set; }

    public HttpClient CreateClient(string name)
    {
        CreateClientCalls++;
        return new HttpClient(handler, disposeHandler: false);
    }
}

sealed class QueueHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    public List<CapturedRequest> Requests { get; } = [];

    public void EnqueueJson(HttpStatusCode statusCode, string json) =>
        _responses.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new CapturedRequest(
            request.Method,
            request.RequestUri?.ToString() ?? string.Empty,
            body,
            request.Headers.Authorization?.ToString() ?? string.Empty));

        if (_responses.Count == 0)
            throw new InvalidOperationException("El test no configuró una respuesta HTTP para esta solicitud.");
        return _responses.Dequeue();
    }
}

sealed record CapturedRequest(HttpMethod Method, string Uri, string Body, string Authorization);

sealed class PassThroughTokenProtector : ITokenProtector
{
    public string Protect(string value) => "protected:" + value;
    public string Unprotect(string protectedValue) => protectedValue.StartsWith("protected:", StringComparison.Ordinal)
        ? protectedValue[10..]
        : protectedValue;
}
