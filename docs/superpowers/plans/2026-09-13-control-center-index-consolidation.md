# Control Center Index Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `MailMessageIndex` the exclusive read source for Control Center metrics and move Gmail access to periodic metadata synchronization.

**Architecture:** Persist the Gmail metadata needed for Control Center classification, centralize classification rules in a pure classifier, and rewrite snapshot/activity services to query EF Core only. Add a hosted sync coordinator with persistent per-account lease metadata so multiple backend instances do not duplicate mailbox synchronization.

**Tech Stack:** .NET 10, ASP.NET Core hosted services, EF Core, SQLite/current relational provider, existing Gmail REST integration, existing smoke-test project.

**Spec:** `docs/superpowers/specs/2026-09-13-control-center-index-consolidation-design.md`

## Global Constraints

- Message bodies and attachment bytes must never be persisted.
- `/control-center` and `/control-center/activity` must perform no Gmail HTTP calls.
- Preserve the current automated/non-actionable classification semantics.
- Keep current Control Center response contracts stable unless a schema change is strictly required.
- Default background synchronization interval: 5 minutes and configurable.
- Horizontal synchronization coordination must use persistent database state, not only process-local locks.

---

### Task 1: Persist Control Center classification metadata

**Files:**
- Modify: `src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexService.cs`
- Test: `src/backend/NexoMail.ControlCenterSmokeTests/Program.cs`

**Interfaces:**
- Produces additional `MailMessageIndexEntity` properties for Gmail labels/read state and automation headers.
- `GmailMetadataIndexService` populates those properties on every refreshed message.

- [ ] Add a failing smoke-test case that indexes an incoming Gmail message containing CATEGORY_UPDATES, UNREAD, `Auto-Submitted`, `Precedence` and `List-Unsubscribe`, then asserts those source signals are persisted.
- [ ] Run `dotnet run --project src/backend/NexoMail.ControlCenterSmokeTests/NexoMail.ControlCenterSmokeTests.csproj` and verify the new assertions fail.
- [ ] Add focused entity properties and EF mappings for labels/read state, `AutoSubmitted`, `Precedence`, and `HasListUnsubscribe`.
- [ ] Extend `LoadMessageMetadataAsync` to request and parse the required headers/labels and extend `IndexedMessage` accordingly.
- [ ] Extend `UpsertAccountIndexAsync` to update all classification metadata on both insert and refresh.
- [ ] Run the smoke tests and verify the new metadata-persistence scenario passes.
- [ ] Commit with `feat: persist control center mail metadata`.

