# Unified Inbox Source of Truth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `MailMessageIndex` the reconciled operational mail universe used by Unified Inbox, Control Center and Nexo Intelligence, with provider-backed mutations and explicit freshness/reconciliation state.

**Architecture:** Provider APIs remain authoritative externally, but ordinary NexoMail list/read productivity views consume one normalized local index. Gmail synchronization must completely backfill the configured scope, reconciliation must prove provider/index parity, and frontend priority views must resolve messages from that same indexed universe rather than synthesize a second mailbox.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core 10, SQLite/SQL Server, Gmail REST API, React 19, TanStack Query, Vite 8, Node smoke scripts, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-15-unified-inbox-source-of-truth-design.md`

## Global Constraints

- The provider remains the external source of truth; the reconciled index is NexoMail's operational source of truth.
- Message bodies and attachment bytes must not be persisted by this project.
- Every indexed message is unique by `(UserId, AccountId, ProviderMessageId)`.
- `/mail/messages`, Control Center and Nexo Intelligence must operate over the same indexed universe before Point 1 is approved.
- Provider outages must not erase already indexed mail.
- Stale, incomplete or failed synchronization must be observable and must never be represented as fresh/complete.
- The first production synchronized scope is 90 days; completeness means all provider message IDs inside that 90-day scope, not merely the first N messages.
- Intelligent filtering may select or rank indexed mail but may not invent a mail record or determine whether a message exists.
- Full body/thread/attachment retrieval and mailbox mutations may continue calling the provider.
- Implementation is TDD-first; each task starts RED and ends GREEN before the next task.

---

### Task 1: Persist provider-independent mailbox membership

**Files:**
- Modify: `src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Google/ControlCenterIndexSchemaBootstrap.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexService.cs`
- Modify: `src/backend/NexoMail.ControlCenterSmokeTests/Program.cs`

**Interfaces:**
- Consumes: existing `MailMessageIndexEntity` and Gmail label metadata.
- Produces: normalized boolean membership fields `IsInbox`, `IsSent`, `IsDraft`, `IsSpam`, `IsTrash`, plus existing `IsUnread`.

- [ ] **Step 1: Add failing schema/entity assertions**

Add assertions to the existing control-center smoke regression helper or `Program.cs` proving `MailMessageIndexEntity` exposes all normalized membership flags:

```csharp
var entity = new MailMessageIndexEntity();
Ensure(!entity.IsSent && !entity.IsDraft && !entity.IsSpam && !entity.IsTrash,
    "Los estados normalizados deben existir y partir en false.");
```

- [ ] **Step 2: Run the existing smoke suite and verify RED**

Run:

```powershell
dotnet run --project src/backend/NexoMail.ControlCenterSmokeTests/NexoMail.ControlCenterSmokeTests.csproj
```

Expected: compile failure because `IsSent`, `IsDraft`, `IsSpam` and `IsTrash` do not yet exist.

- [ ] **Step 3: Add normalized fields and EF mapping**

Extend `MailMessageIndexEntity` with:

```csharp
public bool IsSent { get; set; }
public bool IsDraft { get; set; }
public bool IsSpam { get; set; }
public bool IsTrash { get; set; }
```

Keep the existing unique index on `(UserId, AccountId, ProviderMessageId)` and indexes on user/timestamp/thread.

- [ ] **Step 4: Make bootstrap safe for SQLite and SQL Server**

Add idempotent columns to `ControlCenterIndexSchemaBootstrap.EnsureAsync`:

```csharp
await EnsureSqliteColumnAsync(connection, "MailMessageIndex", "IsSent", "INTEGER NOT NULL DEFAULT 0", ct);
await EnsureSqliteColumnAsync(connection, "MailMessageIndex", "IsDraft", "INTEGER NOT NULL DEFAULT 0", ct);
await EnsureSqliteColumnAsync(connection, "MailMessageIndex", "IsSpam", "INTEGER NOT NULL DEFAULT 0", ct);
await EnsureSqliteColumnAsync(connection, "MailMessageIndex", "IsTrash", "INTEGER NOT NULL DEFAULT 0", ct);
```

SQL Server equivalents must use `bit NOT NULL` with deterministic default constraints.

- [ ] **Step 5: Populate normalized membership during Gmail upsert**

In `GmailMetadataIndexService.UpsertAccountIndexAsync`, derive fields only from source labels:

```csharp
entity.IsInbox = message.Labels.Contains("INBOX");
entity.IsSent = message.Labels.Contains("SENT");
entity.IsDraft = message.Labels.Contains("DRAFT");
entity.IsSpam = message.Labels.Contains("SPAM");
entity.IsTrash = message.Labels.Contains("TRASH");
entity.IsUnread = message.Labels.Contains("UNREAD");
```

- [ ] **Step 6: Add a Gmail metadata fixture containing all relevant labels**

The fake Gmail response must exercise `INBOX`, `SENT`, `DRAFT`, `SPAM`, `TRASH`, and `UNREAD` across separate messages and assert the normalized flags after sync.

- [ ] **Step 7: Run smoke suite GREEN**

Run the same `dotnet run` command. Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs src/backend/NexoMail.Infrastructure/Google/ControlCenterIndexSchemaBootstrap.cs src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexService.cs src/backend/NexoMail.ControlCenterSmokeTests/Program.cs
git commit -m "feat: normalize indexed mailbox membership"
```

