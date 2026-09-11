# Microsoft Graph Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Microsoft 365 delegated OAuth and a deliberately limited Microsoft Graph mailbox path that connects an organizational account, lists the inbox, opens a message, and marks it read/unread without persisting message bodies or attachment contents.

**Architecture:** Follow NexoMail's existing Gmail pattern: explicit `HttpClient` REST calls, existing `IMailProvider`/`MailGateway` routing, `UserScopedMailProvider`, ASP.NET Core Data Protection, and the existing OAuth credential table. Microsoft-specific OAuth, token refresh, safe cursor handling, and Graph mapping live under `NexoMail.Infrastructure.Microsoft`. Unsupported Microsoft write features stay unavailable in Phase 1 rather than failing after the UI advertises them.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core SQLite, ASP.NET Core Data Protection, React/Vite/TypeScript, TanStack Query, pnpm, Microsoft Graph REST v1.0.

**Spec:** `docs/superpowers/specs/2026-09-11-microsoft-graph-integration-design.md`

## Global constraints

- Authority: `https://login.microsoftonline.com/organizations`.
- Authorize endpoint: `https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize`.
- Token endpoint: `https://login.microsoftonline.com/organizations/oauth2/v2.0/token`.
- Graph base: `https://graph.microsoft.com/v1.0/`.
- Local callback: `http://localhost:5052/api/oauth/microsoft/callback`.
- Phase 1 scopes exactly: `openid profile email offline_access User.Read Mail.ReadWrite`.
- `Mail.Send` may remain registered in Entra but is not requested or used in Phase 1.
- No application permissions, tenant-wide mailbox access, personal Outlook/Hotmail, or Exchange on-premises.
- Never persist Microsoft access tokens, full message bodies, or attachment bytes.
- Persist only the protected refresh token through the existing `ITokenProtector` (`NexoMail.Infrastructure.Google` namespace is retained for Phase 1 to avoid unrelated refactoring).
- Never commit a real client secret, refresh token, access token, or real mailbox fixture.
- Supported Microsoft operations: inbox list, single-message read, mark read/unread.
- Unsupported Microsoft operations must never report false success.
- Gmail behavior must remain unchanged.

---

### Task 1: Create the Microsoft smoke-test harness and share account-limit enforcement

**Files:**
- Create: `src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj`
- Create: `src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs`
- Create: `src/backend/NexoMail.Infrastructure/MailAccountConnectionPolicy.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Google/GoogleOAuthService.cs`
- Modify: `NexoMail.sln`

- [ ] **Step 1: Write the first failing test**

Create the console smoke-test project with project references to Infrastructure, Application, and Domain plus the ASP.NET Core framework reference:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="../NexoMail.Infrastructure/NexoMail.Infrastructure.csproj" />
    <ProjectReference Include="../NexoMail.Application/NexoMail.Application.csproj" />
    <ProjectReference Include="../NexoMail.Domain/NexoMail.Domain.csproj" />
  </ItemGroup>
</Project>
```

In `Program.cs`, create an in-memory SQLite database, seed a user, an effective plan with `MaxAccounts = 1`, and one active account. Add a `TestUserContext` implementing the complete current interface:

```csharp
sealed class TestUserContext(Guid userId) : IUserContext
{
    public bool IsAuthenticated => true;
    public Guid UserId => userId;
    public string Email => "test@nexomail.local";
    public string DisplayName => "NexoMail Test";
}
```

Assert a second connection is blocked:

```csharp
var policy = new MailAccountConnectionPolicy(database, new TestUserContext(userId));
var blocked = false;
try { await policy.EnsureCanConnectAnotherAccountAsync(CancellationToken.None); }
catch (InvalidOperationException) { blocked = true; }
Ensure(blocked, "El límite comercial debe bloquear una segunda cuenta.");
```

- [ ] **Step 2: Run RED**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
```

Expected: compile failure because `MailAccountConnectionPolicy` does not exist.

- [ ] **Step 3: Implement the shared policy**

Move only the existing effective-plan/max-account logic from `GoogleOAuthService.EnsureCanConnectAnotherAccountAsync` into:

