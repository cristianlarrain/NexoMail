# Microsoft Graph Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Microsoft 365 delegated OAuth plus a deliberately limited Microsoft Graph mailbox path that connects an organizational account, lists its inbox, opens a message, and marks it read/unread without persisting message bodies or attachment contents.

**Architecture:** Follow NexoMail's existing Gmail architecture: explicit `HttpClient` REST calls, `IMailProvider`/`MailGateway`, `UserScopedMailProvider`, EF Core SQLite, Data Protection, and the existing OAuth credential table. Microsoft-specific OAuth, token refresh, safe Graph pagination, and Graph mapping live in `NexoMail.Infrastructure.Microsoft`. Unsupported Microsoft write features remain unavailable in Phase 1 rather than appearing functional and failing later.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core SQLite, ASP.NET Core Data Protection, React/Vite/TypeScript, TanStack Query, pnpm, Microsoft Graph REST v1.0.

**Approved spec:** `docs/superpowers/specs/2026-09-11-microsoft-graph-integration-design.md`

## Fixed Phase 1 rules

- Authority: `https://login.microsoftonline.com/organizations`.
- Authorize endpoint: `https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize`.
- Token endpoint: `https://login.microsoftonline.com/organizations/oauth2/v2.0/token`.
- Graph base: `https://graph.microsoft.com/v1.0/`.
- Local callback: `http://localhost:5052/api/oauth/microsoft/callback`.
- OAuth scopes exactly: `openid profile email offline_access User.Read Mail.ReadWrite`.
- `Mail.Send` remains registered in Entra for a later phase but is not requested or used here.
- No application permissions, organization-wide mailbox access, personal Outlook/Hotmail, or Exchange on-premises.
- Never persist Microsoft access tokens, full message bodies, or attachment bytes.
- Persist only the protected refresh token through the existing `ITokenProtector`. For this phase reuse `NexoMail.Infrastructure.Google.ITokenProtector`; do not perform an unrelated namespace refactor.
- Never commit a real Microsoft secret, token, or real-mailbox fixture.
- Supported Microsoft operations: inbox list, single-message read, mark read/unread.
- Unsupported Microsoft operations must never report false success.
- Gmail behavior must remain unchanged.

---

### Task 1: Create the Microsoft smoke-test harness and extract shared account-limit policy

**Files**
- Create: `src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj`
- Create: `src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs`
- Create: `src/backend/NexoMail.Infrastructure/MailAccountConnectionPolicy.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Google/GoogleOAuthService.cs`
- Modify: `NexoMail.sln`

- [ ] **1.1 Write the failing account-limit test**

Create the smoke-test console project:

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

Use in-memory SQLite and this complete test user context:

```csharp
sealed class TestUserContext(Guid userId) : IUserContext
{
    public bool IsAuthenticated => true;
    public Guid UserId => userId;
    public string Email => "test@nexomail.local";
    public string DisplayName => "NexoMail Test";
}
```

Seed one active account under a plan with `MaxAccounts = 1`; assert `MailAccountConnectionPolicy.EnsureCanConnectAnotherAccountAsync` throws `InvalidOperationException`.

- [ ] **1.2 Run RED**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
```

Expected: missing `MailAccountConnectionPolicy`.

- [ ] **1.3 Implement shared policy and preserve Google API compatibility**

Move only Google OAuth's existing effective-plan/count logic into `MailAccountConnectionPolicy`. Inject it into `GoogleOAuthService` and keep:

```csharp
public Task EnsureCanConnectAnotherAccountAsync(CancellationToken ct) =>
    accountConnectionPolicy.EnsureCanConnectAnotherAccountAsync(ct);
