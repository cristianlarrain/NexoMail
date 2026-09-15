# Unified Inbox Source of Truth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `MailMessageIndex` the reconciled operational mail universe used by Unified Inbox, Control Center and Nexo Intelligence, while providers remain authoritative for discovery, full-content retrieval and mutations.

**Architecture:** Gmail receives a complete metadata-only backfill plus incremental Gmail History synchronization and explicit reconciliation. Ordinary mailbox listing moves to a provider-independent `UnifiedInboxQueryService` only after parity is demonstrated. Microsoft Graph and IMAP remain behind the existing beta path until equivalent index ingestion exists; index-only production mode must not silently mix indexed Gmail with live non-Gmail accounts.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core 10, SQLite/SQL Server, Gmail REST API, React 19, TanStack Query, Vite 8, Node smoke scripts, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-15-unified-inbox-source-of-truth-design.md`

## Global Constraints

- Provider APIs remain the authoritative external source.
- `MailMessageIndex` becomes NexoMail's operational read source only after completeness/parity is proven.
- Message bodies and attachment bytes must never be persisted by this project.
- Every indexed message is unique by `(UserId, AccountId, ProviderMessageId)`.
- Gmail Point-1 validation covers the complete provider-visible mailbox history, not an arbitrary 90-day cutoff.
- The existing 90-day setting may remain only as the recent-refresh efficiency window; it must not define mailbox completeness.
- Provider outages must not erase already indexed mail.
- Stale, incomplete or failed synchronization must be observable and must never be represented as fresh/complete.
- Nexo Intelligence may classify, rank and filter indexed mail but may not determine whether a mail record exists.
- Full message body, thread body, attachment download, send, reply and mailbox mutation operations may remain provider-backed.
- Microsoft Graph and IMAP must not be enabled in index-only production mode until they implement the same normalized ingestion/reconciliation contract. During this plan they remain beta/non-cutover providers.
- TDD is mandatory: each implementation task starts RED, reaches GREEN, then commits before the next task.

---

### Task 1: Persist provider-independent mailbox membership

**Files:**
- Modify: `src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Google/ControlCenterIndexSchemaBootstrap.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexService.cs`
- Modify: `src/backend/NexoMail.ControlCenterSmokeTests/Program.cs`

**Interfaces:**
- Consumes: existing `MailMessageIndexEntity`, Gmail `labelIds`.
- Produces: `IsInbox`, `IsSent`, `IsDraft`, `IsSpam`, `IsTrash`, `IsUnread` on every indexed message.

- [ ] **Step 1: Write the failing entity contract**

```csharp
var indexed = new MailMessageIndexEntity();
Ensure(!indexed.IsSent && !indexed.IsDraft && !indexed.IsSpam && !indexed.IsTrash,
    "El índice debe exponer estados normalizados de carpeta.");
