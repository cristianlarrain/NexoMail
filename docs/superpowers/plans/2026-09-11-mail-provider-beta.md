# Mail Provider Beta Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dejar NexoMail listo para una marcha blanca de 30 días con Gmail estable, Microsoft 365 por Microsoft Graph e IMAP/SMTP genérico en Beta, usando un modal único de proveedores.

**Architecture:** Se conserva `MailAccount -> MailProviderType -> IMailProvider -> MailGateway`. Microsoft 365 usa OAuth organizacional y `OAuthCredentials`; IMAP/SMTP usa MailKit y una entidad de credenciales propia cifrada con Data Protection. La UI sólo ofrece capacidades realmente disponibles y mantiene Gmail sin regresiones.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core SQLite, HttpClient, Microsoft Graph REST, MailKit/MimeKit, React 19, TypeScript, Vite, pnpm.

**Spec:** `docs/superpowers/specs/2026-09-11-mail-provider-beta-design.md`

## Global Constraints

- Rama objetivo: `feature/commercial-foundation`.
- Gmail debe seguir funcionando sin regresiones.
- Microsoft 365 sólo para cuentas organizacionales durante la marcha blanca.
- IMAP/SMTP se publica como Beta con configuración manual y prueba antes de persistir.
- Exchange Server on-premise, Yahoo OAuth dedicado y Outlook.com/Hotmail quedan fuera.
- No persistir cuerpos completos de mensajes ni bytes de adjuntos.
- No registrar tokens, contraseñas, códigos OAuth ni cuerpos de correo.
- Las credenciales persistidas deben quedar cifradas.
- Los límites de cuentas por plan se validan en backend.
- IMAP Beta no ofrece borradores remotos durante esta fase.

---

### Task 1: Persistencia y contratos de proveedores

**Files:**
- Modify: `src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Data/DatabaseBootstrap.cs`
- Modify: `src/backend/NexoMail.Infrastructure/NexoMail.Infrastructure.csproj`
- Create: `src/backend/NexoMail.Infrastructure/Imap/ImapCredentialModels.cs`
- Create: `src/backend/NexoMail.CommercialSmokeTests/MailProviderPersistenceSmoke.cs`
- Modify: `src/backend/NexoMail.CommercialSmokeTests/Program.cs`

**Interfaces:**
- Produces: `DbSet<ImapCredentialEntity> ImapCredentials`.
- Produces: `ImapCredentialEntity` 1:1 con `MailAccountEntity`.
- Produces: paquete `MailKit` para IMAP/SMTP y MimeKit.

- [ ] **Step 1: Write the failing persistence smoke test**

```csharp
var account = new MailAccountEntity { Id = Guid.NewGuid(), UserId = user.Id, Provider = MailProviderType.Imap, EmailAddress = "beta@example.com", DisplayName = "Beta", CreatedAt = DateTimeOffset.UtcNow };
db.MailAccounts.Add(account);
db.ImapCredentials.Add(new ImapCredentialEntity {
    Id = Guid.NewGuid(), MailAccountId = account.Id, Username = "beta@example.com",
    EncryptedPassword = "cipher", ImapHost = "imap.example.com", ImapPort = 993,
    ImapSecurity = "ssl", SmtpHost = "smtp.example.com", SmtpPort = 465,
    SmtpSecurity = "ssl", UpdatedAt = DateTimeOffset.UtcNow
});
await db.SaveChangesAsync();
Assert(await db.ImapCredentials.AnyAsync(x => x.MailAccountId == account.Id), "IMAP credential must persist");
```

- [ ] **Step 2: Run the test and verify it fails because `ImapCredentials` does not exist**

Run: `dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj`
Expected: FAIL/compile error referencing the missing IMAP entity.

- [ ] **Step 3: Add the entity and EF mapping**

```csharp
public sealed class ImapCredentialEntity {
    public Guid Id { get; set; }
    public Guid MailAccountId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string EncryptedPassword { get; set; } = string.Empty;
    public string ImapHost { get; set; } = string.Empty;
    public int ImapPort { get; set; }
    public string ImapSecurity { get; set; } = "ssl";
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; }
    public string SmtpSecurity { get; set; } = "starttls";
    public DateTimeOffset UpdatedAt { get; set; }
}
```

