# Microsoft Graph Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Microsoft 365 delegated OAuth and a read-only Microsoft Graph mailbox path that can connect an organizational account, list inbox messages, open a message, and mark it read without persisting message bodies or attachment contents.

**Architecture:** Follow the existing Gmail pattern with explicit `HttpClient` calls, the existing `IMailProvider`/`MailGateway` provider routing, protected refresh-token persistence, and `UserScopedMailProvider`. Microsoft-specific OAuth, token refresh, cursor handling, and Graph mapping live in a focused `NexoMail.Infrastructure.Microsoft` namespace. Unsupported Phase 1 Microsoft write operations remain visibly unavailable rather than silently degrading.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core SQLite, ASP.NET Core Data Protection, React/Vite/TypeScript, TanStack Query, pnpm, Microsoft Graph REST v1.0.

**Spec:** `docs/superpowers/specs/2026-09-11-microsoft-graph-integration-design.md`

## Global Constraints

- Microsoft authority: `https://login.microsoftonline.com/organizations`.
- Authorization endpoint: `https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize`.
- Token endpoint: `https://login.microsoftonline.com/organizations/oauth2/v2.0/token`.
- Graph base URL: `https://graph.microsoft.com/v1.0/`.
- Local callback: `http://localhost:5052/api/oauth/microsoft/callback`.
- Phase 1 OAuth scopes are exactly: `openid profile email offline_access User.Read Mail.ReadWrite`.
- `Mail.Send` may remain registered in Entra but is not requested or used in Phase 1.
- No application permissions, organization-wide mailbox access, personal Outlook/Hotmail support, or Exchange on-premises support.
- Do not persist Microsoft access tokens, message bodies, or attachment bytes.
- Persist the Microsoft refresh token only through the existing `ITokenProtector`.
- Do not commit a real Microsoft client secret, refresh token, access token, or mailbox data.
- Microsoft account operations supported in Phase 1: inbox list, single-message read, mark read/unread.
- Unsupported Microsoft operations must not report false success.
- Gmail behavior must remain unchanged.

---

### Task 1: Establish Microsoft test harness and shared account-limit policy

**Files:**
- Create: `src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj`
- Create: `src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs`
- Create: `src/backend/NexoMail.Infrastructure/MailAccountConnectionPolicy.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Google/GoogleOAuthService.cs`
- Modify: `NexoMail.sln`

**Interfaces:**
- Produces: `MailAccountConnectionPolicy.EnsureCanConnectAnotherAccountAsync(CancellationToken)`.
- `GoogleOAuthService.EnsureCanConnectAnotherAccountAsync` remains public and delegates to the shared policy so existing API routing does not change.
- The smoke-test project references `NexoMail.Infrastructure`, `NexoMail.Application`, and `NexoMail.Domain`.

- [ ] **Step 1: Create the Microsoft smoke-test console project and a failing account-limit check**

Create `NexoMail.MicrosoftGraphSmokeTests.csproj` with the same console-project shape as the existing smoke tests:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../NexoMail.Infrastructure/NexoMail.Infrastructure.csproj" />
    <ProjectReference Include="../NexoMail.Application/NexoMail.Application.csproj" />
    <ProjectReference Include="../NexoMail.Domain/NexoMail.Domain.csproj" />
  </ItemGroup>
</Project>
```

Add an initial `Program.cs` that builds an in-memory SQLite `NexoMailDbContext`, inserts a user and a commercial plan with `MaxAccounts = 1`, inserts one active account, and calls:

```csharp
var policy = new MailAccountConnectionPolicy(database, new TestUserContext(userId));
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
```

The test helper `TestUserContext` in this smoke project implements `IUserContext` and returns the supplied `UserId`.

- [ ] **Step 2: Run the smoke test and verify RED**

Run:

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
```

Expected: build fails because `MailAccountConnectionPolicy` does not exist.

- [ ] **Step 3: Implement the shared account-limit policy**

Create `MailAccountConnectionPolicy.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure;

public sealed class MailAccountConnectionPolicy(NexoMailDbContext database, IUserContext userContext)
{
    public async Task EnsureCanConnectAnotherAccountAsync(CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        var access = await CommercialAccessStore.GetAsync(database, userId, cancellationToken)
            ?? throw new InvalidOperationException("No fue posible determinar el plan de la cuenta.");
        var plan = access.EffectivePlan;
        if (!plan.MaxAccounts.HasValue) return;

        var connectedAccounts = await database.MailAccounts.AsNoTracking()
            .CountAsync(x => x.UserId == userId && x.IsActive, cancellationToken);
        if (connectedAccounts >= plan.MaxAccounts.Value)
            throw new InvalidOperationException($"Su plan efectivo {plan.Name} permite hasta {plan.MaxAccounts.Value} cuentas de correo. Cambie de plan o regularice su suscripción para conectar una cuenta adicional.");
    }
}
```