```

- [ ] **1.4 Add project to solution and run GREEN**

```powershell
dotnet sln NexoMail.sln add src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
```

- [ ] **1.5 Commit**

```powershell
git add NexoMail.sln src/backend/NexoMail.MicrosoftGraphSmokeTests src/backend/NexoMail.Infrastructure/MailAccountConnectionPolicy.cs src/backend/NexoMail.Infrastructure/Google/GoogleOAuthService.cs
git commit -m "refactor: share mail account connection policy"
```

---

### Task 2: Implement Microsoft OAuth authorization, protected state, callback, and account persistence

**Files**
- Create: `src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftGraphOptions.cs`
- Create: `src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftOAuthService.cs`
- Modify: `src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs`

- [ ] **2.1 Add failing authorization URL/state tests**

With test-only options, assert `BeginAuthorization()` uses:

```text
https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize
redirect_uri=http://localhost:5052/api/oauth/microsoft/callback
scope=openid profile email offline_access User.Read Mail.ReadWrite
```

Assert `Mail.Send` is absent and `state` is present.

Add three state failures that occur before any HTTP call: tampered state, state for another NexoMail user, and state older than ten minutes. For expiry, protect equivalent JSON with Data Protection purpose `NexoMail.MicrosoftOAuth.State.v1` and `IssuedAt = UtcNow - 11 minutes`.

- [ ] **2.2 Run RED**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
```

- [ ] **2.3 Implement options and authorization/state**

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

State contains `UserId`, `IssuedAt`, random nonce; bind it to current `IUserContext` and expire at ten minutes.

- [ ] **2.4 Add failing callback/persistence/reconnection tests**

Fake token response:

```json
{"access_token":"test-access-token","refresh_token":"test-refresh-token","expires_in":3600}
```

Fake `/me`:

```json
{"id":"graph-test-user","displayName":"Microsoft Test","mail":"persona@empresa.test","userPrincipalName":"persona@empresa.test"}
```

Assert a MicrosoftGraph account is created, email is correct, display name is `Microsoft 365`, refresh token is protected, and no access token is persisted. Then deactivate and reconnect the same address; assert exactly one account exists and is reactivated. Also test `mail=null` with fallback to `userPrincipalName`.

The DB unique index is `(UserId, EmailAddress)` regardless of provider. Seed the same email under Gmail and assert Microsoft connection fails with a safe `InvalidOperationException` instead of a SQLite unique-index exception.

- [ ] **2.5 Implement callback exchange and persistence**

Exchange authorization code server-side using exact callback/scopes. Use access token transiently for `/me`. Resolve `mail`, then `userPrincipalName`.

Look up existing account by `(UserId, EmailAddress)` before insert. If another provider already owns that address, throw:

```text
Esta dirección ya está conectada en NexoMail mediante otro proveedor.
```

If the existing account is MicrosoftGraph, reactivate it. If creating a new account, call `MailAccountConnectionPolicy.EnsureCanConnectAnotherAccountAsync` again immediately before add so a slot consumed after OAuth start cannot bypass the plan limit.

Create new account with `Provider = MicrosoftGraph`, display name `Microsoft 365`, and a Microsoft-identifying color. Protect refresh token through `ITokenProtector`; never persist access token.

- [ ] **2.6 Run GREEN and commit**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
git add src/backend/NexoMail.Infrastructure/Microsoft src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs
git commit -m "feat: add Microsoft OAuth connection flow"
```

---

### Task 3: Implement access-token refresh, cache, and refresh-token rotation

**Files**
- Create: `src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftGraphTokenProvider.cs`
- Modify: `src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs`

- [ ] **3.1 Add failing refresh tests**

Seed protected `test-refresh-old`; fake:

```json
{"access_token":"test-access-new","refresh_token":"test-refresh-rotated","expires_in":3600}
```

Assert returned access token, protected rotated refresh token, no persisted access token, and only one token endpoint request across two immediate calls.

- [ ] **3.2 Run RED**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
```

- [ ] **3.3 Implement token provider**

Use per-account `ConcurrentDictionary<Guid, CachedAccessToken>` plus `SemaphoreSlim` gates. Cache until roughly two minutes before expiry. Refresh using exact Phase 1 scopes. If Microsoft rotates refresh token, protect and save it before returning. Cache validity must include credential `UpdatedAt`, so reconnecting invalidates an older cached access token.