---

### Task 2: Add an index-backed Unified Inbox query service

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/Mail/UnifiedInboxQueryService.cs`
- Create: `src/backend/NexoMail.UnifiedInboxSmokeTests/NexoMail.UnifiedInboxSmokeTests.csproj`
- Create: `src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs`
- Modify: `NexoMail.sln`

**Interfaces:**
- Consumes: `NexoMailDbContext`, `IUserContext`, `MailQuery`.
- Produces:

```csharp
Task<PagedResult<MailSummary>> GetMessagesAsync(MailQuery query, CancellationToken cancellationToken);
Task<IReadOnlyCollection<MailSummary>> ResolveAsync(IReadOnlyCollection<MailMessageReference> references, CancellationToken cancellationToken);
```

Add to `MailModels.cs` in this task if needed by the resolver:

```csharp
public sealed record MailMessageReference(Guid AccountId, string ProviderMessageId);
```

- [ ] **Step 1: Scaffold the executable smoke project**

Use the existing smoke-test pattern:

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

Add the project to `NexoMail.sln` using `dotnet sln NexoMail.sln add ...` so configuration GUIDs are generated correctly.

- [ ] **Step 2: Write RED smoke scenarios**

Create in-memory SQLite data for two active accounts and assert:

```csharp
var page = await service.GetMessagesAsync(new MailQuery(null, "inbox", 2), ct);
Ensure(page.Items.Count == 2, "La bandeja unificada debe paginar sobre ambas cuentas.");
Ensure(page.Items.Select(x => x.AccountId).Distinct().Count() == 2,
    "La primera página debe poder combinar cuentas sin colisiones.");
Ensure(page.Items.SequenceEqual(page.Items.OrderByDescending(x => x.ReceivedAt)),
    "El orden global debe ser cronológico descendente.");