Map it with `HasOne<MailAccountEntity>().WithOne().HasForeignKey<ImapCredentialEntity>(x => x.MailAccountId).OnDelete(DeleteBehavior.Cascade)` and create the table from `DatabaseBootstrap` for existing SQLite databases.

- [ ] **Step 4: Add MailKit**

```xml
<PackageReference Include="MailKit" Version="4.14.1" />
```

- [ ] **Step 5: Run persistence smoke tests**

Run: `dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/backend/NexoMail.Infrastructure src/backend/NexoMail.CommercialSmokeTests
git commit -m "feat: add imap credential persistence"
```

---

### Task 2: Microsoft 365 OAuth y proveedor Graph

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/Microsoft/Microsoft365Options.cs`
- Create: `src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftOAuthService.cs`
- Create: `src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftGraphMailProvider.cs`
- Create: `src/backend/NexoMail.Infrastructure/Microsoft/MicrosoftGraphDraftProvider.cs`
- Modify: `src/backend/NexoMail.Api/Program.cs`
- Modify: `src/backend/NexoMail.Api/appsettings.json`
- Modify: `src/backend/NexoMail.Api/appsettings.Development.example.json`
- Create: `src/backend/NexoMail.CommercialSmokeTests/MicrosoftProviderSmoke.cs`
- Modify: `src/backend/NexoMail.CommercialSmokeTests/Program.cs`

**Interfaces:**
- Produces: `MicrosoftOAuthService.BeginAuthorization()` y `CompleteAuthorizationAsync(...)`.
- Produces: `IMailProvider` con `ProviderType == MailProviderType.MicrosoftGraph`.
- Produces: `IMailDraftProvider` con `ProviderType == MailProviderType.MicrosoftGraph`.
- API: `GET /api/oauth/microsoft/start`, `GET /api/oauth/microsoft/callback`.

- [ ] **Step 1: Write failing Microsoft smoke tests**

```csharp
Assert(MailProviderType.MicrosoftGraph.ToString() == "MicrosoftGraph", "Microsoft provider enum must remain stable");
var options = new Microsoft365Options { ClientId = "client", ClientSecret = "secret", RedirectUri = "https://nexomail.test/api/oauth/microsoft/callback" };
Assert(options.AuthorityTenant == "organizations", "Marcha blanca must default to organizational accounts");
```

Add source-contract assertions that the authorization scope contains `offline_access`, `User.Read`, `Mail.ReadWrite`, and `Mail.Send` and does not include calendar/files permissions.

- [ ] **Step 2: Verify the smoke test fails because Microsoft infrastructure is absent**

Run: `dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj`
Expected: FAIL/compile error.

- [ ] **Step 3: Implement Microsoft options and OAuth**

```csharp
public sealed class Microsoft365Options {
    public const string SectionName = "Microsoft365";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string FrontendUrl { get; set; } = "http://localhost:5173/settings/accounts";
    public string AuthorityTenant { get; set; } = "organizations";
}
```

Authorization endpoint: `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize`; token endpoint: corresponding `/token`. State is Data Protection-protected, user-bound, nonce-bearing and expires after 10 minutes. Complete callback by calling `https://graph.microsoft.com/v1.0/me?$select=mail,userPrincipalName,displayName`, creating/updating a `MailAccountEntity` with `MicrosoftGraph`, then persisting encrypted refresh token into `OAuthCredentials`.

- [ ] **Step 4: Implement Graph provider**

Use Graph REST with access tokens refreshed from the encrypted refresh token. Implement:

```csharp
public MailProviderType ProviderType => MailProviderType.MicrosoftGraph;
public Task<PagedResult<MailSummary>> GetMessagesAsync(MailQuery query, CancellationToken ct);
public Task<MailMessage?> GetMessageAsync(Guid accountId, string messageId, CancellationToken ct);
public Task SendAsync(ComposeMessage message, CancellationToken ct);
public Task ReplyAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken ct);
public Task ReplyAllAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken ct);
public Task ForwardAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken ct);
public Task MarkReadAsync(Guid accountId, string messageId, bool read, CancellationToken ct);
public Task MoveToTrashAsync(Guid accountId, string messageId, CancellationToken ct);
public Task MoveToFolderAsync(Guid accountId, string messageId, string folderId, CancellationToken ct);
public Task<IReadOnlyCollection<MailFolder>> GetFoldersAsync(Guid accountId, CancellationToken ct);
```