```

- [ ] **Step 2: Run RED**

```powershell
dotnet run --project src/backend/NexoMail.ControlCenterSmokeTests/NexoMail.ControlCenterSmokeTests.csproj
```

Expected: compile failure for the four missing properties.

- [ ] **Step 3: Add normalized fields**

```csharp
public bool IsSent { get; set; }
public bool IsDraft { get; set; }
public bool IsSpam { get; set; }
public bool IsTrash { get; set; }
```

Do not replace existing `GmailLabels`; provider-specific source metadata remains available for Gmail-only classification rules.

- [ ] **Step 4: Extend SQLite and SQL Server bootstrap**

SQLite:

```csharp
await EnsureSqliteColumnAsync(connection, "MailMessageIndex", "IsSent", "INTEGER NOT NULL DEFAULT 0", ct);
await EnsureSqliteColumnAsync(connection, "MailMessageIndex", "IsDraft", "INTEGER NOT NULL DEFAULT 0", ct);
await EnsureSqliteColumnAsync(connection, "MailMessageIndex", "IsSpam", "INTEGER NOT NULL DEFAULT 0", ct);
await EnsureSqliteColumnAsync(connection, "MailMessageIndex", "IsTrash", "INTEGER NOT NULL DEFAULT 0", ct);
```

SQL Server must add the same columns as `bit NOT NULL` with named default constraints.

- [ ] **Step 5: Populate flags during every Gmail upsert**

```csharp
entity.IsInbox = message.Labels.Contains("INBOX");
entity.IsSent = message.Labels.Contains("SENT");
entity.IsDraft = message.Labels.Contains("DRAFT");
entity.IsSpam = message.Labels.Contains("SPAM");
entity.IsTrash = message.Labels.Contains("TRASH");
entity.IsUnread = message.Labels.Contains("UNREAD");
```

- [ ] **Step 6: Add deterministic fake Gmail metadata cases**

Create separate fake messages that exercise each normalized label and assert persisted values after sync.

- [ ] **Step 7: Run GREEN**

```powershell
dotnet run --project src/backend/NexoMail.ControlCenterSmokeTests/NexoMail.ControlCenterSmokeTests.csproj
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs src/backend/NexoMail.Infrastructure/Google/ControlCenterIndexSchemaBootstrap.cs src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexService.cs src/backend/NexoMail.ControlCenterSmokeTests/Program.cs
git commit -m "feat: normalize indexed mailbox membership"
```

---

### Task 2: Add the provider-independent Unified Inbox query service

**Files:**
- Modify: `src/backend/NexoMail.Domain/MailModels.cs`
- Create: `src/backend/NexoMail.Infrastructure/Mail/UnifiedInboxQueryService.cs`
- Create: `src/backend/NexoMail.UnifiedInboxSmokeTests/NexoMail.UnifiedInboxSmokeTests.csproj`
- Create: `src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs`
- Modify: `NexoMail.sln`

**Interfaces:**

Add the exact reference DTO:

```csharp
public sealed record MailMessageReference(Guid AccountId, string ProviderMessageId);
```

`UnifiedInboxQueryService` produces:

```csharp
Task<PagedResult<MailSummary>> GetMessagesAsync(MailQuery query, CancellationToken cancellationToken);
Task<IReadOnlyCollection<MailSummary>> ResolveAsync(IReadOnlyCollection<MailMessageReference> references, CancellationToken cancellationToken);
```

- [ ] **Step 1: Create the smoke project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../NexoMail.Application/NexoMail.Application.csproj" />
    <ProjectReference Include="../NexoMail.Domain/NexoMail.Domain.csproj" />
    <ProjectReference Include="../NexoMail.Infrastructure/NexoMail.Infrastructure.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.0" />
  </ItemGroup>
</Project>
```

Then run:

```powershell
dotnet sln NexoMail.sln add src/backend/NexoMail.UnifiedInboxSmokeTests/NexoMail.UnifiedInboxSmokeTests.csproj
```

- [ ] **Step 2: Write RED scenarios over in-memory SQLite**

Seed two users, two accounts for the authenticated user and one account for the other user. Cover `inbox`, `archive`, `sent`, `drafts`, `spam`, `trash`, `ignored`, account filtering, text search and `is:unread`.

Core assertions:

```csharp
var first = await service.GetMessagesAsync(new MailQuery(null, "inbox", 2), ct);
Ensure(first.Items.Count == 2, "La primera página debe respetar take.");
Ensure(first.Items.All(x => allowedAccountIds.Contains(x.AccountId)), "No puede cruzar usuarios.");
Ensure(first.Items.SequenceEqual(first.Items.OrderByDescending(x => x.ReceivedAt)),
    "El orden debe ser global y descendente.");
```

- [ ] **Step 3: Run RED**

```powershell
dotnet run --project src/backend/NexoMail.UnifiedInboxSmokeTests/NexoMail.UnifiedInboxSmokeTests.csproj
```

Expected: compile failure because `UnifiedInboxQueryService` does not exist.

- [ ] **Step 4: Implement folder predicates**

Use exactly:

```text
inbox   = IsInbox && !IsSpam && !IsTrash
drafts  = IsDraft && !IsTrash
sent    = IsSent && !IsTrash
spam    = IsSpam
trash   = IsTrash
archive = !IsInbox && !IsSent && !IsDraft && !IsSpam && !IsTrash
```

`ignored` is an inbox subset whose `(AccountId, FromAddress)` matches `IgnoredSenders`. Ordinary inbox excludes those senders.

- [ ] **Step 5: Implement stable snapshot pagination**

Use:

```csharp
private sealed record InboxCursor(DateTimeOffset SnapshotAt, int Offset);
```

First page captures `SnapshotAt = DateTimeOffset.UtcNow`. Later pages reuse it, query `OccurredAt <= SnapshotAt`, order by `OccurredAt DESC`, `AccountId`, `ProviderMessageId`, apply `Skip(Offset)`, and fetch `Take + 1`. The next cursor increments offset by the number returned. Encode/decode as Base64Url JSON and reject malformed cursors with `InvalidOperationException("El cursor de bandeja no es válido.")`.