```csharp
public sealed class MailAccountConnectionPolicy(NexoMailDbContext database, IUserContext userContext)
{
    public async Task EnsureCanConnectAnotherAccountAsync(CancellationToken cancellationToken)
    {
        var access = await CommercialAccessStore.GetAsync(database, userContext.UserId, cancellationToken)
            ?? throw new InvalidOperationException("No fue posible determinar el plan de la cuenta.");
        var plan = access.EffectivePlan;
        if (!plan.MaxAccounts.HasValue) return;

        var connected = await database.MailAccounts.AsNoTracking()
            .CountAsync(x => x.UserId == userContext.UserId && x.IsActive, cancellationToken);
        if (connected >= plan.MaxAccounts.Value)
            throw new InvalidOperationException($"Su plan efectivo {plan.Name} permite hasta {plan.MaxAccounts.Value} cuentas de correo. Cambie de plan o regularice su suscripción para conectar una cuenta adicional.");
    }
}
```

Inject the policy into `GoogleOAuthService`; keep its public method as a delegating compatibility method.

- [ ] **Step 4: Add project to solution and run GREEN**

```powershell
dotnet sln NexoMail.sln add src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add NexoMail.sln src/backend/NexoMail.MicrosoftGraphSmokeTests src/backend/NexoMail.Infrastructure/MailAccountConnectionPolicy.cs src/backend/NexoMail.Infrastructure/Google/GoogleOAuthService.cs
git commit -m "refactor: share mail account connection policy"
```

---

### Task 2: Implement Microsoft OAuth URL, protected state, callback persistence, and reconnection

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftGraphOptions.cs`
- Create: `src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftOAuthService.cs`
- Modify: `src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs`

- [ ] **Step 1: Add failing authorization/state tests**

Test an options instance containing test-only values and assert `BeginAuthorization()` produces:

```text
host = login.microsoftonline.com
path = /organizations/oauth2/v2.0/authorize
client_id = configured client id
redirect_uri = http://localhost:5052/api/oauth/microsoft/callback
scope = openid profile email offline_access User.Read Mail.ReadWrite
```

Assert `Mail.Send` is absent and `state` is present.

Add three state-failure cases that must fail before any HTTP exchange:

1. altered/tampered state;
2. state generated for a different NexoMail user;
3. expired state older than ten minutes.

For the expiration test, create the same Data Protection purpose `NexoMail.MicrosoftOAuth.State.v1`, protect a JSON payload containing the current user ID, `IssuedAt = DateTimeOffset.UtcNow.AddMinutes(-11)`, and a nonce, then pass it to `CompleteAuthorizationAsync` and assert `InvalidOperationException`.

- [ ] **Step 2: Run RED**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
```

Expected: Microsoft OAuth classes missing.

- [ ] **Step 3: Add options and authorization/state implementation**

`MicrosoftGraphOptions`:

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

Use constants:

```csharp
private const string AuthorizeEndpoint = "https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize";
private const string TokenEndpoint = "https://login.microsoftonline.com/organizations/oauth2/v2.0/token";
private const string GraphMeEndpoint = "https://graph.microsoft.com/v1.0/me?$select=id,displayName,mail,userPrincipalName";
internal const string Phase1Scopes = "openid profile email offline_access User.Read Mail.ReadWrite";
```

Use the Data Protection purpose `NexoMail.MicrosoftOAuth.State.v1`, include `UserId`, `IssuedAt`, and random nonce, bind state to the logged-in NexoMail user, and expire at ten minutes.

Import/reuse `NexoMail.Infrastructure.Google.ITokenProtector`; do not relocate it in this phase.

- [ ] **Step 4: Add failing callback persistence tests**

With a fake `IHttpClientFactory`, return these test-only payloads:

```json
{"access_token":"test-access-token","refresh_token":"test-refresh-token","expires_in":3600}
```

```json
{"id":"graph-test-user","displayName":"Microsoft Test","mail":"persona@empresa.test","userPrincipalName":"persona@empresa.test"}
```

Assert:

- one `MailAccountEntity` is created with `Provider = MicrosoftGraph`;
- email is `persona@empresa.test`;
- display name is `Microsoft 365`;
- refresh token is not stored in clear text and decrypts through the test protector;
- no access token is persisted.