- [ ] **3.4 Run GREEN and commit**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
git add src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftGraphTokenProvider.cs src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs
git commit -m "feat: add Microsoft token refresh and rotation"
```

---

### Task 4: Implement safe Graph inbox/read path and provider error normalization

**Files**
- Create: `src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftGraphCursor.cs`
- Create: `src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftGraphMailProvider.cs`
- Modify: `src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs`

- [ ] **4.1 Add failing inbox/cursor tests**

Use a Graph list fixture with `@odata.nextLink` and one message. Assert mapping to `MailSummary`, including id, sender, subject, bodyPreview, receivedDateTime, isRead, hasAttachments, account id, and `folderId=inbox`.

Round-trip the continuation cursor. Add hostile cursor tests for another host, HTTP instead of HTTPS, and Graph paths outside `/v1.0/me/`; each must fail before HTTP.

- [ ] **4.2 Add failing message/read-state tests**

Use a single-message fixture containing `from`, `toRecipients`, `ccRecipients`, `subject`, `body`, `bodyPreview`, `receivedDateTime`, `isRead`, and `hasAttachments`. Assert mapping to `MailMessage`.

If Graph body `contentType` is `html`, pass content as transient HTML. If `text`, HTML-encode it and convert line breaks before putting it into `HtmlBody`; add a plaintext-body test to prevent text containing `<tag>` from becoming markup.

Assert `MarkReadAsync` PATCHes `/me/messages/{id}` with `{ "isRead": true }` and also supports false.

- [ ] **4.3 Add failing safe-error tests**

Assert:

```text
401/403 -> InvalidOperationException with generic reconnect/permission message
429     -> InvalidOperationException with generic temporary throttling message
5xx     -> HttpRequestException with generic temporary Microsoft service message
other   -> HttpRequestException with generic Graph operation message
```

A fake response body containing a sensitive marker must not appear in any thrown message.

- [ ] **4.4 Run RED**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
```

- [ ] **4.5 Implement opaque validated cursor**

Base64URL encode/decode the Graph nextLink. Decoded URI is valid only when:

```csharp
uri.Scheme == Uri.UriSchemeHttps
&& string.Equals(uri.Host, "graph.microsoft.com", StringComparison.OrdinalIgnoreCase)
&& uri.AbsolutePath.StartsWith("/v1.0/me/", StringComparison.Ordinal)
```

Anything else throws `InvalidOperationException("El cursor de Microsoft Graph no es válido.")`.

- [ ] **4.6 Implement `MicrosoftGraphMailProvider`**

New page request:

```text
me/mailFolders/inbox/messages?$top={1..50}&$orderby=receivedDateTime desc&$select=id,from,subject,bodyPreview,receivedDateTime,isRead,hasAttachments
```

Cursor requests use only validated Graph nextLink. Non-inbox folders and non-empty search return empty pages in Phase 1.

Detail request:

```text
me/messages/{id}?$select=id,from,toRecipients,ccRecipients,subject,body,bodyPreview,receivedDateTime,isRead,hasAttachments
```

`GetThreadAsync` returns `[]`; `GetFoldersAsync` returns only inbox. Attachment download, send/reply/reply-all/forward, move/trash, and empty-folder throw one consistent Phase 1 `NotSupportedException`.

- [ ] **4.7 Implement safe Graph status mapping**

Never append response bodies/tokens/headers. Required user-safe messages:

```text
401/403: Microsoft 365 rechazó el acceso al buzón. Vuelve a conectar la cuenta o revisa los permisos de la organización.
429: Microsoft 365 está limitando temporalmente las solicitudes. Inténtalo nuevamente en unos minutos.
5xx: Microsoft 365 no está disponible temporalmente.
other: Microsoft Graph no pudo completar la operación.
```