- [ ] **Step 6: Implement search and projection without provider calls**

Normal search covers `FromName`, `FromAddress`, `Subject`, `Snippet`. Exact query `is:unread` filters `IsUnread`.

Projection:

```csharp
new MailSummary(
    x.ProviderMessageId,
    x.AccountId,
    x.FromName,
    x.FromAddress,
    string.IsNullOrWhiteSpace(x.Subject) ? "(sin asunto)" : x.Subject,
    x.Snippet,
    x.OccurredAt,
    !x.IsUnread,
    x.HasAttachments,
    requestedFolder)
```

- [ ] **Step 7: Implement exact reference resolution**

`ResolveAsync` filters by authenticated `UserId` and returns only real indexed `(AccountId, ProviderMessageId)` rows. It never constructs fallback rows.

- [ ] **Step 8: Prove no skip/no duplicate pagination**

Load every page and assert:

```csharp
Ensure(keys.Count == keys.Distinct().Count(), "La paginación repitió mensajes.");
Ensure(keys.Count == expectedInboxCount, "La paginación omitió mensajes.");
```

- [ ] **Step 9: Run GREEN and commit**

```powershell
dotnet run --project src/backend/NexoMail.UnifiedInboxSmokeTests/NexoMail.UnifiedInboxSmokeTests.csproj
```

```bash
git add NexoMail.sln src/backend/NexoMail.Domain/MailModels.cs src/backend/NexoMail.Infrastructure/Mail/UnifiedInboxQueryService.cs src/backend/NexoMail.UnifiedInboxSmokeTests
git commit -m "feat: query unified inbox from mail index"
```

---

### Task 3: Complete the initial Gmail metadata backfill over the full mailbox

**Files:**
- Modify: `src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Google/ControlCenterIndexSchemaBootstrap.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexHostedService.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexService.cs`
- Modify: `src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs`

**Interfaces:**

Add to `MailIndexStateEntity`:

```csharp
public string? BackfillPageToken { get; set; }
public DateTimeOffset? BackfillStartedAt { get; set; }
public DateTimeOffset? BackfillCompletedAt { get; set; }
```

`WindowDays` remains the recent-refresh efficiency window; it no longer defines completeness.

- [ ] **Step 1: Write RED three-page backfill test**

Fake Gmail `users/me/messages` returns three pages when called with no date query and `includeSpamTrash=true`. Set the per-cycle metadata budget so one synchronization pass cannot consume all pages. Assert `BackfillCompletedAt` stays null until the final page.

- [ ] **Step 2: Run RED**

```powershell
dotnet run --project src/backend/NexoMail.UnifiedInboxSmokeTests/NexoMail.UnifiedInboxSmokeTests.csproj
```

- [ ] **Step 3: Persist backfill checkpoint columns**

`BackfillPageToken` must be nullable text/nvarchar(1024); timestamps are nullable ISO text in SQLite and `datetimeoffset` in SQL Server.

- [ ] **Step 4: Separate recent refresh budget from full backfill**

Use these option defaults:

```csharp
public int WindowDays { get; set; } = 90;
public int RefreshNewestCount { get; set; } = 300;
public int BackfillPageSize { get; set; } = 500;
public int MaxMetadataLoadsPerCycle { get; set; } = 1500;
```

`WindowDays` applies only to recent refresh. Full backfill uses no age query.

- [ ] **Step 5: Add paged full-mailbox ID listing**

```csharp
private sealed record GmailMessageIdPage(IReadOnlyCollection<string> Ids, string? NextPageToken);
```

Request `users/me/messages?includeSpamTrash=true&maxResults=<pageSize>` plus `pageToken` when present. Do not include `newer_than`, `after`, folder labels or category filters in the full-backfill listing.

- [ ] **Step 6: Persist progress after each successful page**

At first full backfill set `BackfillStartedAt`. After each page, persist `NextPageToken`. When `NextPageToken` is null, clear `BackfillPageToken` and set `BackfillCompletedAt`.

- [ ] **Step 7: Preserve fast recent/unread refresh on every cycle**

Recent and previously unread IDs are refreshed first; then remaining cycle budget is used for full backfill. Existing lease semantics remain unchanged.

- [ ] **Step 8: Prove full provider ID coverage GREEN**

After enough cycles:

```csharp
Ensure(state.BackfillCompletedAt is not null, "El backfill debe declarar finalización.");
Ensure(indexedIds.SetEquals(allProviderIds), "El índice completo debe contener todos los IDs del proveedor.");
```