Then set the account inactive, repeat connection for the same address, and assert the existing account is reactivated rather than duplicated.

Also test `/me` with `mail = null` and a valid `userPrincipalName` fallback.

Because the DB unique index is `(UserId, EmailAddress)` regardless of provider, seed the same address under a different provider and assert the Microsoft callback fails cleanly with a user-safe `InvalidOperationException` rather than reaching a SQLite unique-index exception.

- [ ] **Step 5: Implement token exchange and account persistence**

Post authorization-code exchange with `code`, client ID/secret, exact redirect URI, `grant_type=authorization_code`, and exact Phase 1 scopes. Use the returned access token only transiently to call `/me`.

When resolving the account:

```csharp
var existing = await database.MailAccounts.SingleOrDefaultAsync(
    x => x.UserId == userContext.UserId && x.EmailAddress == email,
    cancellationToken);
```

If `existing` has another provider, throw:

```text
Esta dirección ya está conectada en NexoMail mediante otro proveedor.
```

If it is MicrosoftGraph, reactivate it. Otherwise create a MicrosoftGraph account. Protect the refresh token before persistence.

- [ ] **Step 6: Run GREEN and commit**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
git add src/backend/NexoMail.Infrastructure/Microsoft src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs
git commit -m "feat: add Microsoft OAuth connection flow"
```

---

### Task 3: Implement access-token refresh, memory cache, and refresh-token rotation

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftGraphTokenProvider.cs`
- Modify: `src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs`

- [ ] **Step 1: Add failing refresh tests**

Seed a Microsoft credential whose protected refresh token decrypts to `test-refresh-old`. Fake the token endpoint response:

```json
{"access_token":"test-access-new","refresh_token":"test-refresh-rotated","expires_in":3600}
```

Assert:

- `GetAccessTokenAsync` returns `test-access-new`;
- rotated refresh token is stored protected;
- second call uses the access-token memory cache and does not call the token endpoint again;
- access token does not appear in any persisted entity.

- [ ] **Step 2: Run RED**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
```

- [ ] **Step 3: Implement token provider**

Use per-account `ConcurrentDictionary<Guid, CachedAccessToken>` and `SemaphoreSlim` gates. Load only the current credential snapshot from SQLite, unprotect refresh token, request a fresh access token with the exact Phase 1 scopes, and cache until approximately two minutes before provider expiry.

If Microsoft returns a replacement refresh token, protect and save it before returning. Update `UpdatedAt`; cache invalidation must compare credential timestamp so reconnecting an account invalidates an older access token.

- [ ] **Step 4: Run GREEN and commit**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
git add src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftGraphTokenProvider.cs src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs
git commit -m "feat: add Microsoft token refresh and rotation"
```

---

### Task 4: Implement safe Graph inbox pagination, message detail, read state, and Graph error normalization

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftGraphCursor.cs`
- Create: `src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftGraphMailProvider.cs`
- Modify: `src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs`

- [ ] **Step 1: Add failing inbox mapping and cursor tests**

Fake this Graph list response:

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

Assert `MailSummary` maps id, account, sender, subject, preview, timestamp, unread state, attachment flag, and `folderId = inbox`. Assert a continuation cursor exists.

Round-trip that cursor into a second request and assert the HTTP request can only target `https://graph.microsoft.com/v1.0/me/...`.

Add malicious/tampered cursors decoding to `https://example.com/...`, `http://graph.microsoft.com/...`, or a Graph path outside `/v1.0/me/`; each must fail without making an HTTP call.

- [ ] **Step 2: Add failing message-detail and read-state tests**

Use:

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

Assert `MailMessage` maps sender, To, Cc, subject, transient HTML body, preview, timestamp, read state, and `folderId = inbox`. Attachment bytes are not requested or persisted in Phase 1.

Assert `MarkReadAsync` PATCHes `/me/messages/m1` with `{ "isRead": true }` (and supports false for unread).

- [ ] **Step 3: Add failing Graph error tests**

For list/detail/read requests assert safe behavior for:

- 401 and 403 -> `InvalidOperationException` with a generic Microsoft permission/reconnect message;
- 429 -> `InvalidOperationException` with a generic temporary throttling message;
- 5xx -> `HttpRequestException` with a generic temporary Microsoft service message;
- provider response bodies containing test-sensitive strings must never be copied into exception messages.

- [ ] **Step 4: Run RED**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
```

- [ ] **Step 5: Implement validated opaque cursor**

Base64URL-encode/decode Graph `@odata.nextLink`. Decoding must require:

```csharp
uri.Scheme == Uri.UriSchemeHttps
&& string.Equals(uri.Host, "graph.microsoft.com", StringComparison.OrdinalIgnoreCase)
&& uri.AbsolutePath.StartsWith("/v1.0/me/", StringComparison.Ordinal)
```

Anything else throws `InvalidOperationException("El cursor de Microsoft Graph no es válido.")`.

- [ ] **Step 6: Implement `MicrosoftGraphMailProvider` Phase 1 methods**

New inbox request:

```text
me/mailFolders/inbox/messages?$top={1..50}&$orderby=receivedDateTime desc&$select=id,from,subject,bodyPreview,receivedDateTime,isRead,hasAttachments
```

If folder is not `inbox` or `query.Search` is non-empty, return an empty page in Phase 1 rather than incorrect/unfiltered data.

Single message request:

```text
me/messages/{messageId}?$select=id,from,toRecipients,ccRecipients,subject,body,bodyPreview,receivedDateTime,isRead,hasAttachments
```

Read-state PATCH uses `JsonContent.Create(new { isRead = read })`.

`GetThreadAsync` returns `[]`; `GetFoldersAsync` returns only inbox. `GetAttachmentAsync`, send/reply/reply-all/forward, move/trash, and empty-folder throw one consistent Phase 1 `NotSupportedException`.

- [ ] **Step 7: Normalize Graph status errors without provider-body leakage**

Create a private helper used by Graph calls. Required mapping:

```text
401/403 -> "Microsoft 365 rechazó el acceso al buzón. Vuelve a conectar la cuenta o revisa los permisos de la organización."
429     -> "Microsoft 365 está limitando temporalmente las solicitudes. Inténtalo nuevamente en unos minutos."
5xx     -> HttpRequestException("Microsoft 365 no está disponible temporalmente.")
other   -> HttpRequestException("Microsoft Graph no pudo completar la operación.")
```

Do not append `response.Content`, tokens, request headers, or raw Graph errors to these messages.

- [ ] **Step 8: Run GREEN and commit**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
git add src/backend/NexoMail.Infrastructure/Microsoft src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs
git commit -m "feat: add Microsoft Graph read provider"
```

---

### Task 5: Wire Microsoft OAuth/provider services into the API and distinguish consent failures

**Files:**
- Modify: `src/backend/NexoMail.Api/Program.cs`
- Modify: `src/backend/NexoMail.Api/appsettings.Development.example.json`
- Modify: `src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs`

- [ ] **Step 1: Add failing callback-error classification tests**

Expose a small pure helper on `MicrosoftOAuthService`, e.g. `AuthorizationFailureMessage(string? error, string? errorDescription)`, and test:

- plain `access_denied` with no admin marker -> user cancellation/denial message;
- description containing `AADSTS65001`, `AADSTS90094`, `admin approval`, or `administrator` (case-insensitive) -> explicit organization/admin-approval message;
- arbitrary provider text containing sensitive/test text is never echoed verbatim.

- [ ] **Step 2: Register services**

In `Program.cs` add `using NexoMail.Infrastructure.Microsoft;`, configure the `Microsoft` section, and register:

```csharp
builder.Services.AddHttpClient("MicrosoftGraph", client =>
    client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/"));
builder.Services.AddScoped<MailAccountConnectionPolicy>();
builder.Services.AddScoped<MicrosoftOAuthService>();
builder.Services.AddScoped<MicrosoftGraphTokenProvider>();
```

When `DemoMode` is false, register `MicrosoftGraphMailProvider` and wrap it with `UserScopedMailProvider`, alongside the existing Gmail provider.

- [ ] **Step 3: Add OAuth endpoints**

Add:

```text
GET /api/oauth/microsoft/start
GET /api/oauth/microsoft/callback
```

`start` runs the shared account-limit policy and redirects to Microsoft.

Callback binds optional `code`, `state`, `error`, and `error_description`. For provider errors call the safe classification helper; never surface raw `error_description`. For incomplete callbacks use a Microsoft-specific generic error. Catch `InvalidOperationException` and `HttpRequestException` and redirect to `FailureRedirect` with safe messages.

- [ ] **Step 4: Fix committed development template**

Replace the old unused TenantId shape with:

```json
"Microsoft": {
  "ClientId": "",
  "ClientSecret": "",
  "RedirectUri": "http://localhost:5052/api/oauth/microsoft/callback",
  "FrontendUrl": "http://localhost:5173/settings/accounts"
}
```

No real values in committed config.

- [ ] **Step 5: Build and run Microsoft smoke tests**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
dotnet build NexoMail.sln --configuration Release
```

Expected: PASS and build exit 0.

- [ ] **Step 6: Commit**

```powershell
git add src/backend/NexoMail.Api/Program.cs src/backend/NexoMail.Api/appsettings.Development.example.json src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs
git commit -m "feat: expose Microsoft OAuth endpoints"
```

---

### Task 6: Add Microsoft account UI and gate all unsupported write surfaces

**Files:**
- Modify: `src/frontend/src/pages/AccountsPage.tsx`
- Modify: `src/frontend/src/pages/InboxPage.tsx`
- Modify: `src/frontend/src/pages/MessagePage.tsx`
- Modify: `src/frontend/src/pages/ComposePage.tsx`
- Modify: `src/frontend/src/pages/NexiActionPlanPage.tsx`
- Modify: `src/frontend/src/pages/NexiSearchActionPage.tsx`
- Create: `src/frontend/scripts/microsoft-graph-smoke.mjs`
- Modify: `.github/workflows/frontend-build.yml`

- [ ] **Step 1: Write the frontend smoke script first and run RED**

The script reads the six page files and asserts:

```js
accountsPage.includes('Agregar Microsoft 365')
accountsPage.includes('/api/oauth/microsoft/start')
accountsPage.includes("connected') === 'microsoft'")
inboxPage.includes("'MicrosoftGraph'")
messagePage.includes("'MicrosoftGraph'")
composePage.includes("'MicrosoftGraph'")
nexiActionPlanPage.includes("'MicrosoftGraph'")
nexiSearchActionPage.includes("'MicrosoftGraph'")
```

Run:

```powershell
cd src/frontend
node scripts/microsoft-graph-smoke.mjs
```

Expected: RED.

- [ ] **Step 2: Add Microsoft 365 connect/success UI**

In `AccountsPage.tsx` add a second connect action to `/api/oauth/microsoft/start`, disabled by the same commercial account limit as Gmail. Add `connected=microsoft` success notice.

Fix edit/remove wording so Microsoft accounts display `Microsoft 365` and generic removal text says the messages remain with the provider, not specifically Gmail.

- [ ] **Step 3: Gate `InboxPage` unsupported operations by provider**

The page already loads `accounts`. Build `accountProviderById`. For a Microsoft account or a mixed selection containing Microsoft:

Keep enabled:
- refresh;
- open message;
- select messages;
- mark read/unread.

Disable/hide:
- archive/move/spam/trash;
- empty trash;
- ignore sender;
- draft-specific actions.

Do not execute a bulk operation partially on a mixed Gmail+Microsoft selection.

- [ ] **Step 4: Gate `MessagePage` unsupported operations**

Load/reuse the account list and compute `isMicrosoftPhase1` from `accountId`.

For Microsoft keep:
- message body display;
- automatic mark-as-read;
- navigation to previous/next messages.

Hide/disable:
- reply/reply all/forward;
- archive/spam/trash/move;
- ignore sender;
- attachment download/preview links because Graph attachment retrieval is not implemented;
- Control Center tracking/finalization actions that rely on Gmail-only services.

Thread query may remain because the provider safely returns `[]`.

- [ ] **Step 5: Prevent Microsoft accounts from becoming send/draft/contact sources in `ComposePage`**

Create a send-capable account collection that excludes `MicrosoftGraph` in Phase 1. New compose defaults and account selector use only send-capable accounts. If navigation state attempts reply/forward/edit-draft from a Microsoft account, show a clear Phase 1 unsupported notice and prevent send/save/contact lookup.

This is required because otherwise a connected Microsoft account would appear selectable and fail only after the user composes a message.

- [ ] **Step 6: Gate Nexi write actions**

In both `NexiActionPlanPage.tsx` and `NexiSearchActionPage.tsx`, resolve each item's account provider before executing a mail action.

For Microsoft Phase 1:
- allow only `mark_read` and `mark_unread` if those actions are surfaced;
- do not execute archive/trash/move/send/reply/forward/draft or other unsupported provider writes;
- mark unsupported planned actions as unavailable/skipped with a user-safe explanation rather than reporting completion.

Gmail execution paths stay unchanged.

- [ ] **Step 7: Make frontend smoke GREEN and add CI hook**

Strengthen the smoke script to assert capability checks exist in Inbox, Message, Compose, and both Nexi pages. Add to `.github/workflows/frontend-build.yml` before build:

```yaml
      - name: Verify Microsoft Graph Phase 1 UI
        run: node scripts/microsoft-graph-smoke.mjs