### Task 2: Extract and regression-test the message classifier

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/Google/ControlCenterMessageClassifier.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Google/GmailControlCenterService.cs`
- Test: `src/backend/NexoMail.ControlCenterSmokeTests/Program.cs`

**Interfaces:**
- Produces a pure `ControlCenterMessageClassifier.IsNonActionableReceived(...)` API accepting persisted-message metadata.
- Consumers must not need Gmail HTTP objects or JSON structures.

- [ ] Add failing regression cases for personal mail, promotion/social/forum categories, noreply variants, `List-Unsubscribe`, `Auto-Submitted`, bulk/list/junk precedence, high-confidence transactional subjects, and CATEGORY_UPDATES plus notification sender/transactional subject.
- [ ] Run smoke tests and confirm the classifier tests fail because the extracted API does not yet exist.
- [ ] Implement the classifier by moving the exact normalization, sender-signal and subject-rule behavior from `GmailControlCenterService` without semantic simplification.
- [ ] Adapt the old service temporarily to call the classifier so there is only one rules implementation during the migration.
- [ ] Run smoke tests and verify all classification regressions pass.
- [ ] Commit with `refactor: centralize control center classification`.

### Task 3: Read Control Center snapshots exclusively from the index

**Files:**
- Rewrite: `src/backend/NexoMail.Infrastructure/Google/GmailControlCenterService.cs`
- Test: `src/backend/NexoMail.ControlCenterSmokeTests/Program.cs`

**Interfaces:**
- Keeps `GetSnapshotAsync(Guid? accountId, CancellationToken)` and `UpdateStateAsync(...)` public contracts.
- Removes request-time dependencies on `IHttpClientFactory`, `ITokenProtector` and `IOptions<GmailOptions>`.

- [ ] Add indexed-message scenarios representing received-last, sent-last, unread/read, >48h overdue, resolved and snoozed conversations, plus account filtering.
- [ ] Configure the smoke test HTTP factory to throw on every request and assert `GetSnapshotAsync` still succeeds.
- [ ] Run smoke tests and verify the no-HTTP assertion fails against the current service.
- [ ] Rewrite `GetSnapshotAsync` to load active Gmail accounts, relevant `MailMessageIndex`, `ControlCenterStates`, and `MailIndexStates` only.
- [ ] Group messages by `(AccountId, ThreadId)`, select the newest message, create pending sent/received items, apply `ControlCenterMessageClassifier`, then apply current resolved/snoozed suppression semantics.
- [ ] Compute unread counts, seven-day activity, overdue counts, priorities, pending items and account summaries from indexed rows.
- [ ] Derive account availability from index state presence/freshness instead of Gmail exceptions.
- [ ] Remove all token and Gmail HTTP helpers from `GmailControlCenterService`.
- [ ] Run smoke tests and verify all snapshot and no-HTTP scenarios pass.
- [ ] Commit with `refactor: serve control center from mail index`.

### Task 4: Read Control Center activity exclusively from the index

**Files:**
- Rewrite: `src/backend/NexoMail.Infrastructure/Google/GmailControlCenterActivityService.cs`
- Test: `src/backend/NexoMail.ControlCenterSmokeTests/Program.cs`

**Interfaces:**
- Keeps `GetActivityAsync(Guid? accountId, int? requestedDays, int? requestedOffsetDays, CancellationToken)`.
- Removes Gmail/token/options dependencies.

- [ ] Add failing indexed activity cases for 7, 14 and 30 days, offset windows, sent/received totals and account filtering.
- [ ] Use an HTTP factory that throws if called and assert activity still succeeds.
- [ ] Run smoke tests and confirm failure against the current implementation.
- [ ] Rewrite activity aggregation over `MailMessageIndex` using the requested UTC date window and active Gmail accounts.
- [ ] Determine per-account availability from `MailIndexStates`.
- [ ] Remove all Gmail HTTP counting/token code.
- [ ] Run smoke tests and verify activity tests pass with zero HTTP usage.
- [ ] Commit with `refactor: serve control center activity from index`.

### Task 5: Remove Control Center dependence on process-local read cache

**Files:**
- Modify: `src/backend/NexoMail.Api/Program.cs`
- Test: existing API/smoke coverage where applicable.

**Interfaces:**
- `/control-center` and `/control-center/activity` call the index-backed services directly.
- `MailReadCache` remains available for unrelated endpoints.

- [ ] Add or update endpoint-level coverage to verify both endpoints resolve their services without cache-wrapped live fetches.
- [ ] Remove `MailReadCache` usage from the two Control Center endpoint handlers while preserving response DTOs and status codes.
- [ ] Run backend build and smoke tests.
- [ ] Commit with `refactor: remove control center read cache dependency`.

### Task 6: Add request-independent metadata synchronization

**Files:**
- Modify: `src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexService.cs`
- Create: `src/backend/NexoMail.Infrastructure/Google/GmailMetadataIndexHostedService.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs`
- Modify: `src/backend/NexoMail.Api/Program.cs` and/or existing service registration file.
- Test: `src/backend/NexoMail.ControlCenterSmokeTests/Program.cs`

**Interfaces:**
- Add a synchronization entry point that accepts an explicit user/account scope rather than depending exclusively on request `IUserContext`.
- Hosted service periodically creates a DI scope and asks the sync coordinator to process active Gmail accounts.
- Persist lease fields in `MailIndexStateEntity`, for example `SyncLeaseOwner` and `SyncLeaseUntil`, with atomic acquire/release semantics.

- [ ] Add failing tests for explicit-user synchronization and lease rejection when another worker owns an unexpired lease.
- [ ] Run smoke tests and confirm failures.
- [ ] Refactor `GmailMetadataIndexService` so its core sync can execute for an explicit user/account scope while keeping the manual endpoint-compatible wrapper.
- [ ] Add persistent lease fields/mapping and an atomic lease acquisition method per account.
- [ ] Implement `GmailMetadataIndexHostedService` with a configurable five-minute default interval, scoped service resolution, cancellation support and per-account fault isolation.
- [ ] Register hosted service/options in the API startup path.
- [ ] Run smoke tests and backend build.
- [ ] Commit with `feat: synchronize gmail metadata in background`.

### Task 7: Final verification and cleanup

**Files:**
- Review all files changed above.
- Update documentation only if configuration keys or operational startup behavior need to be recorded.

**Interfaces:**
- No Control Center read path may reference Gmail/token/http dependencies.

- [ ] Search source for Gmail HTTP calls reachable from `GmailControlCenterService` and `GmailControlCenterActivityService`; expected result: none.
- [ ] Run `dotnet build NexoMail.sln`.
- [ ] Run `dotnet run --project src/backend/NexoMail.ControlCenterSmokeTests/NexoMail.ControlCenterSmokeTests.csproj`.
- [ ] Review EF schema/migration behavior for the current development and production database strategy and add the required migration/schema update using the repository's established pattern.
- [ ] Verify manual `/control-center/index/sync` still works and remains diagnostic/forced-sync functionality.
- [ ] Verify frontend Control Center contracts need no changes.
- [ ] Commit final migration/docs cleanup with `chore: finalize control center index consolidation`.