```

Also add scenarios for account filter, `inbox`, `archive`, `sent`, `drafts`, `spam`, `trash`, `ignored`, search and unread search (`is:unread`).

- [ ] **Step 3: Run the new smoke project and verify RED**

```powershell
dotnet run --project src/backend/NexoMail.UnifiedInboxSmokeTests/NexoMail.UnifiedInboxSmokeTests.csproj
```

Expected: compile failure because `UnifiedInboxQueryService` does not exist.

- [ ] **Step 4: Implement folder semantics from normalized fields**

Use these exact predicates:

```csharp
"inbox" => x.IsInbox && !x.IsSpam && !x.IsTrash,
"sent" => x.IsSent && !x.IsTrash,
"drafts" => x.IsDraft && !x.IsTrash,
"spam" => x.IsSpam,
"trash" => x.IsTrash,
"archive" => !x.IsInbox && !x.IsSent && !x.IsDraft && !x.IsSpam && !x.IsTrash
```

For `ignored`, require inbox membership and sender membership in `IgnoredSenders`; for ordinary `inbox`, exclude ignored senders.

- [ ] **Step 5: Implement stable snapshot cursor pagination**

Use an opaque Base64Url cursor containing a stable snapshot cutoff plus offset:

```csharp
private sealed record InboxCursor(DateTimeOffset SnapshotAt, int Offset);
```

First page sets `SnapshotAt = DateTimeOffset.UtcNow`; later pages reuse it. Query only rows with `OccurredAt <= SnapshotAt`, order by `OccurredAt DESC`, `AccountId`, `ProviderMessageId`, then `Skip(cursor.Offset).Take(take + 1)`. Encode the next cursor only when the extra row exists.

- [ ] **Step 6: Implement search and DTO projection**

Search must match sender name, sender address, subject and snippet case-insensitively. `is:unread` maps to `IsUnread`. Project without provider calls:

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

`ResolveAsync` must filter by authenticated `UserId` and resolve only `(AccountId, ProviderMessageId)` pairs passed by the caller. It must never create fallback/synthetic `MailSummary` values.

- [ ] **Step 8: Prove stable pagination and isolation GREEN**

Tests must load all pages and assert:

```csharp
Ensure(allKeys.Count == allKeys.Distinct().Count(), "La paginación no puede repetir mensajes.");
Ensure(allKeys.Count == expectedCount, "La paginación no puede omitir mensajes.");
Ensure(!allKeys.Any(key => key.StartsWith(otherUserAccountId.ToString(), StringComparison.OrdinalIgnoreCase)),
    "La bandeja no puede cruzar usuarios.");