- [ ] **Step 9: Commit**

```bash
git add src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs src/backend/NexoMail.Infrastructure/Google/ControlCenterIndexSchemaBootstrap.cs src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexHostedService.cs src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexService.cs src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs
git commit -m "feat: backfill complete gmail metadata history"
```

---

### Task 4: Add Gmail History incremental synchronization after backfill

**Files:**
- Modify: `src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Google/ControlCenterIndexSchemaBootstrap.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexService.cs`
- Modify: `src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs`

**Interfaces:**

Add to `MailIndexStateEntity`:

```csharp
public string? GmailHistoryId { get; set; }
```

- [ ] **Step 1: Write RED history-change test**

After a completed backfill, fake Gmail history must contain: one new message, one label change on an old message, and one deleted message. Assert a subsequent sync updates all three without rescanning the complete mailbox.

- [ ] **Step 2: Run RED**

Expected: history assertions fail because no history checkpoint exists.

- [ ] **Step 3: Capture the initial Gmail history checkpoint**

At successful backfill completion, request `users/me/profile` and persist `historyId` in `GmailHistoryId`.

- [ ] **Step 4: Implement paged `users.history.list`**

Request from the persisted `startHistoryId`, page through every returned history page, collect changed message IDs from `messagesAdded`, `labelsAdded`, `labelsRemoved`, and deleted IDs from `messagesDeleted`.

- [ ] **Step 5: Refresh changed IDs and remove confirmed deleted IDs**

For changed IDs, load current metadata and upsert through the same writer. For a `messagesDeleted` ID, delete only the indexed row with the same authenticated user/account/message key and its indexed attachment metadata. This deletion is provider-confirmed, not inferred from local absence.

- [ ] **Step 6: Advance checkpoint only after successful page processing**

Persist the newest returned `historyId` after the corresponding changes are committed. A failed cycle must not jump over unprocessed history.

- [ ] **Step 7: Handle expired history checkpoints safely**

If Gmail rejects `startHistoryId` as too old, clear `GmailHistoryId`, mark the account stale, and require reconciliation/full refresh before claiming freshness. Do not delete existing index rows.

- [ ] **Step 8: Run GREEN and commit**

```powershell
dotnet run --project src/backend/NexoMail.UnifiedInboxSmokeTests/NexoMail.UnifiedInboxSmokeTests.csproj
```

```bash
git add src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs src/backend/NexoMail.Infrastructure/Google/ControlCenterIndexSchemaBootstrap.cs src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexService.cs src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs
git commit -m "feat: sync gmail index from history changes"
```

---

### Task 5: Add provider-to-index reconciliation and repair

**Files:**
- Modify: `src/backend/NexoMail.Domain/MailModels.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Google/ControlCenterIndexSchemaBootstrap.cs`
- Create: `src/backend/NexoMail.Infrastructure/Google/GmailIndexReconciliationService.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexService.cs`
- Modify: `src/backend/NexoMail.Api/MetadataIndexEndpoints.cs`
- Modify: `src/backend/NexoMail.Api/MailProviderBetaModule.cs`
- Modify: `src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs`

**Interfaces:**

```csharp
public sealed record MailIndexReconciliationResult(
    Guid AccountId,
    int ProviderCount,
    int IndexedCount,
    IReadOnlyCollection<string> MissingProviderIds,
    IReadOnlyCollection<string> OrphanIndexedIds,
    int DuplicateIndexedIds,
    int Repaired,
    DateTimeOffset ReconciledAt,
    bool IsHealthy);
```

```csharp
Task<MailIndexReconciliationResult> ReconcileAsync(
    Guid accountId,
    bool repairMissing,
    CancellationToken cancellationToken);
```

Add to `MailIndexStateEntity`:

```csharp
public DateTimeOffset? LastReconciledAt { get; set; }
public string? LastReconciliationErrorCode { get; set; }
```

- [ ] **Step 1: Write RED mismatch scenario**

Provider IDs are `A,B,C`; index contains `A,B,ORPHAN`. First pass with `repairMissing:false` must report missing `C`, orphan `ORPHAN`, unhealthy status and zero duplicates.

- [ ] **Step 2: Run RED**

```powershell
dotnet run --project src/backend/NexoMail.UnifiedInboxSmokeTests/NexoMail.UnifiedInboxSmokeTests.csproj
```

- [ ] **Step 3: Reuse Gmail authentication/listing from the metadata service**