Change the Google service constructor to receive `MailAccountConnectionPolicy accountConnectionPolicy` and make its existing method delegate:

```csharp
public Task EnsureCanConnectAnotherAccountAsync(CancellationToken cancellationToken) =>
    accountConnectionPolicy.EnsureCanConnectAnotherAccountAsync(cancellationToken);
```

- [ ] **Step 4: Add the smoke-test project to the solution and run GREEN**

Run:

```powershell
dotnet sln NexoMail.sln add src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
```

Expected: `PASS` for the account-limit check.

- [ ] **Step 5: Commit**

```powershell
git add NexoMail.sln src/backend/NexoMail.MicrosoftGraphSmokeTests src/backend/NexoMail.Infrastructure/MailAccountConnectionPolicy.cs src/backend/NexoMail.Infrastructure/Google/GoogleOAuthService.cs
git commit -m "refactor: share mail account connection policy"
```

---

### Task 2: Implement Microsoft OAuth start, state validation, callback persistence, and reconnection

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftGraphOptions.cs`
- Create: `src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftOAuthService.cs`
- Modify: `src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs`

**Interfaces:**
- `MicrosoftGraphOptions.SectionName = "Microsoft"`.
- `MicrosoftOAuthService.EnsureCanConnectAnotherAccountAsync(CancellationToken)`.
- `MicrosoftOAuthService.BeginAuthorization()` returns the Microsoft authorization URL.
- `MicrosoftOAuthService.CompleteAuthorizationAsync(string code, string state, CancellationToken)` persists/reactivates the account and protected refresh token.
- `SuccessRedirect()` returns `...?connected=microsoft`; `FailureRedirect(string)` URL-encodes the error.

- [ ] **Step 1: Add failing OAuth URL/state assertions to the smoke test**

Instantiate `MicrosoftOAuthService` with:

```csharp
new MicrosoftGraphOptions
{
    ClientId = "client-test",
    ClientSecret = "secret-test",
    RedirectUri = "http://localhost:5052/api/oauth/microsoft/callback",
    FrontendUrl = "http://localhost:5173/settings/accounts"
}
```

Use an ephemeral Data Protection provider and assert the authorization URL contains:

```csharp
Ensure(uri.Host == "login.microsoftonline.com", "OAuth debe usar login.microsoftonline.com.");
Ensure(uri.AbsolutePath == "/organizations/oauth2/v2.0/authorize", "OAuth debe usar la autoridad organizations.");
Ensure(query["client_id"] == "client-test", "OAuth debe enviar ClientId.");
Ensure(query["redirect_uri"] == "http://localhost:5052/api/oauth/microsoft/callback", "OAuth debe enviar el callback registrado.");
Ensure(query["scope"] == "openid profile email offline_access User.Read Mail.ReadWrite", "OAuth debe pedir solo los scopes de Fase 1.");
Ensure(!query["scope"].Contains("Mail.Send", StringComparison.Ordinal), "Fase 1 no debe solicitar Mail.Send.");
Ensure(!string.IsNullOrWhiteSpace(query["state"]), "OAuth debe incluir state protegido.");
```

Add tests for tampered state and state created for a different `IUserContext`; both must throw `InvalidOperationException` before any token request.

- [ ] **Step 2: Run and verify RED**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
```

Expected: compile failure because Microsoft classes are absent.

- [ ] **Step 3: Implement `MicrosoftGraphOptions`**

```csharp
namespace NexoMail.Infrastructure.Microsoft;

public sealed class MicrosoftGraphOptions
{
    public const string SectionName = "Microsoft";
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = "http://localhost:5052/api/oauth/microsoft/callback";
    public string FrontendUrl { get; init; } = "http://localhost:5173/settings/accounts";
}
```

- [ ] **Step 4: Implement authorization URL and protected state**

Use these constants inside `MicrosoftOAuthService`:

```csharp
private const string AuthorizeEndpoint = "https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize";
private const string TokenEndpoint = "https://login.microsoftonline.com/organizations/oauth2/v2.0/token";
private const string GraphMeEndpoint = "https://graph.microsoft.com/v1.0/me?$select=id,displayName,mail,userPrincipalName";
private const string Phase1Scopes = "openid profile email offline_access User.Read Mail.ReadWrite";
```

Create the state protector with:

```csharp
private readonly IDataProtector _stateProtector =
    dataProtectionProvider.CreateProtector("NexoMail.MicrosoftOAuth.State.v1");
```

The state record is:

```csharp
private sealed record MicrosoftOAuthState(Guid UserId, DateTimeOffset IssuedAt, string Nonce);
```

Reject tampered state, wrong user, and age greater than ten minutes.

- [ ] **Step 5: Add failing callback/persistence assertions**

Extend the smoke test with a fake `IHttpClientFactory` whose unnamed client returns a token response for the token endpoint and whose `MicrosoftGraph` client returns `/me`:

```json
{"access_token":"access-1","refresh_token":"refresh-1","expires_in":3600}
```

```json
{"id":"graph-user-1","displayName":"Microsoft Test","mail":"persona@empresa.test","userPrincipalName":"persona@empresa.test"}
```

After `CompleteAuthorizationAsync`, assert:

```csharp
var account = await database.MailAccounts.SingleAsync(x => x.Provider == MailProviderType.MicrosoftGraph);
Ensure(account.EmailAddress == "persona@empresa.test", "Debe persistir el correo de /me.");
Ensure(account.DisplayName == "Microsoft 365", "Debe usar nombre visible Microsoft 365.");
var credential = await database.OAuthCredentials.SingleAsync(x => x.MailAccountId == account.Id);
Ensure(credential.EncryptedRefreshToken != "refresh-1", "El refresh token no puede guardarse en claro.");
Ensure(tokenProtector.Unprotect(credential.EncryptedRefreshToken) == "refresh-1", "El refresh token protegido debe ser recuperable.");
```

Repeat the callback with the same mailbox after setting `account.IsActive = false`; assert exactly one account exists and it becomes active again.

Also exercise a `/me` response with `"mail": null` and verify fallback to `userPrincipalName`.

- [ ] **Step 6: Implement callback exchange and persistence**

Post this form to the token endpoint:

```csharp
new FormUrlEncodedContent(new Dictionary<string, string>
{
    ["code"] = code,
    ["client_id"] = _options.ClientId,
    ["client_secret"] = _options.ClientSecret,
    ["redirect_uri"] = _options.RedirectUri,
    ["grant_type"] = "authorization_code",
    ["scope"] = Phase1Scopes
})
```

Call `/me` using the returned access token. Resolve email with `mail` first and `userPrincipalName` second. Create/reactivate:

```csharp
new MailAccountEntity
{
    Id = Guid.NewGuid(),
    UserId = userContext.UserId,
    Provider = MailProviderType.MicrosoftGraph,
    EmailAddress = email,
    DisplayName = "Microsoft 365",
    Color = "#0078d4",
    CreatedAt = DateTimeOffset.UtcNow,
    IsActive = true
};
```

Protect the refresh token through `ITokenProtector` and store it in the existing `OAuthCredentialEntity`. Do not persist the access token.

- [ ] **Step 7: Run GREEN and commit**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
git add src/backend/NexoMail.Infrastructure/Microsoft src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs
git commit -m "feat: add Microsoft OAuth connection flow"
```

---

### Task 3: Implement Microsoft access-token refresh with rotation

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftGraphTokenProvider.cs`
- Modify: `src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs`

**Interfaces:**
- Produces: `Task<string> GetAccessTokenAsync(Guid accountId, CancellationToken cancellationToken)`.
- Depends on `NexoMailDbContext`, `ITokenProtector`, `IOptions<MicrosoftGraphOptions>`, and `IHttpClientFactory`.
- Durable state remains the protected refresh token; short-lived access tokens are process-memory cache only.

- [ ] **Step 1: Add failing token-refresh tests**

Seed a Microsoft account and credential where the protected value unwraps to `refresh-old`. Configure the fake token endpoint to return:

```json
{"access_token":"access-new","refresh_token":"refresh-rotated","expires_in":3600}
```

Assert:

```csharp
var accessToken = await tokenProvider.GetAccessTokenAsync(account.Id, CancellationToken.None);
Ensure(accessToken == "access-new", "Debe devolver el access token de Microsoft.");
var updated = await database.OAuthCredentials.SingleAsync(x => x.MailAccountId == account.Id);
Ensure(tokenProtector.Unprotect(updated.EncryptedRefreshToken) == "refresh-rotated", "Debe persistir refresh-token rotation.");
Ensure(updated.EncryptedRefreshToken != "refresh-rotated", "El refresh token rotado debe permanecer protegido.");
```

Call the method a second time and assert the fake token endpoint request count remains `1`, proving the access token is cached until near expiration.

- [ ] **Step 2: Run RED**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
```

Expected: compile failure because `MicrosoftGraphTokenProvider` does not exist.

- [ ] **Step 3: Implement the token provider**

Use:

```csharp
private static readonly ConcurrentDictionary<Guid, CachedAccessToken> AccessTokens = new();
private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> TokenGates = new();
```

Refresh with:

```csharp
new FormUrlEncodedContent(new Dictionary<string, string>
{
    ["client_id"] = options.Value.ClientId,
    ["client_secret"] = options.Value.ClientSecret,
    ["refresh_token"] = refreshToken,
    ["grant_type"] = "refresh_token",
    ["scope"] = "openid profile email offline_access User.Read Mail.ReadWrite"
})
```

Cache expiry as `now.AddSeconds(Math.Max(60, expiresIn - 120))`. If the token response contains a non-empty replacement refresh token different from the existing one, protect it, set `UpdatedAt = DateTimeOffset.UtcNow`, and `SaveChangesAsync` before returning the access token.

- [ ] **Step 4: Run GREEN and commit**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
git add src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftGraphTokenProvider.cs src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs
git commit -m "feat: add Microsoft token refresh and rotation"
```

---

### Task 4: Implement Graph inbox listing, safe pagination, message detail, and read state

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftGraphCursor.cs`
- Create: `src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftGraphMailProvider.cs`
- Modify: `src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs`

**Interfaces:**
- `MicrosoftGraphMailProvider : IMailProvider` with `ProviderType => MailProviderType.MicrosoftGraph`.
- Supports `GetMessagesAsync`, `GetMessageAsync`, `MarkReadAsync`.
- `GetThreadAsync` returns `[]`.
- `GetFoldersAsync` returns only `new MailFolder("inbox", "Bandeja de entrada", 0)`.
- Other `IMailProvider` write/content methods throw a Microsoft Phase 1 `NotSupportedException`.

- [ ] **Step 1: Add failing list/message/read tests with fake Graph JSON**

List response fixture:

```json
{
  "@odata.nextLink":"https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?$skiptoken=abc123",
  "value":[{
    "id":"m1",
    "subject":"Asunto Graph",
    "bodyPreview":"Vista previa",
    "receivedDateTime":"2026-09-11T14:30:00Z",
    "isRead":false,
    "hasAttachments":true,
    "from":{"emailAddress":{"name":"Remitente","address":"sender@example.com"}}
  }]
}
```

Assert `MailSummary` maps id, account id, sender, subject, preview, timestamp, unread state, attachment flag, and `folderId == "inbox"`. Assert `NextCursor` is non-empty.

Decode the cursor through the provider on a second request and ensure the fake HTTP handler receives only a `https://graph.microsoft.com/v1.0/...` URL. Add a malformed cursor test that fails safely rather than requesting another host.

Single-message fixture:

```json
{
  "id":"m1",
  "subject":"Asunto Graph",
  "bodyPreview":"Vista previa",
  "body":{"contentType":"html","content":"<p>Contenido Graph</p>"},
  "receivedDateTime":"2026-09-11T14:30:00Z",
  "isRead":false,
  "hasAttachments":true,
  "from":{"emailAddress":{"name":"Remitente","address":"sender@example.com"}},
  "toRecipients":[{"emailAddress":{"name":"Destino","address":"destino@example.com"}}],
  "ccRecipients":[{"emailAddress":{"name":"Copia","address":"copia@example.com"}}]
}
```

Assert mapping into `MailMessage`, including `HtmlBody`, recipients, CC, and empty attachment-content collection for Phase 1.

For `MarkReadAsync`, assert a PATCH is sent to `/me/messages/m1` with JSON `{"isRead":true}`.