Use well-known Graph folders `inbox`, `archive`, `sentitems`, `drafts`, `junkemail`, `deleteditems`. Request HTML body only when opening a message, not in list calls.

- [ ] **Step 5: Implement Microsoft drafts**

Use Graph `/me/messages` create/update and `/send` endpoints, preserving the existing `IMailDraftProvider` contract.

- [ ] **Step 6: Register services and OAuth routes**

```csharp
builder.Services.Configure<Microsoft365Options>(builder.Configuration.GetSection(Microsoft365Options.SectionName));
builder.Services.AddScoped<MicrosoftOAuthService>();
builder.Services.AddScoped<MicrosoftGraphMailProvider>();
builder.Services.AddScoped<IMailProvider>(sp => new UserScopedMailProvider(sp.GetRequiredService<MicrosoftGraphMailProvider>(), sp.GetRequiredService<NexoMailDbContext>(), sp.GetRequiredService<IUserContext>()));
builder.Services.AddScoped<IMailDraftProvider, MicrosoftGraphDraftProvider>();
```

Routes must redirect institutional consent/admin-policy failures to `/settings/accounts?error=...` with a user-readable explanation.

- [ ] **Step 7: Run backend smoke tests and build**

Run: `dotnet build NexoMail.sln`
Run: `dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/backend
git commit -m "feat: add microsoft 365 mail provider"
```

---

### Task 3: IMAP/SMTP Beta seguro

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/Imap/ImapConnectionRequest.cs`
- Create: `src/backend/NexoMail.Infrastructure/Imap/ImapAccountService.cs`
- Create: `src/backend/NexoMail.Infrastructure/Imap/ImapMailProvider.cs`
- Modify: `src/backend/NexoMail.Api/Program.cs`
- Create: `src/backend/NexoMail.CommercialSmokeTests/ImapProviderSmoke.cs`
- Modify: `src/backend/NexoMail.CommercialSmokeTests/Program.cs`

**Interfaces:**
- API: `POST /api/mail/accounts/imap/connect`.
- Produces: `ImapAccountService.ConnectAsync(ImapConnectionRequest, CancellationToken)`.
- Produces: `IMailProvider` con `ProviderType == MailProviderType.Imap`.
- No `IMailDraftProvider` for IMAP in this phase.

- [ ] **Step 1: Write failing validation smoke tests**

```csharp
Assert(ImapConnectionRequest.NormalizeSecurity("SSL/TLS") == "ssl", "SSL/TLS must normalize");
Assert(ImapConnectionRequest.NormalizeSecurity("STARTTLS") == "starttls", "STARTTLS must normalize");
AssertThrows(() => ImapConnectionRequest.ValidateHost("127.0.0.1"), "loopback must be rejected");
AssertThrows(() => ImapConnectionRequest.ValidatePort(0), "invalid port must be rejected");
```

- [ ] **Step 2: Verify tests fail**

Run: `dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj`
Expected: FAIL/compile error.

- [ ] **Step 3: Implement validated connection request**

```csharp
public sealed record ImapConnectionRequest(
    string EmailAddress, string DisplayName, string Username, string Password,
    string ImapHost, int ImapPort, string ImapSecurity,
    string SmtpHost, int SmtpPort, string SmtpSecurity);