```

- [ ] **Step 9: Commit**

```bash
git add NexoMail.sln src/backend/NexoMail.Domain/MailModels.cs src/backend/NexoMail.Infrastructure/Mail/UnifiedInboxQueryService.cs src/backend/NexoMail.UnifiedInboxSmokeTests
git commit -m "feat: query unified inbox from mail index"
```

---

### Task 3: Make the 90-day synchronized scope complete rather than capped

**Files:**
- Modify: `src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Google/ControlCenterIndexSchemaBootstrap.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexHostedService.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexService.cs`
- Modify: `src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs`

**Interfaces:**
- Produces persistent backfill state on `MailIndexStateEntity`:

```csharp
public DateTimeOffset? ScopeStartAt { get; set; }
public string? BackfillPageToken { get; set; }
public DateTimeOffset? BackfillCompletedAt { get; set; }
```

- [ ] **Step 1: Add RED test for mailbox with more IDs than one sync cycle**

Fake Gmail must return at least three list pages for the 90-day query. Configure the per-cycle work budget lower than the total, run synchronization repeatedly, and assert `BackfillCompletedAt` remains null until the last provider page is consumed.

- [ ] **Step 2: Run Unified Inbox smoke RED**

```powershell
dotnet run --project src/backend/NexoMail.UnifiedInboxSmokeTests/NexoMail.UnifiedInboxSmokeTests.csproj
```

Expected: new completeness assertions fail.

- [ ] **Step 3: Persist backfill state**

Add the three state fields to EF and both schema bootstrap providers. `BackfillPageToken` must allow at least 1024 characters.

- [ ] **Step 4: Separate completeness from work budget**

Change `GmailMetadataIndexSyncOptions` so the production defaults remain a 90-day scope while cycle limits only control work per pass:

```csharp
public int WindowDays { get; set; } = 90;
public int RefreshNewestCount { get; set; } = 300;
public int BackfillPageSize { get; set; } = 500;
public int MaxMetadataLoadsPerCycle { get; set; } = 1500;
```

Do not use a message-count ceiling to define whether the synchronized scope is complete.

- [ ] **Step 5: Add paged Gmail ID listing**

Introduce a private result type:

```csharp
private sealed record GmailMessageIdPage(IReadOnlyCollection<string> Ids, string? NextPageToken);
```

The provider list call for completeness must use the configured 90-day query, `includeSpamTrash=true`, a page token and bounded page size. Persist `NextPageToken` after each successful page.

- [ ] **Step 6: Preserve fast recent refresh**

Each cycle first refreshes recent IDs and previously unread IDs, then consumes backfill pages up to `MaxMetadataLoadsPerCycle`. Once Gmail returns no next token, set `BackfillCompletedAt = now` and clear `BackfillPageToken`.

- [ ] **Step 7: Reset completeness when scope changes**

If configured `WindowDays` changes such that `ScopeStartAt` changes materially, clear `BackfillCompletedAt` and restart backfill for the new scope.

- [ ] **Step 8: Run smoke GREEN**

Assert after the final cycle:

```csharp
Ensure(state.BackfillCompletedAt is not null, "El índice debe declarar explícitamente cuándo completó el alcance.");
Ensure(indexedProviderIds.SetEquals(expectedProviderIds), "El backfill completo no puede dejar IDs fuera.");
```

- [ ] **Step 9: Commit**

```bash
git add src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs src/backend/NexoMail.Infrastructure/Google/ControlCenterIndexSchemaBootstrap.cs src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexHostedService.cs src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexService.cs src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs
git commit -m "feat: complete indexed mailbox scope"
```

---

### Task 4: Add provider-to-index reconciliation and repair diagnostics

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
- Produces:

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

and:

```csharp
Task<MailIndexReconciliationResult> ReconcileAsync(Guid accountId, bool repairMissing, CancellationToken cancellationToken);
```

- [ ] **Step 1: Write RED mismatch/repair test**

Fake provider IDs: `A`, `B`, `C`. Seed index rows `A`, `B`, `ORPHAN`. First reconciliation with `repairMissing:false` must report missing `C`, orphan `ORPHAN`, zero duplicate rows, unhealthy status.

- [ ] **Step 2: Run smoke RED**

Expected: compile failure because reconciliation service/result do not exist.

- [ ] **Step 3: Persist reconciliation state**

Add to `MailIndexStateEntity` and schema bootstrap:

```csharp
public DateTimeOffset? LastReconciledAt { get; set; }
public string? LastReconciliationErrorCode { get; set; }
```

- [ ] **Step 4: Implement full provider ID enumeration for the configured scope**

Reuse the Gmail token/client machinery through focused internal methods on `GmailMetadataIndexService` rather than duplicating OAuth code. The reconciliation path must enumerate all IDs in the declared scope with `includeSpamTrash=true`.

- [ ] **Step 5: Compare provider and index sets**

Use set difference exactly:

```csharp
var missing = providerIds.Except(indexedIds, StringComparer.Ordinal).Order().ToArray();
var orphan = indexedIds.Except(providerIds, StringComparer.Ordinal).Order().ToArray();
```

Duplicate count is computed from indexed rows before converting them to a set. Do not auto-delete orphan rows.

- [ ] **Step 6: Repair only missing provider rows when requested**

When `repairMissing` is true, load metadata for missing IDs and upsert through the same index writer used by ordinary synchronization. Re-run the index set after repair so `Repaired` and `IsHealthy` describe the post-repair state.

- [ ] **Step 7: Expose an authorized diagnostic endpoint**

Add:

```csharp
mail.MapPost("/index/reconcile/{accountId:guid}", async (
    Guid accountId,
    bool? repair,
    GmailIndexReconciliationService service,
    CancellationToken ct) =>
    Results.Ok(await service.ReconcileAsync(accountId, repair ?? true, ct)));