Expose focused internal helpers from `GmailMetadataIndexService` rather than duplicating token refresh code:

```csharp
internal Task<IReadOnlyCollection<string>> ListAllProviderMessageIdsAsync(Guid accountId, CancellationToken ct);
internal Task<int> RepairMissingMessageIdsAsync(Guid userId, Guid accountId, IReadOnlyCollection<string> ids, CancellationToken ct);
```

The full ID enumeration uses `includeSpamTrash=true` and no age/folder filter.

- [ ] **Step 4: Compare sets without deleting orphans**

```csharp
var missing = providerIds.Except(indexedIds, StringComparer.Ordinal).Order().ToArray();
var orphan = indexedIds.Except(providerIds, StringComparer.Ordinal).Order().ToArray();
```

Count duplicates from raw indexed rows before converting to a set. Unique DB constraints should keep this at zero; the diagnostic still verifies it.

- [ ] **Step 5: Repair missing IDs idempotently**

When `repairMissing=true`, load/upsert only missing provider IDs, then recalculate result. Never auto-delete `OrphanIndexedIds` from reconciliation alone.

- [ ] **Step 6: Persist reconciliation status**

On success set `LastReconciledAt` and clear error. On failure set `LastReconciliationErrorCode="reconcile_error"` without altering existing indexed mail.

- [ ] **Step 7: Register and expose authorized diagnostic endpoint**

In `MailProviderBetaModule.AddServices`:

```csharp
services.AddScoped<GmailMetadataIndexService>();
services.AddScoped<GmailIndexReconciliationService>();
```

In `MetadataIndexEndpoints.Map`:

```csharp
mail.MapPost("/index/reconcile/{accountId:guid}", async (
    Guid accountId,
    bool? repair,
    GmailIndexReconciliationService service,
    CancellationToken ct) =>
    Results.Ok(await service.ReconcileAsync(accountId, repair ?? true, ct)));
```

The service verifies `MailAccount.UserId == IUserContext.UserId`, `IsActive`, and `Provider == Gmail` before provider access.

- [ ] **Step 8: Prove second reconciliation is a no-op**

```csharp
Ensure(second.MissingProviderIds.Count == 0, "No deben quedar mensajes faltantes.");
Ensure(second.DuplicateIndexedIds == 0, "La reparación no puede duplicar mensajes.");
Ensure(second.Repaired == 0, "La segunda reconciliación debe ser idempotente.");
```

- [ ] **Step 9: Commit**

```bash
git add src/backend/NexoMail.Domain/MailModels.cs src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs src/backend/NexoMail.Infrastructure/Google/ControlCenterIndexSchemaBootstrap.cs src/backend/NexoMail.Infrastructure/Google/GmailIndexReconciliationService.cs src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexService.cs src/backend/NexoMail.Api/MetadataIndexEndpoints.cs src/backend/NexoMail.Api/MailProviderBetaModule.cs src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs
git commit -m "feat: reconcile gmail provider with mail index"
```

---

### Task 6: Keep indexed state convergent after provider mutations

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/Mail/MailIndexMutationService.cs`
- Modify: `src/backend/NexoMail.Api/Program.cs`
- Modify: `src/backend/NexoMail.Api/DraftEndpoints.cs`
- Modify: `src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs`

**Interfaces:**

```csharp
Task MarkReadAsync(Guid accountId, string providerMessageId, bool read, CancellationToken cancellationToken);
Task MoveAsync(Guid accountId, string providerMessageId, string folderId, CancellationToken cancellationToken);
Task MarkAccountForImmediateSyncAsync(Guid accountId, CancellationToken cancellationToken);
```

- [ ] **Step 1: Write RED mutation tests**

```csharp
await mutation.MarkReadAsync(accountId, messageId, true, ct);
Ensure(!row.IsUnread, "Marcar leído debe actualizar el índice.");