```

Reject blank fields, invalid email/ports, IP literals, localhost and unsafe hostnames. Only allow `ssl` and `starttls` in Beta.

- [ ] **Step 4: Implement `ImapAccountService`**

Use `MailKit.Net.Imap.ImapClient` and `MailKit.Net.Smtp.SmtpClient` with explicit timeout. Validate both connections before opening a database transaction. After successful validation, enforce commercial account limit, create/reactivate `MailAccountEntity`, protect password with `ITokenProtector`, then upsert `ImapCredentialEntity`.

- [ ] **Step 5: Implement `ImapMailProvider`**

Resolve folders using `SpecialFolder` where available. Message IDs are encoded UIDs plus folder identity so an operation can reopen the correct message. Implement list/read/attachment/send/reply/reply-all/forward/read-state/move/trash/folders. `EmptyFolderAsync` may expunge Trash only after selecting the resolved trash folder. Do not persist bodies or attachment bytes.

- [ ] **Step 6: Register service and endpoint**

```csharp
builder.Services.AddScoped<ImapAccountService>();
builder.Services.AddScoped<ImapMailProvider>();
builder.Services.AddScoped<IMailProvider>(sp => new UserScopedMailProvider(sp.GetRequiredService<ImapMailProvider>(), sp.GetRequiredService<NexoMailDbContext>(), sp.GetRequiredService<IUserContext>()));
```

Expose authenticated CSRF-protected POST `/api/mail/accounts/imap/connect` returning the created account. Map authentication/TLS/connectivity failures to sanitized 400/502 responses.

- [ ] **Step 7: Run backend build and smoke tests**

Run: `dotnet build NexoMail.sln`
Run: `dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/backend
git commit -m "feat: add imap smtp beta provider"
```

---

### Task 4: Modal de proveedores y preparación de marcha blanca

**Files:**
- Modify: `src/frontend/src/pages/AccountsPage.tsx`
- Modify: `src/frontend/src/api/mailApi.ts`
- Create: `src/frontend/src/components/MailProviderLogo.tsx`
- Create: `src/frontend/src/styles/account-providers.css`
- Modify: `src/frontend/src/main.tsx`
- Create: `src/frontend/scripts/mail-provider-beta-smoke.mjs`
- Modify: `src/frontend/package.json`
- Modify: `.github/workflows/frontend-build.yml`
- Modify: `README.md`

**Interfaces:**
- UI button: `Agregar cuenta`.
- Modal options: Gmail, Microsoft 365, IMAP/SMTP Beta.
- `mailApi.connectImap(request)` POSTs to `/api/mail/accounts/imap/connect`.

- [ ] **Step 1: Write failing frontend smoke test**

```js
assert(accounts.includes('Agregar cuenta'))
assert(accounts.includes('Gmail / Google Workspace'))
assert(accounts.includes('Microsoft 365'))
assert(accounts.includes('IMAP / SMTP'))
assert(accounts.includes('autorización previa'))
assert(accounts.includes('/api/oauth/microsoft/start'))
assert(mailApi.includes('/api/mail/accounts/imap/connect'))
```

- [ ] **Step 2: Run it and verify failure**

Run: `node src/frontend/scripts/mail-provider-beta-smoke.mjs`
Expected: FAIL against the Gmail-only page.

- [ ] **Step 3: Implement provider modal**

Replace `Agregar Gmail` with `Agregar cuenta`. Modal cards use `MailProviderLogo` with local inline SVG/CSS rendering for Google/Microsoft/mail-server marks. Gmail redirects to `/api/oauth/google/start`, Microsoft to `/api/oauth/microsoft/start`, IMAP advances to a manual configuration form. Future providers are disabled and labeled `Próximamente`.

- [ ] **Step 4: Implement IMAP form**

Fields: email, visible name, username, password/application password, IMAP host/port/security, SMTP host/port/security. Defaults: 993 + SSL/TLS for IMAP and 587 + STARTTLS for SMTP. Primary action: `Probar y conectar`. Never echo password after submit.

- [ ] **Step 5: Add marcha blanca copy**

Show a discreet `Marcha blanca · 30 días` badge in account configuration and document required production settings in README. Do not globally clutter the UI.

- [ ] **Step 6: Run frontend tests/build**

Run: `cd src/frontend && pnpm install --frozen-lockfile`
Run: `node scripts/mail-provider-beta-smoke.mjs`
Run: `pnpm build`
Expected: PASS.

- [ ] **Step 7: Run full solution verification**

Run: `dotnet build NexoMail.sln`
Run: `dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj`
Run: `cd src/frontend && pnpm build`
Expected: all PASS.

- [ ] **Step 8: Commit**

```bash
git add src/frontend .github README.md
git commit -m "feat: add mail provider beta onboarding"
```