- [ ] **Step 2: Run RED**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
```

Expected: compile failure because provider/cursor classes are absent.

- [ ] **Step 3: Implement validated opaque cursor handling**

`MicrosoftGraphCursor.Encode(string nextLink)` Base64URL-encodes the full Graph nextLink. `Decode(string cursor)` must:

```csharp
if (!Uri.TryCreate(decoded, UriKind.Absolute, out var uri)
    || uri.Scheme != Uri.UriSchemeHttps
    || !string.Equals(uri.Host, "graph.microsoft.com", StringComparison.OrdinalIgnoreCase)
    || !uri.AbsolutePath.StartsWith("/v1.0/me/", StringComparison.Ordinal))
    throw new InvalidOperationException("El cursor de Microsoft Graph no es válido.");
```

This prevents a modified frontend cursor from turning the backend into an arbitrary HTTP requester.

- [ ] **Step 4: Implement `GetMessagesAsync`**

For a new page request call:

```text
me/mailFolders/inbox/messages?$top={1..50}&$orderby=receivedDateTime desc&$select=id,from,subject,bodyPreview,receivedDateTime,isRead,hasAttachments
```

If `query.FolderId != "inbox"` or `query.Search` is non-empty, return `new PagedResult<MailSummary>([])` in Phase 1. If a cursor is supplied, use only the URI returned from `MicrosoftGraphCursor.Decode`.

Map null/missing sender defensively to empty name/address and missing subject to `(sin asunto)`.

- [ ] **Step 5: Implement `GetMessageAsync`, `MarkReadAsync`, and unsupported operations**

Single message request:

```text
me/messages/{messageId}?$select=id,from,toRecipients,ccRecipients,subject,body,bodyPreview,receivedDateTime,isRead,hasAttachments
```

Use `HttpMethod.Patch` with `JsonContent.Create(new { isRead = read })` for read state.

Unsupported methods return tasks that throw a consistent message, for example:

```csharp
private static NotSupportedException Unsupported() =>
    new("Esta operación aún no está disponible para Microsoft 365 en la Fase 1 de NexoMail.");
```

`GetThreadAsync` returns `Task.FromResult<IReadOnlyCollection<MailThreadMessage>>([])` so automatic thread loading does not break message opening.

- [ ] **Step 6: Run GREEN and commit**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
git add src/backend/NexoMail.Infrastructure/Microsoft src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs
git commit -m "feat: add Microsoft Graph read provider"
```

---

### Task 5: Wire Microsoft into ASP.NET Core and account settings UI

**Files:**
- Modify: `src/backend/NexoMail.Api/Program.cs`
- Modify: `src/backend/NexoMail.Api/appsettings.Development.example.json`
- Modify: `src/frontend/src/pages/AccountsPage.tsx`
- Create: `src/frontend/scripts/microsoft-graph-smoke.mjs`
- Modify: `.github/workflows/frontend-build.yml`

**Interfaces:**
- Adds `/api/oauth/microsoft/start` and `/api/oauth/microsoft/callback`.
- Registers `MicrosoftGraphMailProvider` through `UserScopedMailProvider` only when `DemoMode` is false.
- Frontend exposes `Agregar Microsoft 365` and success state `connected=microsoft`.

- [ ] **Step 1: Write the failing frontend smoke script first**

`microsoft-graph-smoke.mjs` reads `AccountsPage.tsx`, `InboxPage.tsx`, and `MessagePage.tsx` and asserts at minimum:

```js
requireCondition(accountsPage.includes('Agregar Microsoft 365'), 'Debe existir el control para conectar Microsoft 365.')
requireCondition(accountsPage.includes("/api/oauth/microsoft/start"), 'Microsoft 365 debe iniciar OAuth en el backend.')
requireCondition(accountsPage.includes("connected') === 'microsoft'"), 'Debe existir confirmación de conexión Microsoft.')
requireCondition(inboxPage.includes("MicrosoftGraph"), 'La bandeja debe reconocer capacidades Microsoft Phase 1.')
requireCondition(messagePage.includes("MicrosoftGraph"), 'La vista de mensaje debe reconocer capacidades Microsoft Phase 1.')
```

- [ ] **Step 2: Run RED**

```powershell
cd src/frontend
node scripts/microsoft-graph-smoke.mjs
```

Expected: fails because the Microsoft connect button/gating are not present.

- [ ] **Step 3: Register Microsoft backend services**

Add:

```csharp
using NexoMail.Infrastructure.Microsoft;
```

and service registration:

```csharp
builder.Services.AddHttpClient("MicrosoftGraph", client =>
    client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/"));
builder.Services.Configure<MicrosoftGraphOptions>(builder.Configuration.GetSection(MicrosoftGraphOptions.SectionName));
builder.Services.AddScoped<MailAccountConnectionPolicy>();
builder.Services.AddScoped<MicrosoftOAuthService>();
builder.Services.AddScoped<MicrosoftGraphTokenProvider>();
```

In non-demo mode register both Gmail and Microsoft providers, each wrapped in `UserScopedMailProvider`:

```csharp
builder.Services.AddScoped<MicrosoftGraphMailProvider>();
builder.Services.AddScoped<IMailProvider>(services => new UserScopedMailProvider(
    services.GetRequiredService<MicrosoftGraphMailProvider>(),
    services.GetRequiredService<NexoMailDbContext>(),
    services.GetRequiredService<IUserContext>()));
```

- [ ] **Step 4: Add Microsoft OAuth API endpoints**

Add `/microsoft/start` parallel to Google. Callback behavior:

```csharp
if (!string.IsNullOrWhiteSpace(error))
{
    var message = string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase)
        ? "Microsoft canceló o denegó la autorización. Si tu organización exige aprobación administrativa, solicita autorización al administrador de Microsoft 365."
        : "Microsoft no pudo autorizar la conexión.";
    return Results.Redirect(service.FailureRedirect(message));
}
```

Incomplete callback returns a Microsoft-specific message. Catch `InvalidOperationException` and `HttpRequestException`; do not return raw Microsoft response bodies.

- [ ] **Step 5: Correct the development configuration template**

Replace the current Microsoft template with:

```json
"Microsoft": {
  "ClientId": "",
  "ClientSecret": "",
  "RedirectUri": "http://localhost:5052/api/oauth/microsoft/callback",
  "FrontendUrl": "http://localhost:5173/settings/accounts"
}
```

Remove the unused `TenantId` entry because the implementation uses the `organizations` authority.

- [ ] **Step 6: Add the Microsoft account button and success notice**

In `AccountsPage.tsx`, render separate buttons that both obey `accountLimitReached`:

```tsx
<button className="primary-button" disabled={accountLimitReached} onClick={() => window.location.assign('/api/oauth/google/start')}>
  <MailPlus size={16} /> Agregar Gmail
</button>
<button className="secondary-button" disabled={accountLimitReached} onClick={() => window.location.assign('/api/oauth/microsoft/start')}>
  <MailPlus size={16} /> Agregar Microsoft 365
</button>
```

Add:

```tsx
{params.get('connected') === 'microsoft' && <div className="success-notice">La cuenta Microsoft 365 fue conectada correctamente.</div>}
```

Fix provider wording in edit/remove UI so Microsoft accounts do not say Gmail in confirmation text.

- [ ] **Step 7: Run backend build; frontend smoke remains RED only for action gating**

```powershell
dotnet build NexoMail.sln --configuration Release
cd src/frontend
node scripts/microsoft-graph-smoke.mjs
```

Expected: .NET build passes; frontend smoke still fails until Task 6 capability gating is added.

- [ ] **Step 8: Commit**

```powershell
git add src/backend/NexoMail.Api/Program.cs src/backend/NexoMail.Api/appsettings.Development.example.json src/frontend/src/pages/AccountsPage.tsx src/frontend/scripts/microsoft-graph-smoke.mjs
git commit -m "feat: wire Microsoft 365 account connection"
```

---

### Task 6: Gate unsupported Microsoft actions and verify Gmail-only isolation

**Files:**
- Modify: `src/frontend/src/pages/InboxPage.tsx`
- Modify: `src/frontend/src/pages/MessagePage.tsx`
- Modify: `src/frontend/scripts/microsoft-graph-smoke.mjs`
- Inspect and change only if missing provider filters: `src/backend/NexoMail.Infrastructure/Google/GmailControlCenterService.cs`
- Inspect and change only if missing provider filters: `src/backend/NexoMail.Infrastructure/Google/GmailControlCenterActivityService.cs`
- Inspect and change only if missing provider filters: `src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexService.cs`
- Modify: `src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs`
- Modify: `.github/workflows/frontend-build.yml`

**Interfaces:**
- `MarkReadAsync` remains enabled for Microsoft.
- Microsoft Phase 1 disables/hides move/archive/spam/trash/reply/reply-all/forward/draft/contact/rule actions that would invoke unsupported provider methods.
- Gmail-only services must query `MailProviderType.Gmail` explicitly.

- [ ] **Step 1: Add a failing backend isolation check**