await mutation.MoveAsync(accountId, messageId, "trash", ct);
Ensure(row.IsTrash && !row.IsInbox, "Mover a Papelera debe actualizar el índice.");
```

Cover `archive`, `spam`, `trash`, `inbox`, read and unread.

- [ ] **Step 2: Run RED**

- [ ] **Step 3: Implement local transitions**

```text
read=true  -> IsUnread=false
read=false -> IsUnread=true
archive    -> IsInbox=false
spam       -> IsSpam=true,  IsInbox=false, IsTrash=false
trash      -> IsTrash=true, IsInbox=false, IsSpam=false
inbox      -> IsInbox=true, IsTrash=false, IsSpam=false
```

Every changed row updates `IndexedAt`.

- [ ] **Step 4: Wire only after provider success**

Provider mutation executes first. Index mutation executes only if the provider operation completed successfully. Provider exceptions leave indexed state unchanged.

- [ ] **Step 5: Handle send/reply without fabricated IDs**

After successful send/reply/draft-send, call `MarkAccountForImmediateSyncAsync`. It sets `LastIndexedAt = DateTimeOffset.UnixEpoch` and clears transient sync error state. It does not insert a synthetic sent message.

- [ ] **Step 6: Run GREEN and commit**

```powershell
dotnet run --project src/backend/NexoMail.UnifiedInboxSmokeTests/NexoMail.UnifiedInboxSmokeTests.csproj
```

```bash
git add src/backend/NexoMail.Infrastructure/Mail/MailIndexMutationService.cs src/backend/NexoMail.Api/Program.cs src/backend/NexoMail.Api/DraftEndpoints.cs src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs
git commit -m "feat: converge mail index after provider mutations"
```

---

### Task 7: Add a safe Gmail index-read cutover gate and switch `/mail/messages`

**Files:**
- Modify: `src/backend/NexoMail.Api/appsettings.json`
- Modify: `src/backend/NexoMail.Api/MailProviderBetaModule.cs`
- Modify: `src/backend/NexoMail.Api/Program.cs`
- Modify: `src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs`

**Interfaces:**

Add configuration:

```json
"UnifiedInbox": {
  "IndexReadEnabled": false,
  "RequireCompleteBackfill": true
}
```

Create in `MailProviderBetaModule.cs` or a focused options file:

```csharp
public sealed class UnifiedInboxOptions
{
    public const string SectionName = "UnifiedInbox";
    public bool IndexReadEnabled { get; set; }
    public bool RequireCompleteBackfill { get; set; } = true;
}
```

- [ ] **Step 1: Write RED readiness test**

When index-read mode is enabled, an authenticated user with an active MicrosoftGraph or Imap account must fail readiness rather than silently receive a partial Gmail-only unified inbox. A Gmail account with null `BackfillCompletedAt` must also fail readiness when `RequireCompleteBackfill=true`.

- [ ] **Step 2: Run RED**

- [ ] **Step 3: Register query/mutation services and options**

```csharp
services.Configure<UnifiedInboxOptions>(configuration.GetSection(UnifiedInboxOptions.SectionName));
services.AddScoped<UnifiedInboxQueryService>();
services.AddScoped<MailIndexMutationService>();
```

- [ ] **Step 4: Implement `UnifiedInboxReadinessService`**

Create `src/backend/NexoMail.Infrastructure/Mail/UnifiedInboxReadinessService.cs` with:

```csharp
Task<UnifiedInboxReadiness> GetAsync(CancellationToken cancellationToken);
```

where:

```csharp
public sealed record UnifiedInboxReadiness(
    bool IsReady,
    IReadOnlyCollection<Guid> IncompleteAccountIds,
    IReadOnlyCollection<Guid> UnsupportedProviderAccountIds);
```

Gmail is supported in this plan. `MicrosoftGraph` and `Imap` are unsupported for index-only cutover until their ingestion adapters exist.

- [ ] **Step 5: Switch ordinary list reads only when the gate is enabled and ready**

When `IndexReadEnabled=false`, retain the existing provider path solely as migration/shadow behavior. When enabled, require `readiness.IsReady`; otherwise return HTTP 409 with a clear synchronization/provider-readiness error rather than silently mixing data sources.

When ready, `/mail/messages` constructs `MailQuery` and calls only `UnifiedInboxQueryService.GetMessagesAsync`.

- [ ] **Step 6: Add exact reference resolver endpoint**

```csharp
mail.MapPost("/messages/resolve", async (
    MailMessageReference[] references,
    UnifiedInboxQueryService inbox,
    CancellationToken ct) =>
    Results.Ok(await inbox.ResolveAsync(references, ct)));