- [ ] **4.8 Run GREEN and commit**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
git add src/backend/NexoMail.Infrastructure/Microsoft src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs
git commit -m "feat: add Microsoft Graph read provider"
```

---

### Task 5: Wire Microsoft into ASP.NET Core and normalize API/OAuth errors

**Files**
- Modify: `src/backend/NexoMail.Api/Program.cs`
- Modify: `src/backend/NexoMail.Api/appsettings.Development.example.json`
- Modify: `src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs`

- [ ] **5.1 Add failing consent-error classification tests**

Add a pure helper on `MicrosoftOAuthService`, e.g. `AuthorizationFailureMessage(error, errorDescription)`. Test:

- plain `access_denied` -> cancellation/denial message;
- description containing `AADSTS65001`, `AADSTS90094`, `admin approval`, or `administrator`, case-insensitive -> explicit organization/admin-approval message;
- arbitrary/sensitive provider description is never echoed.

- [ ] **5.2 Register Microsoft services**

In `Program.cs` configure `MicrosoftGraphOptions`, add named `MicrosoftGraph` HttpClient, `MailAccountConnectionPolicy`, `MicrosoftOAuthService`, and `MicrosoftGraphTokenProvider`. When `DemoMode=false`, register `MicrosoftGraphMailProvider` wrapped in `UserScopedMailProvider` alongside Gmail.

- [ ] **5.3 Add OAuth routes**

Add authenticated:

```text
GET /api/oauth/microsoft/start
GET /api/oauth/microsoft/callback
```

`start` runs the shared connection policy. Callback accepts optional `code`, `state`, `error`, `error_description`, uses the safe classifier for Microsoft errors, and never surfaces raw provider descriptions. Catch OAuth exchange `HttpRequestException` with a generic Microsoft connection message.

- [ ] **5.4 Add generic API handling for unsupported/provider failures**

Extend existing top-level API exception middleware without exposing internals:

```text
NotSupportedException -> 400 JSON { error: safe exception message }
HttpRequestException  -> 502 JSON { error: "El proveedor de correo no pudo completar la operación. Inténtalo nuevamente." }
```

Keep the existing `KeyNotFoundException -> 404` behavior. Do not globally echo arbitrary `InvalidOperationException` messages.

This ensures a direct API call to an unsupported Microsoft operation is user-safe even if UI gating is bypassed.

- [ ] **5.5 Fix committed development config template**

Use:

```json
"Microsoft": {
  "ClientId": "",
  "ClientSecret": "",
  "RedirectUri": "http://localhost:5052/api/oauth/microsoft/callback",
  "FrontendUrl": "http://localhost:5173/settings/accounts"
}
```

Remove unused `TenantId`; commit no real credentials.

- [ ] **5.6 Build/test GREEN and commit**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
dotnet build NexoMail.sln --configuration Release
git add src/backend/NexoMail.Api/Program.cs src/backend/NexoMail.Api/appsettings.Development.example.json src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs
git commit -m "feat: expose Microsoft OAuth endpoints"
```

---

### Task 6: Add Microsoft account UI and gate every unsupported write surface

**Files**
- Modify: `src/frontend/src/pages/AccountsPage.tsx`
- Modify: `src/frontend/src/pages/InboxPage.tsx`
- Modify: `src/frontend/src/pages/MessagePage.tsx`
- Modify: `src/frontend/src/pages/ComposePage.tsx`
- Modify: `src/frontend/src/pages/NexiActionPlanPage.tsx`
- Modify: `src/frontend/src/pages/NexiSearchActionPage.tsx`
- Create: `src/frontend/scripts/microsoft-graph-smoke.mjs`
- Modify: `.github/workflows/frontend-build.yml`

- [ ] **6.1 Write frontend smoke first; run RED**

Smoke script must confirm all six pages explicitly recognize `MicrosoftGraph`, AccountsPage contains `Agregar Microsoft 365`, `/api/oauth/microsoft/start`, and the `connected=microsoft` success state.

```powershell
cd src/frontend
node scripts/microsoft-graph-smoke.mjs
```

- [ ] **6.2 Add account connect/success UI**