```

This endpoint must validate account ownership inside the service.

- [ ] **Step 8: Prove repair is idempotent GREEN**

Run reconciliation twice with repair enabled. The second result must have:

```csharp
Ensure(second.MissingProviderIds.Count == 0, "No deben quedar mensajes faltantes después de reparar.");
Ensure(second.DuplicateIndexedIds == 0, "La reparación no puede crear duplicados.");
Ensure(second.Repaired == 0, "Una segunda reconciliación sana debe ser idempotente.");
```

Keep `ORPHAN` reported until independently explained; reconciliation must not delete it.

- [ ] **Step 9: Commit**

```bash
git add src/backend/NexoMail.Domain/MailModels.cs src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs src/backend/NexoMail.Infrastructure/Google/ControlCenterIndexSchemaBootstrap.cs src/backend/NexoMail.Infrastructure/Google/GmailIndexReconciliationService.cs src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexService.cs src/backend/NexoMail.Api/MetadataIndexEndpoints.cs src/backend/NexoMail.Api/MailProviderBetaModule.cs src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs
git commit -m "feat: reconcile provider mail with local index"
```

---

### Task 5: Keep indexed state convergent after mailbox mutations

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/Mail/MailIndexMutationService.cs`
- Modify: `src/backend/NexoMail.Api/Program.cs`
- Modify: `src/backend/NexoMail.Api/DraftEndpoints.cs`
- Modify: `src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs`

**Interfaces:**
- Produces:

```csharp
Task MarkReadAsync(Guid accountId, string providerMessageId, bool read, CancellationToken cancellationToken);
Task MoveAsync(Guid accountId, string providerMessageId, string folderId, CancellationToken cancellationToken);
Task MarkAccountForRefreshAsync(Guid accountId, CancellationToken cancellationToken);
```

- [ ] **Step 1: Add RED mutation tests**

Seed one indexed inbox/unread row. After index mutation calls, assert:

```csharp
await mutation.MarkReadAsync(accountId, messageId, true, ct);
Ensure(!row.IsUnread, "Marcar leído debe converger inmediatamente en el índice.");

await mutation.MoveAsync(accountId, messageId, "trash", ct);
Ensure(row.IsTrash && !row.IsInbox, "Mover a Papelera debe reflejarse en el índice.");
```

Also test archive, inbox restore and spam.

- [ ] **Step 2: Run smoke RED**

Expected: compile failure because `MailIndexMutationService` does not exist.

- [ ] **Step 3: Implement exact local state transitions after successful provider mutations**

Rules:

```text
read=true  => IsUnread=false
read=false => IsUnread=true
archive    => IsInbox=false
spam       => IsSpam=true, IsInbox=false, IsTrash=false
trash      => IsTrash=true, IsInbox=false, IsSpam=false
inbox      => IsInbox=true, IsTrash=false, IsSpam=false
```

Update `IndexedAt` whenever an indexed row is changed locally.

- [ ] **Step 4: Wire mutations only after provider success**

For mark-read/move/trash endpoints, call the provider first. Only on successful completion call `MailIndexMutationService`. If provider mutation throws, leave the index unchanged.

- [ ] **Step 5: Make send/reply/draft-send request a near-term refresh**

Because current provider send contracts return `void`, do not fabricate a sent message ID. After successful send/reply/draft-send, set the account index state so the next sync refreshes immediately. `MarkAccountForRefreshAsync` sets `LastIndexedAt` to `DateTimeOffset.UnixEpoch` and clears transient sync error state without deleting indexed messages.

- [ ] **Step 6: Run smoke GREEN**

Verify provider failure does not mutate index, provider success does, and send/reply only marks refresh without inventing rows.

- [ ] **Step 7: Commit**

```bash
git add src/backend/NexoMail.Infrastructure/Mail/MailIndexMutationService.cs src/backend/NexoMail.Api/Program.cs src/backend/NexoMail.Api/DraftEndpoints.cs src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs
git commit -m "feat: keep mail index consistent after mutations"
```

---

### Task 6: Cut ordinary `/mail/messages` reads over to the unified index

**Files:**
- Modify: `src/backend/NexoMail.Api/Program.cs`
- Modify: `src/backend/NexoMail.Api/MailProviderBetaModule.cs`
- Modify: `src/backend/NexoMail.Api/MetadataIndexEndpoints.cs`
- Modify: `src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs`