```

- [ ] **Step 7: Change `/mail/refresh` semantics in index mode**

Mark Gmail account states for immediate synchronization without clearing indexed rows. In migration mode, existing cache invalidation may remain alongside sync intent until the live list path is removed.

- [ ] **Step 8: Run backend GREEN**

```powershell
dotnet build NexoMail.sln
dotnet run --project src/backend/NexoMail.UnifiedInboxSmokeTests/NexoMail.UnifiedInboxSmokeTests.csproj
dotnet run --project src/backend/NexoMail.ControlCenterSmokeTests/NexoMail.ControlCenterSmokeTests.csproj
dotnet run --project src/backend/NexoMail.IntelligenceSmokeTests/NexoMail.IntelligenceSmokeTests.csproj
```

- [ ] **Step 9: Commit**

```bash
git add src/backend/NexoMail.Api/appsettings.json src/backend/NexoMail.Api/MailProviderBetaModule.cs src/backend/NexoMail.Api/Program.cs src/backend/NexoMail.Infrastructure/Mail/UnifiedInboxReadinessService.cs src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs
git commit -m "feat: gate unified inbox index reads"
```

---

### Task 8: Remove the synthetic priority mailbox from React

**Files:**
- Modify: `src/frontend/src/api/mailApi.ts`
- Modify: `src/frontend/src/pages/InboxPage.tsx`
- Create: `src/frontend/scripts/unified-inbox-source-smoke.mjs`

**Interfaces:**

```ts
resolveMessages: (references: Array<{ accountId: string; providerMessageId: string }>) =>
  api<MailSummary[]>('/mail/messages/resolve', {
    method: 'POST',
    body: JSON.stringify(references),
  })
```

- [ ] **Step 1: Write RED frontend source test**

```js
if (source.includes("senderName: reference.counterpart")) {
  throw new Error('La vista prioritaria todavía fabrica MailSummary fuera del índice.')
}
if (!source.includes('mailApi.resolveMessages')) {
  throw new Error('La vista prioritaria debe resolver mensajes desde el índice.')
}
```

- [ ] **Step 2: Run RED**

```powershell
node src/frontend/scripts/unified-inbox-source-smoke.mjs
```

- [ ] **Step 3: Add `mailApi.resolveMessages`**

Use the exact interface above.

- [ ] **Step 4: Replace synthetic fallback rows**

Priority selection may continue using Control Center/tracking metadata in Point 1, but convert those items to `MailMessageReference[]` and resolve the actual display rows from `/mail/messages/resolve`. Remove the object literal that fabricates sender, preview, read and attachment values.

- [ ] **Step 5: Keep ordinary folder/search pages on `mailApi.messages`**

Once index-read mode is enabled, both paths ultimately resolve real `MailMessageIndex` rows.

- [ ] **Step 6: Run GREEN**

```powershell
node src/frontend/scripts/unified-inbox-source-smoke.mjs
cd src/frontend
pnpm build
```

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/api/mailApi.ts src/frontend/src/pages/InboxPage.tsx src/frontend/scripts/unified-inbox-source-smoke.mjs
git commit -m "refactor: resolve priority mail from unified index"
```

---

### Task 9: Prove cross-module invariants, outages and parity

**Files:**
- Modify: `src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs`
- Modify: `src/backend/NexoMail.ControlCenterSmokeTests/Program.cs`
- Modify: `src/backend/NexoMail.IntelligenceSmokeTests/Program.cs`
- Create: `.github/workflows/unified-inbox-smoke.yml`

**Interfaces:**
- Produces automated evidence for the Point-1 acceptance gate.

- [ ] **Step 1: Assert every Control Center item exists in the index**

```csharp
Ensure(await database.MailMessageIndex.AsNoTracking().AnyAsync(x =>
    x.UserId == userId &&
    x.AccountId == item.AccountId &&
    x.ProviderMessageId == item.MessageId, ct),
    $"Control Center referencia un mensaje inexistente en el índice: {item.AccountId}/{item.MessageId}");
```

- [ ] **Step 2: Assert every Intelligence conversation is backed by indexed messages**

For each Intelligence result, assert at least one row for the authenticated user, account and thread/conversation key used by the adapter.

- [ ] **Step 3: Prove provider outage does not erase mail**

Seed indexed rows, make provider HTTP throw, run ordinary indexed inbox read and assert all seeded rows remain visible. Separately assert `MailIndexState.LastSyncErrorCode` exposes the provider failure.

- [ ] **Step 4: Prove partial account failure isolation**

One Gmail account returns authentication failure; another synchronizes. Previously indexed mail from both remains queryable and the healthy account advances its sync state.

- [ ] **Step 5: Prove final provider/index parity**

For the deterministic validation mailbox:

```csharp
Ensure(result.MissingProviderIds.Count == 0, "Faltan mensajes del proveedor.");
Ensure(result.DuplicateIndexedIds == 0, "Existen mensajes duplicados.");
Ensure(result.OrphanIndexedIds.Count == 0, "Existen huérfanos no explicados.");
Ensure(result.IsHealthy, "La reconciliación final debe quedar saludable.");
```