Add `Agregar Microsoft 365` beside Gmail, governed by the same account limit. Add Microsoft-specific success notice. Make edit/remove provider labels generic/correct; removal must not claim Microsoft mail remains in Gmail.

- [ ] **6.3 Gate InboxPage**

Use the already-loaded accounts to map account ID -> provider. For Microsoft account views or mixed selections containing Microsoft, keep refresh/open/select/mark-read available. Hide/disable archive, spam, trash, move, ignore sender, empty trash, and draft-specific actions. Never partially execute a mixed-provider bulk action.

- [ ] **6.4 Gate MessagePage**

Resolve current account provider. Microsoft Phase 1 keeps display, navigation, and automatic mark-as-read. Hide/disable reply, reply-all, forward, archive, spam, trash/move, ignore sender, attachment download/preview, and Gmail Control Center tracking/finalization actions. Thread query may remain because provider returns empty safely.

- [ ] **6.5 Gate ComposePage before any provider call**

Create a send-capable account collection excluding `MicrosoftGraph`. New-message sender selection/defaults use only send-capable accounts. If navigation state tries reply/forward/edit-draft from Microsoft, show a clear Phase 1 unsupported notice and prevent send, save-draft, and Google Contacts lookup.

- [ ] **6.6 Gate Nexi action execution**

In both `NexiActionPlanPage.tsx` and `NexiSearchActionPage.tsx`, resolve each item's account provider. For Microsoft, only `mark_read`/`mark_unread` may execute in Phase 1. Archive/trash/move/send/reply/forward/draft and other unsupported write actions must be marked unavailable/skipped with a user-safe explanation, never `completed`. Gmail behavior is unchanged.

- [ ] **6.7 Run frontend smoke/build GREEN and add workflow step**

Add:

```yaml
- name: Verify Microsoft Graph Phase 1 UI
  run: node scripts/microsoft-graph-smoke.mjs
```

Then:

```powershell
cd src/frontend
node scripts/microsoft-graph-smoke.mjs
pnpm build
```

- [ ] **6.8 Commit**

```powershell
git add src/frontend/src/pages/AccountsPage.tsx src/frontend/src/pages/InboxPage.tsx src/frontend/src/pages/MessagePage.tsx src/frontend/src/pages/ComposePage.tsx src/frontend/src/pages/NexiActionPlanPage.tsx src/frontend/src/pages/NexiSearchActionPage.tsx src/frontend/scripts/microsoft-graph-smoke.mjs .github/workflows/frontend-build.yml
git commit -m "feat: gate Microsoft Phase 1 UI capabilities"
```

---

### Task 7: Prove Gmail-only service isolation

**Files**
- Inspect/test: `src/backend/NexoMail.Infrastructure/Google/GoogleContactsService.cs`
- Inspect/test: `src/backend/NexoMail.Infrastructure/Google/GmailRuleService.cs`
- Inspect/test: `src/backend/NexoMail.Infrastructure/Google/GmailControlCenterService.cs`
- Inspect/test: `src/backend/NexoMail.Infrastructure/Google/GmailControlCenterActivityService.cs`
- Inspect/test: `src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexService.cs`
- Modify only a service that lacks the required provider guard
- Modify: `src/backend/NexoMail.MicrosoftGraphSmokeTests/Program.cs`

- [ ] **7.1 Add isolation assertions with one Gmail and one Microsoft account**

Prove contacts and rules reject Microsoft before Google HTTP calls, and control-center/activity/metadata account queries include only Gmail. Aggregation query guard is:

```csharp
.Where(x => x.UserId == userId && x.IsActive && x.Provider == MailProviderType.Gmail)
```

- [ ] **7.2 Run tests; change production only where a guard is missing**

```powershell
dotnet run --project src/backend/NexoMail.MicrosoftGraphSmokeTests/NexoMail.MicrosoftGraphSmokeTests.csproj
```

Do not manufacture refactors if current Gmail services already satisfy the invariant.