**Interfaces:**
- `/api/mail/messages` keeps the current query contract: `accountId`, `folder`, `take`, `cursor`, `search`.
- Full message/thread/attachment operations remain on `IMailGateway`.
- Add `POST /api/mail/messages/resolve` accepting `MailMessageReference[]` for exact priority-reference resolution from the same index.

- [ ] **Step 1: Add RED source-boundary assertion**

Add a source-level or API composition smoke assertion requiring the ordinary list handler to depend on `UnifiedInboxQueryService`, not `IMailGateway.GetMessagesAsync`.

- [ ] **Step 2: Run smoke RED**

Expected: assertion fails against the current live-provider list path.

- [ ] **Step 3: Register the unified services**

In `MailProviderBetaModule.AddServices` register:

```csharp
services.AddScoped<UnifiedInboxQueryService>();
services.AddScoped<MailIndexMutationService>();
services.AddScoped<GmailMetadataIndexService>();
services.AddScoped<GmailIndexReconciliationService>();
```

Keep provider implementations registered because full message operations and mutations still need them.

- [ ] **Step 4: Replace only the ordinary list handler**

The `/mail/messages` handler becomes:

```csharp
mail.MapGet("/messages", async (
    UnifiedInboxQueryService inbox,
    Guid? accountId,
    string? folder,
    int? take,
    string? cursor,
    string? search,
    CancellationToken ct) =>
{
    var query = new MailQuery(
        accountId,
        string.IsNullOrWhiteSpace(folder) ? "inbox" : folder.Trim().ToLowerInvariant(),
        Math.Clamp(take ?? 25, 1, 100),
        cursor,
        search);
    return Results.Ok(await inbox.GetMessagesAsync(query, ct));
});
```

Do not wrap this read in process-local `MailReadCache` for correctness.

- [ ] **Step 5: Add exact reference resolver endpoint**

```csharp
mail.MapPost("/messages/resolve", async (
    MailMessageReference[] references,
    UnifiedInboxQueryService inbox,
    CancellationToken ct) =>
    Results.Ok(await inbox.ResolveAsync(references, ct)));
```

- [ ] **Step 6: Make `/mail/refresh` trigger index sync intent rather than merely clearing read cache**

The endpoint should mark active account index states stale/refreshable and return `202 Accepted` or `204 NoContent`; it must not erase rows.

- [ ] **Step 7: Run backend builds and smoke suites GREEN**

```powershell
dotnet build NexoMail.sln
dotnet run --project src/backend/NexoMail.UnifiedInboxSmokeTests/NexoMail.UnifiedInboxSmokeTests.csproj
dotnet run --project src/backend/NexoMail.ControlCenterSmokeTests/NexoMail.ControlCenterSmokeTests.csproj
dotnet run --project src/backend/NexoMail.IntelligenceSmokeTests/NexoMail.IntelligenceSmokeTests.csproj
```

- [ ] **Step 8: Commit**

```bash
git add src/backend/NexoMail.Api/Program.cs src/backend/NexoMail.Api/MailProviderBetaModule.cs src/backend/NexoMail.Api/MetadataIndexEndpoints.cs src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs
git commit -m "refactor: serve unified inbox from mail index"
```

---

### Task 7: Remove the synthetic priority mailbox in React

**Files:**
- Modify: `src/frontend/src/api/mailApi.ts`
- Modify: `src/frontend/src/pages/InboxPage.tsx`
- Create: `src/frontend/scripts/unified-inbox-source-smoke.mjs`

**Interfaces:**
- `mailApi.resolveMessages(references)` posts exact `(accountId, providerMessageId)` references to `/mail/messages/resolve`.
- Priority selection may still come from current Control Center/tracking metadata in Point 1, but every displayed `MailSummary` must be resolved from the unified index.

- [ ] **Step 1: Write RED source smoke**

Create `unified-inbox-source-smoke.mjs` to assert `InboxPage.tsx` no longer contains the synthetic fallback literal:

```js
if (source.includes("senderName: reference.counterpart")) {
  throw new Error('La vista prioritaria todavía fabrica MailSummary fuera del índice.')
}
if (!source.includes('mailApi.resolveMessages')) {
  throw new Error('La vista prioritaria debe resolver referencias desde la bandeja unificada.')
}
```

- [ ] **Step 2: Run RED**

```powershell
node src/frontend/scripts/unified-inbox-source-smoke.mjs
```

Expected: FAIL on the current synthetic fallback.

- [ ] **Step 3: Add resolver API client**

In `mailApi.ts`:

```ts
resolveMessages: (references: Array<{ accountId: string; providerMessageId: string }>) =>
  api<MailSummary[]>('/mail/messages/resolve', {
    method: 'POST',
    body: JSON.stringify(references),
  }),
```

- [ ] **Step 4: Replace synthetic priority mapping with resolved indexed messages**

When `priorityOnly`, build references from Control Center/tracking metadata and query `mailApi.resolveMessages`. The display list must use only returned `MailSummary` rows. If a Control Center reference does not resolve, omit it from display and leave the invariant failure to backend smoke/diagnostics; never manufacture sender/subject/preview fields.

- [ ] **Step 5: Preserve normal messages query behavior**

Ordinary inbox/folder/search pages continue using `mailApi.messages`, which now reads the index. Priority selection is metadata over the same indexed universe.

- [ ] **Step 6: Run frontend source smoke and build GREEN**

```powershell
node src/frontend/scripts/unified-inbox-source-smoke.mjs
cd src/frontend
pnpm build
```

Expected: both PASS.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/api/mailApi.ts src/frontend/src/pages/InboxPage.tsx src/frontend/scripts/unified-inbox-source-smoke.mjs
git commit -m "refactor: resolve priority mail from unified inbox"
```

---

### Task 8: Prove cross-module invariants and resilience

**Files:**
- Modify: `src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs`
- Modify: `src/backend/NexoMail.ControlCenterSmokeTests/Program.cs`
- Modify: `src/backend/NexoMail.IntelligenceSmokeTests/Program.cs`
- Create: `.github/workflows/unified-inbox-smoke.yml`

**Interfaces:**
- Validation artifact: automated evidence that Inbox, Control Center and Intelligence reference the same indexed rows/conversations.

- [ ] **Step 1: Add Control Center reference invariant**

For every `ControlCenterPendingItem` returned in a seeded scenario:

```csharp
Ensure(await database.MailMessageIndex.AsNoTracking().AnyAsync(x =>
    x.UserId == userId &&
    x.AccountId == item.AccountId &&
    x.ProviderMessageId == item.MessageId, ct),
    $"Control Center referencia un mensaje fuera del índice: {item.AccountId}/{item.MessageId}");
```

- [ ] **Step 2: Add Intelligence conversation invariant**

For every Intelligence result, assert at least one indexed row exists for the same user/account/thread conversation key. No Intelligence result may exist without underlying indexed communication data.

- [ ] **Step 3: Add provider outage resilience scenario**

Seed indexed mail, configure provider HTTP to throw, then assert `UnifiedInboxQueryService.GetMessagesAsync` still returns the seeded rows and the account index state reports stale/sync error separately.

- [ ] **Step 4: Add multi-account partial failure scenario**

One account sync fails authentication; another remains healthy. Assert unified query returns both accounts' previously indexed rows and no rows are removed because of the failing account.

- [ ] **Step 5: Add reconciliation acceptance gate**

The final fake-provider validation account must satisfy:

```csharp
Ensure(result.MissingProviderIds.Count == 0, "Reconciliación final: faltan mensajes del proveedor.");
Ensure(result.DuplicateIndexedIds == 0, "Reconciliación final: existen duplicados.");
Ensure(result.OrphanIndexedIds.Count == 0, "Reconciliación final: existen huérfanos no explicados.");
Ensure(result.IsHealthy, "La cuenta reconciliada debe quedar saludable.");
```

- [ ] **Step 6: Add dedicated GitHub Actions workflow**

Create `.github/workflows/unified-inbox-smoke.yml`:

```yaml
name: Unified Inbox source-of-truth smoke