Seed one Gmail and one Microsoft account for the same NexoMail user. Verify the Gmail control-center and metadata service account-selection queries do not include the Microsoft account. Where a service already filters `Provider == MailProviderType.Gmail`, record the check in the smoke harness rather than changing production code.

The test invariant is:

```csharp
Ensure(gmailOnlyAccountIds.All(id => id != microsoftAccount.Id), "Servicios Gmail no deben procesar cuentas MicrosoftGraph.");
```

Also call an unsupported Microsoft provider operation such as `MoveToTrashAsync` and assert it throws rather than succeeding.

- [ ] **Step 2: Implement account-provider capability lookup in `InboxPage.tsx`**

The page already loads `accounts`. Add:

```tsx
const accountProviderById = useMemo(() => new Map(accounts.map(account => [account.id, account.provider])), [accounts])
const selectedIncludesMicrosoft = selectedItems.some(item => accountProviderById.get(item.accountId) === 'MicrosoftGraph')
const activeMicrosoftAccount = selectedAccount?.provider === 'MicrosoftGraph'
```

For a Microsoft-only account view, keep refresh, open, selection, and mark-read available. Hide/disable archive, spam, trash, ignore-sender, empty-trash, and draft operations. For unified selection, disable an unsupported bulk action whenever `selectedIncludesMicrosoft` is true rather than executing it for a mixed provider set.

Do not alter Gmail behavior.

- [ ] **Step 3: Implement provider-aware capability lookup in `MessagePage.tsx`**

Load accounts using the existing query key/API:

```tsx
const { data: accounts = [] } = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts, staleTime: 10 * 60_000 })
const currentAccount = accounts.find(account => account.id === accountId)
const isMicrosoftPhase1 = currentAccount?.provider === 'MicrosoftGraph'
```

For `isMicrosoftPhase1`, retain message display and automatic `mailApi.read(...)`; do not render/enable reply, reply-all, forward, archive, spam, trash, ignore-sender, attachment download, or tracking actions that depend on Gmail-only Control Center state. The thread query may remain because Microsoft returns `[]` safely.

- [ ] **Step 4: Keep Gmail-specific backend filters explicit**

Confirm these account queries contain:

```csharp
.Where(x => x.UserId == userId && x.IsActive && x.Provider == MailProviderType.Gmail)
```

If any listed Gmail service lacks this predicate, add it and extend the smoke harness to cover that service. Do not refactor those services to Microsoft in Phase 1.

- [ ] **Step 5: Complete frontend smoke and CI hook**

Make `microsoft-graph-smoke.mjs` assert:

```js
requireCondition(inboxPage.includes("provider === 'MicrosoftGraph'"), 'Inbox debe limitar acciones no soportadas para Microsoft.')
requireCondition(messagePage.includes("provider === 'MicrosoftGraph'"), 'MessagePage debe limitar acciones no soportadas para Microsoft.')
```

Add to `.github/workflows/frontend-build.yml` before the build:

```yaml
      - name: Verify Microsoft Graph Phase 1 UI
        run: node scripts/microsoft-graph-smoke.mjs
```

- [ ] **Step 6: Run GREEN and commit**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
cd src/frontend
node scripts/microsoft-graph-smoke.mjs
pnpm build
cd ../..
git add src/frontend/src/pages/InboxPage.tsx src/frontend/src/pages/MessagePage.tsx src/frontend/scripts/microsoft-graph-smoke.mjs .github/workflows/frontend-build.yml src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs src/backend/NexoMail.Infrastructure/Google
git commit -m "feat: gate Microsoft Phase 1 capabilities"
```

---

### Task 7: Add Microsoft CI, run complete regression suite, and prepare real-account test

**Files:**
- Create: `.github/workflows/microsoft-graph-smoke.yml`
- Modify only if documentation is missing: `README.md`

**Interfaces:**
- CI uses fake HTTP only and requires no Microsoft secrets.
- Local real-account configuration uses ignored `src/backend/NexoMail.Api/appsettings.Development.json` or .NET user-secrets; never the committed example file.

- [ ] **Step 1: Add dedicated Microsoft Graph workflow**

Create:

```yaml
name: Microsoft Graph smoke test

on:
  push:
    branches:
      - main
      - 'feature/**'
  pull_request:
    branches:
      - main