- [ ] **Step 6: Add the CI workflow**

```yaml
name: Unified Inbox source-of-truth smoke

on:
  push:
    branches: [main, 'feature/**']
  pull_request:
    branches: [main]
  workflow_dispatch:

jobs:
  verify:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - uses: pnpm/action-setup@v4
        with:
          version: 10
      - uses: actions/setup-node@v4
        with:
          node-version: 22
          cache: pnpm
          cache-dependency-path: src/frontend/pnpm-lock.yaml
      - run: dotnet run --project src/backend/NexoMail.UnifiedInboxSmokeTests/NexoMail.UnifiedInboxSmokeTests.csproj --configuration Release
      - run: dotnet run --project src/backend/NexoMail.ControlCenterSmokeTests/NexoMail.ControlCenterSmokeTests.csproj --configuration Release
      - run: dotnet run --project src/backend/NexoMail.IntelligenceSmokeTests/NexoMail.IntelligenceSmokeTests.csproj --configuration Release
      - run: dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj --configuration Release
      - run: dotnet run --project src/backend/NexoMail.SmokeTests/NexoMail.SmokeTests.csproj --configuration Release
      - run: node src/frontend/scripts/unified-inbox-source-smoke.mjs
      - run: pnpm install --frozen-lockfile
        working-directory: src/frontend
      - run: pnpm build
        working-directory: src/frontend
```

- [ ] **Step 7: Run the full local gate**

```powershell
dotnet build NexoMail.sln
dotnet run --project src/backend/NexoMail.UnifiedInboxSmokeTests/NexoMail.UnifiedInboxSmokeTests.csproj
dotnet run --project src/backend/NexoMail.ControlCenterSmokeTests/NexoMail.ControlCenterSmokeTests.csproj
dotnet run --project src/backend/NexoMail.IntelligenceSmokeTests/NexoMail.IntelligenceSmokeTests.csproj
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
dotnet run --project src/backend/NexoMail.SmokeTests/NexoMail.SmokeTests.csproj
node src/frontend/scripts/unified-inbox-source-smoke.mjs
cd src/frontend
pnpm build
```

- [ ] **Step 8: Verify read boundaries by source search**

Expected after enabling the gate for a Gmail-only ready user:

```text
/api/mail/messages -> UnifiedInboxQueryService -> MailMessageIndex
Control Center -> MailMessageIndex
Nexo Intelligence -> MailMessageIndex
```

`IMailGateway.GetMessagesAsync` remains only in disabled migration/shadow mode and provider-specific diagnostics, not the enabled index-only ordinary list path.

- [ ] **Step 9: Commit**

```bash
git add src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs src/backend/NexoMail.ControlCenterSmokeTests/Program.cs src/backend/NexoMail.IntelligenceSmokeTests/Program.cs .github/workflows/unified-inbox-smoke.yml
git commit -m "test: enforce unified mail source invariants"
```

---

## Point 1 Acceptance Gate

Point 1 is approved for the initial Gmail production scope only when all conditions below are true:

1. Full Gmail metadata backfill is complete and persisted; no age cutoff is used to define completeness.
2. Gmail History incremental synchronization updates adds, label changes and provider-confirmed deletions after backfill.
3. Reconciliation reports `missing = 0`, `duplicates = 0`, and no unexplained orphan rows for validation accounts.
4. `MailMessageIndex` has provider-independent mailbox/read membership sufficient for Unified Inbox queries.
5. Enabled `/api/mail/messages` reads `UnifiedInboxQueryService`, not live Gmail listing.
6. Full body/thread/attachment retrieval remains provider-backed and is not persisted.
7. Read/move/trash mutations converge indexed state only after provider success.
8. Send/reply trigger immediate synchronization intent without fabricated sent IDs.
9. Provider outage keeps previously indexed mail visible and exposes stale/error state.
10. Priority UI never fabricates `MailSummary`; it resolves real indexed rows.
11. Every Control Center item and every Nexo Intelligence conversation is backed by the same indexed universe.
12. Unified Inbox, Control Center, Intelligence, Commercial, Draft lifecycle and frontend build checks are green.
13. No message body or attachment bytes are newly persisted.
14. Microsoft Graph and IMAP remain disabled from index-only production mode until equivalent ingestion/reconciliation adapters are implemented and validated; their presence must make the readiness gate fail rather than silently producing a partial inbox.