on:
  push:
    paths:
      - 'src/backend/NexoMail.Domain/**'
      - 'src/backend/NexoMail.Application/**'
      - 'src/backend/NexoMail.Infrastructure/**'
      - 'src/backend/NexoMail.Api/**'
      - 'src/backend/NexoMail.UnifiedInboxSmokeTests/**'
      - 'src/frontend/src/api/mailApi.ts'
      - 'src/frontend/src/pages/InboxPage.tsx'
      - 'src/frontend/scripts/unified-inbox-source-smoke.mjs'
      - '.github/workflows/unified-inbox-smoke.yml'
  pull_request:
    paths:
      - 'src/backend/**'
      - 'src/frontend/src/api/mailApi.ts'
      - 'src/frontend/src/pages/InboxPage.tsx'
      - 'src/frontend/scripts/unified-inbox-source-smoke.mjs'
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
      - run: node src/frontend/scripts/unified-inbox-source-smoke.mjs
      - run: pnpm install --frozen-lockfile
        working-directory: src/frontend
      - run: pnpm build
        working-directory: src/frontend
```

- [ ] **Step 7: Run full local verification**

```powershell
dotnet build NexoMail.sln
dotnet run --project src/backend/NexoMail.UnifiedInboxSmokeTests/NexoMail.UnifiedInboxSmokeTests.csproj
dotnet run --project src/backend/NexoMail.ControlCenterSmokeTests/NexoMail.ControlCenterSmokeTests.csproj
dotnet run --project src/backend/NexoMail.IntelligenceSmokeTests/NexoMail.IntelligenceSmokeTests.csproj
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
dotnet run --project src/backend/NexoMail.DraftSmokeTests/NexoMail.DraftSmokeTests.csproj
node src/frontend/scripts/unified-inbox-source-smoke.mjs
cd src/frontend
pnpm build
```

If a project name in the last two existing regression commands differs in the repository, use the actual existing smoke project path discovered from `.github/workflows/commercial-smoke.yml` and `.github/workflows/draft-smoke.yml`; do not skip either regression suite.

- [ ] **Step 8: Verify source boundaries by search**

Expected ordinary list behavior after cutover:

```text
/api/mail/messages -> UnifiedInboxQueryService -> MailMessageIndex
Control Center -> MailMessageIndex
Nexo Intelligence -> MailMessageIndex
```

`IMailGateway.GetMessagesAsync` may remain only for provider diagnostics/parity tooling or other explicitly provider-backed features, not the ordinary Unified Inbox endpoint.

- [ ] **Step 9: Commit final verification**

```bash
git add src/backend/NexoMail.UnifiedInboxSmokeTests/Program.cs src/backend/NexoMail.ControlCenterSmokeTests/Program.cs src/backend/NexoMail.IntelligenceSmokeTests/Program.cs .github/workflows/unified-inbox-smoke.yml
git commit -m "test: enforce unified mail source invariants"
```

---

## Point 1 Acceptance Gate

Point 1 is approved only when all of the following are demonstrated on the branch:

1. `MailMessageIndex` has provider-independent folder/read membership sufficient for Unified Inbox queries.
2. The 90-day configured scope completes without a total-message cap.
3. Reconciliation for validation accounts reports `missing = 0`, `duplicates = 0`, and no unexplained orphan rows.
4. `/api/mail/messages` reads `UnifiedInboxQueryService`, not live provider listing.
5. Full body/thread/attachment access can remain provider-backed.
6. Read/move/trash mutations converge index state only after provider success.
7. Send/reply trigger refresh without fabricating sent rows.
8. Provider outage keeps already indexed mail visible and exposes stale/error state separately.
9. Priority UI no longer fabricates `MailSummary` objects from Control Center data.
10. Every Control Center item and every Nexo Intelligence conversation resolves to the same indexed universe.
11. Unified Inbox, Control Center, Intelligence, commercial, draft and frontend build regression checks are green.
12. No message body or attachment bytes are newly persisted.