- [ ] **7.3 Assert unsupported provider method cannot falsely succeed**

Call a representative Microsoft unsupported method such as `MoveToTrashAsync`; require `NotSupportedException` and no Graph write request.

- [ ] **7.4 Run GREEN and commit the actual changed files**

Commit smoke assertions and only any production guard actually required.

---

### Task 8: Add Microsoft CI, configure local secrets safely, and perform real-account acceptance

**Files**
- Create: `.github/workflows/microsoft-graph-smoke.yml`
- Modify `README.md` only if setup documentation is genuinely missing

- [ ] **8.1 Add dedicated no-secret CI**

Workflow checks out, sets up .NET 10, restores/builds/runs `NexoMail.MicrosoftGraphSmokeTests`, then builds `NexoMail.sln`. Add a scan of committed production/config Microsoft files that rejects non-empty `ClientSecret` values or obvious token fields; exclude synthetic smoke-test fixtures.

- [ ] **8.2 Run full regression locally**

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

All smoke tests must PASS; both builds must exit 0.

- [ ] **8.3 Configure local Microsoft secret with .NET user-secrets**

`NexoMail.Api.csproj` already has a `UserSecretsId`. Use an interactive PowerShell variable so the actual secret is not typed into source or the command itself:

```powershell
cd src/backend/NexoMail.Api
dotnet user-secrets set "Microsoft:ClientId" "9ee2ba3a-4565-4848-b3d8-a9d414d36963"
$msSecret = Read-Host "Pegue aquí el secreto local de NexoMail"
dotnet user-secrets set "Microsoft:ClientSecret" "$msSecret"
Remove-Variable msSecret
dotnet user-secrets set "Microsoft:RedirectUri" "http://localhost:5052/api/oauth/microsoft/callback"
dotnet user-secrets set "Microsoft:FrontendUrl" "http://localhost:5173/settings/accounts"
dotnet user-secrets set "MailProviders:DemoMode" "false"
```

Never paste the secret into GitHub, CI, chat, screenshots, or committed JSON.

- [ ] **8.4 Run manual Microsoft 365 flow**

1. Start backend on `http://localhost:5052` and frontend on `http://localhost:5173`.
2. Sign in to NexoMail and open `Cuentas de correo`.
3. Click `Agregar Microsoft 365`.
4. Authenticate only in Microsoft's UI; approve Microsoft Authenticator if requested.
5. If the external tenant requires admin approval, record only the safe visible result and stop; do not bypass policy.
6. If OAuth succeeds, verify account appears as `Microsoft 365`.
7. List inbox; verify sender, subject, preview, date, unread state, and pagination.
8. Open an unread message; verify sender/recipients/body, then confirm it becomes read in Microsoft 365.
9. Confirm unsupported Microsoft write/attachment/compose/Nexi actions are unavailable.
10. Confirm an existing Gmail account still lists and opens normally.

- [ ] **8.5 Verify privacy/persistence after a successful Graph read test**

Confirm only that:

- `MailAccounts.Provider == MicrosoftGraph`;
- stored refresh credential is protected/ciphertext-like;
- no access-token field/table was added;
- no full Microsoft body or attachment bytes were added to SQLite by this integration.

Do not decrypt or print the real refresh token.

- [ ] **8.6 Commit CI/docs changes**

```powershell
git add .github/workflows/microsoft-graph-smoke.yml
git add README.md  # only when actually modified
git commit -m "ci: verify Microsoft Graph phase 1"
```

- [ ] **8.7 Final verification before claiming completion**

```powershell
git status --short
git log -5 --oneline
dotnet build NexoMail.sln --configuration Release
cd src/frontend
pnpm build
```

Completion requires: clean working tree; Microsoft and regression smoke tests green; backend/frontend builds green; no real credential committed; and at least one organizational Microsoft 365 account completing OAuth plus inbox list, message read, and mark-read against live Graph.

An admin-consent block on a particular external tenant is useful diagnostic evidence, but by itself does not prove the Graph read integration works end-to-end.