```

Run:

```powershell
cd src/frontend
node scripts/microsoft-graph-smoke.mjs
pnpm build
```

Expected: PASS and Vite build exit 0.

- [ ] **Step 8: Commit**

```powershell
git add src/frontend/src/pages/AccountsPage.tsx src/frontend/src/pages/InboxPage.tsx src/frontend/src/pages/MessagePage.tsx src/frontend/src/pages/ComposePage.tsx src/frontend/src/pages/NexiActionPlanPage.tsx src/frontend/src/pages/NexiSearchActionPage.tsx src/frontend/scripts/microsoft-graph-smoke.mjs .github/workflows/frontend-build.yml
git commit -m "feat: gate Microsoft Phase 1 UI capabilities"
```

---

### Task 7: Prove Gmail-only service isolation

**Files:**
- Inspect/test: `src/backend/NexoMail.Infrastructure/Google/GoogleContactsService.cs`
- Inspect/test: `src/backend/NexoMail.Infrastructure/Google/GmailRuleService.cs`
- Inspect/test: `src/backend/NexoMail.Infrastructure/Google/GmailControlCenterService.cs`
- Inspect/test: `src/backend/NexoMail.Infrastructure/Google/GmailControlCenterActivityService.cs`
- Inspect/test: `src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexService.cs`
- Modify only where a missing provider guard is found
- Modify: `src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs`

- [ ] **Step 1: Seed one Gmail and one Microsoft account in isolation tests**

For every Gmail-specific service entry point exercised by the current UI, verify account lookup either:

- explicitly requires `Provider == MailProviderType.Gmail`, or
- rejects a Microsoft account before calling a Google endpoint.

The smoke harness must prove at least contacts, rules, control-center snapshot/activity, and metadata indexing cannot process the Microsoft account.

- [ ] **Step 2: Run RED only for missing guards**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
```

If all current services already contain correct provider filters, record PASS without production edits. Do not manufacture a refactor merely to create a diff.

- [ ] **Step 3: Add only missing guards**

Required account predicate for Gmail-only aggregation paths:

```csharp
.Where(x => x.UserId == userId && x.IsActive && x.Provider == MailProviderType.Gmail)
```

Direct account-specific services must reject `account.Provider != MailProviderType.Gmail` before Google HTTP calls.

- [ ] **Step 4: Assert unsupported Microsoft provider methods throw**

Call one representative unsupported method such as `MoveToTrashAsync`; assert it throws the Microsoft Phase 1 unsupported exception and never returns success.

- [ ] **Step 5: Run GREEN and commit only if files changed**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
git status --short
```

If production guards changed:

```powershell
git add src/backend/NexoMail.Infrastructure/Google src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs
git commit -m "test: enforce Microsoft provider isolation"
```

If only smoke assertions changed, commit only the smoke file.

---

### Task 8: Add Microsoft CI, configure local secrets safely, and run the real-account acceptance test

**Files:**
- Create: `.github/workflows/microsoft-graph-smoke.yml`
- Modify only if needed: `README.md`

- [ ] **Step 1: Add a dedicated no-secret CI workflow**

Workflow steps:

```yaml
name: Microsoft Graph smoke test
on:
  push:
    branches: [main, 'feature/**']
  pull_request:
    branches: [main]