jobs:
  microsoft-graph:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore Microsoft Graph smoke test
        run: dotnet restore src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj

      - name: Build Microsoft Graph smoke test
        run: dotnet build src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj --no-restore --configuration Release

      - name: Run Microsoft Graph smoke test
        run: dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj --no-build --configuration Release

      - name: Build complete solution
        run: dotnet build NexoMail.sln --configuration Release

      - name: Verify no Microsoft secrets were committed
        shell: bash
        run: |
          if grep -R -n -E 'refresh-[A-Za-z0-9]|access-[A-Za-z0-9]|ClientSecret"[[:space:]]*:[[:space:]]*"[^"[:space:]]+' src/backend/NexoMail.Api src/backend/NexoMail.Infrastructure/Microsoft --exclude='appsettings.Development.example.json'; then
            echo "Potential Microsoft credential found in committed production/config files."
            exit 1
          fi
          echo "Microsoft credential scan passed."
```

- [ ] **Step 2: Run the complete local regression suite**

From repo root:

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
dotnet run --project src/backend/NexoMail.ControlCenterSmokeTests/NexoMail.ControlCenterSmokeTests.csproj
dotnet run --project src/backend/NexoMail.SmokeTests/NexoMail.SmokeTests.csproj
dotnet build NexoMail.sln --configuration Release
cd src/frontend
node scripts/sidebar-tooltip-smoke.mjs
node scripts/commercial-entitlement-smoke.mjs
node scripts/admin-users-smoke.mjs
node scripts/ai-usage-admin-smoke.mjs
node scripts/legal-consent-smoke.mjs
node scripts/microsoft-graph-smoke.mjs
pnpm build
```

Expected: all smoke tests print PASS and both .NET/frontend builds exit 0.

- [ ] **Step 3: Configure local secrets without committing them**

Use the ignored local development file `src/backend/NexoMail.Api/appsettings.Development.json` with:

```json
{
  "Microsoft": {
    "ClientId": "9ee2ba3a-4565-4848-b3d8-a9d414d36963",
    "ClientSecret": "LOCAL_SECRET_VALUE",
    "RedirectUri": "http://localhost:5052/api/oauth/microsoft/callback",
    "FrontendUrl": "http://localhost:5173/settings/accounts"
  },
  "MailProviders": {
    "DemoMode": false
  }
}
```

`LOCAL_SECRET_VALUE` is entered by the developer locally and never pasted into Git, CI, chat logs, screenshots, or committed files.

Before testing, run:

```powershell
git status --short
```

Expected: `appsettings.Development.json` does not appear because `.gitignore` excludes it.

- [ ] **Step 4: Run the manual Microsoft 365 acceptance flow**

Start backend and frontend. In NexoMail:

1. Sign in to NexoMail.
2. Open `Cuentas de correo`.
3. Click `Agregar Microsoft 365`.
4. Authenticate only on Microsoft's page; approve Microsoft Authenticator if requested.
5. If Microsoft reports administrator approval is required, capture only the non-sensitive error message and stop; do not bypass it.
6. If consent succeeds, verify the account appears as `Microsoft 365`.
7. Open that account's inbox; verify sender, subject, preview, date, unread state, and pagination.
8. Open one unread message; verify body/recipients and then confirm it becomes read in Microsoft 365.
9. Confirm unsupported write actions are absent/disabled for that Microsoft account.
10. Confirm an existing Gmail account still lists and opens messages normally.

- [ ] **Step 5: Verify persistence/privacy after the real test**

Inspect SQLite tables `MailAccounts` and `OAuthCredentials`. Confirm:

- the Microsoft account has `Provider = MicrosoftGraph`;
- the OAuth credential value is protected/ciphertext-like and does not equal a raw refresh token;
- no new table/column stores Microsoft access tokens;
- no full Microsoft message body or attachment bytes were added to persistence by the Graph read path.

Do not print/decode the actual stored refresh token during this verification.

- [ ] **Step 6: Commit CI/documentation changes**

```powershell
git add .github/workflows/microsoft-graph-smoke.yml README.md
git commit -m "ci: verify Microsoft Graph phase 1"
```

- [ ] **Step 7: Final branch verification before declaring Phase 1 complete**

```powershell
git status --short
git log -5 --oneline
dotnet build NexoMail.sln --configuration Release
cd src/frontend
pnpm build
```

Expected: clean working tree, successful backend build, successful frontend build. Functional completion additionally requires at least one organizational Microsoft 365 account to finish OAuth and successfully exercise the Graph read path; an admin-consent block on a specific external tenant is diagnostic evidence, not proof that Graph read integration itself works.