jobs:
  microsoft-graph:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet restore src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
      - run: dotnet build src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj --no-restore --configuration Release
      - run: dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj --no-build --configuration Release
      - run: dotnet build NexoMail.sln --configuration Release
```

Add a repository scan limited to committed production/config Microsoft files that rejects non-empty `ClientSecret` values or obvious raw token fields. Do not scan smoke fixtures whose synthetic test tokens are intentionally present.

- [ ] **Step 2: Run the full regression suite**

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

Expected: all smoke tests PASS; both builds exit 0.

- [ ] **Step 3: Configure the real Client ID/secret with .NET user-secrets**

`NexoMail.Api.csproj` already has a `UserSecretsId`, so prefer user-secrets over writing the secret into a JSON file:

```powershell
cd src/backend/NexoMail.Api
dotnet user-secrets set "Microsoft:ClientId" "9ee2ba3a-4565-4848-b3d8-a9d414d36963"
dotnet user-secrets set "Microsoft:ClientSecret" "PASTE_THE_LOCAL_SECRET_HERE"
dotnet user-secrets set "Microsoft:RedirectUri" "http://localhost:5052/api/oauth/microsoft/callback"
dotnet user-secrets set "Microsoft:FrontendUrl" "http://localhost:5173/settings/accounts"
dotnet user-secrets set "MailProviders:DemoMode" "false"
```

The developer types the actual secret only into the local terminal. It must not be pasted into source, GitHub, CI, chat, screenshots, or documentation.

Verify:

```powershell
cd ../../..
git status --short
```

Expected: no secret/config file appears.

- [ ] **Step 4: Run the manual Microsoft 365 flow**

1. Start backend at `http://localhost:5052` and frontend at `http://localhost:5173`.
2. Sign into NexoMail.
3. Open `Cuentas de correo`.
4. Click `Agregar Microsoft 365`.
5. Authenticate only in Microsoft's UI; approve Microsoft Authenticator if requested.
6. If Microsoft reports administrator approval is required, capture only the non-sensitive user-facing result and stop; never bypass it.
7. If OAuth succeeds, verify the connected account appears as `Microsoft 365`.
8. Open that account's inbox and verify sender, subject, preview, date, unread state, and pagination.
9. Open one unread message and verify body/recipients; confirm it becomes read in Microsoft 365.
10. Confirm unsupported write/attachment/compose/Nexi actions are unavailable for Microsoft.
11. Confirm an existing Gmail account still lists and opens messages normally.

- [ ] **Step 5: Verify persistence/privacy after a successful Graph read test**

Inspect only schema/state necessary to prove:

- `MailAccounts.Provider == MicrosoftGraph`;
- `OAuthCredentials.EncryptedRefreshToken` is protected/ciphertext-like;
- no access-token column/entity was added;
- no full Graph message body or attachment bytes were added to SQLite by this integration.

Do not decrypt or print the real stored refresh token during this verification.

- [ ] **Step 6: Commit CI/documentation changes**

```powershell
git add .github/workflows/microsoft-graph-smoke.yml README.md
git commit -m "ci: verify Microsoft Graph phase 1"
```

Omit `README.md` from the commit if it did not need changes.

- [ ] **Step 7: Final verification before declaring Phase 1 complete**

```powershell
git status --short
git log -5 --oneline
dotnet build NexoMail.sln --configuration Release
cd src/frontend
pnpm build
```

Required completion evidence:

- clean working tree;
- Microsoft smoke tests green;
- existing smoke/regression tests green;
- backend and frontend builds green;
- no real secret committed;
- at least one organizational Microsoft 365 account completes OAuth and exercises inbox list + message read + mark-read successfully.

A tenant-admin-approval screen on a specific external tenant is a valid diagnostic result, but by itself does not prove the Graph read path is functionally complete.