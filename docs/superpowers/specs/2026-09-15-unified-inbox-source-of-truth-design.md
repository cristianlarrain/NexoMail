# Unified Inbox Source of Truth Design

## Goal

Make NexoMail operate over one normalized mail universe so the Unified Inbox, Control Center and Nexo Intelligence can never disagree about which messages and conversations exist.

The external mail provider remains the authoritative external source, but NexoMail uses one reconciled local index as its operational source of truth.

## Problem

NexoMail currently has two read universes:

- The classic/unified inbox reads live provider data through `IMailGateway.GetMessagesAsync(...)`.
- Control Center and Nexo Intelligence read `MailMessageIndex`.

This permits valid mail to appear in the inbox but be absent from Control Center or Intelligence, or for different freshness windows and pagination behavior to produce inconsistent results.

The frontend also has a second path for priority mail: `InboxPage` disables the normal messages query and builds its list from Control Center references when `priority=1`.

## Architectural decision

Adopt the following flow:

`Gmail / Microsoft 365 / IMAP -> synchronization + reconciliation -> normalized NexoMail mail index -> Unified Inbox / Control Center / Nexo Intelligence / Nexi`

Provider APIs remain the source used to discover and mutate mailbox state. The normalized local index becomes the only read source for operational listing, classification and productivity views.

Opening the full body, downloading an attachment, sending, replying, deleting, moving or changing provider state may still call the provider. After successful mutations, NexoMail must update or invalidate the corresponding indexed state.

## Source-of-truth rules

1. Every message visible to NexoMail within the configured synchronized scope must have exactly one normalized indexed representation per `(UserId, AccountId, ProviderMessageId)`.
2. All operational list/read consumers use the same indexed rows.
3. Nexo Intelligence may classify, score or filter messages, but it must never determine whether a message exists.
4. Control Center is a filtered/derived view of indexed conversations, not a separate mailbox.
5. The Unified Inbox must always offer an unfiltered `All` view over the synchronized operational universe.
6. Provider outages must not erase already indexed mail.
7. Stale provider state must be explicit and observable rather than silently represented as complete/fresh.

## Normalized indexed model

The existing `MailMessageIndex` remains the storage boundary for this phase to avoid unnecessary migration churn.

It must expose provider-independent fields sufficient for Unified Inbox reads and Intelligence:

- `UserId`
- `AccountId`
- `ProviderMessageId`
- `ThreadId`
- direction (`received` / `sent`)
- sender name/address
- recipients
- subject
- snippet
- occurrence timestamp
- read/unread state
- normalized mailbox/folder state needed by inbox/archive/sent/spam/trash behavior
- attachment presence
- indexed/synchronized timestamp
- provider-specific classification metadata required by adapters or Intelligence

Provider-specific values such as Gmail labels may remain persisted as adapter metadata, but frontend and cross-provider business logic must depend on normalized fields.

No message body or attachment bytes are added to persistent storage by this project.

## Synchronization completeness

The current Gmail metadata index uses a bounded lookback/window and message limits. That is insufficient to declare the index the only operational source without additional guarantees.

Before `/mail/messages` switches to index-backed reads, synchronization must provide:

- deterministic coverage for the configured synchronized mailbox scope;
- incremental refresh of recent/new messages;
- refresh of previously unread or otherwise state-sensitive messages;
- stable pagination/backfill until the configured scope is complete;
- per-account sync state;
- explicit stale/error state;
- idempotent upsert behavior;
- no duplicate indexed messages.

The first production scope may remain intentionally bounded if product policy defines it, but the UI, Control Center and Intelligence must all operate over exactly the same declared scope.

## Reconciliation

Synchronization alone is not sufficient. Add a reconciliation process that compares provider identifiers against indexed identifiers for a defined account/scope.

For each account reconciliation must report at least:

- provider message count in scope;
- indexed message count in scope;
- missing provider IDs;
- orphan/stale indexed IDs;
- duplicate indexed IDs;
- last successful reconciliation time;
- reconciliation error state.

The zero-defect target for a healthy reconciled account is:

- `missing = 0`
- `duplicates = 0`
- no unexplained orphan rows

The reconciliation path must be safe to rerun and must repair missing index rows without creating duplicates.

## Unified Inbox read path

Introduce an index-backed inbox query boundary that can return `PagedResult<MailSummary>` from the normalized index.

It must support the behavior currently expected by `mailApi.messages(...)`:

- account-specific or all-account listing;
- folder selection;
- search;
- deterministic descending date ordering;
- stable cursor pagination;
- provider/account identity on each item;
- read state;
- attachment indicator.

Once validated, `/api/mail/messages` uses this query boundary instead of live provider listing.

Full message/thread/attachment fetches may remain provider-backed in this phase.

## Mutation consistency

Operations that change mailbox state must preserve source-of-truth consistency.

After a successful provider mutation such as:

- mark read/unread;
- move/archive;
- move to spam;
- move to trash;
- restore/move back;
- send/reply;

NexoMail must either update the affected indexed row(s) immediately or mark them for immediate refresh/reconciliation.

A mutation must never leave the UI permanently inconsistent with the provider.

## Frontend behavior

`InboxPage` must no longer construct a separate priority mailbox from Control Center results.

The page always consumes the Unified Inbox list source. Intelligence/Control Center data is joined as classification metadata or applied as filters over the same indexed message universe.

Examples:

- `All` -> all indexed messages in selected scope.
- `Requires action` -> filter/classification over the same items.
- `Waiting external` -> filter/classification over the same items.
- `Priority` -> ranking/filter over the same items.

No intelligent filter may make the underlying message inaccessible from `All`.

## Control Center alignment

Control Center already reads from `MailMessageIndex`; preserve that direction.

Validation must prove that every Control Center message reference maps to an indexed message/conversation in the same user/account scope.

Control Center must not independently fetch Gmail at request time.

## Nexo Intelligence alignment

Nexo Intelligence continues to consume normalized indexed conversations.

Validation must prove that every Intelligence result maps to a conversation built from the same index used by the Unified Inbox.

Intelligence may attach:

- actionable state;
- conversation state;
- priority score;
- explainability/reason codes;
- semantic review status;

but not existence.

## Failure behavior

### Provider unavailable

- keep existing indexed mail visible;
- mark account sync state as stale/error;
- do not silently drop the account from the unified result;
- prevent false claims of freshness.

### Partial account failure

- one failed account must not suppress other healthy accounts;
- the unified result remains available with account-level availability metadata.

### Index stale

- continue serving indexed data;
- surface stale state;
- schedule/allow retry and reconciliation.

### Reconciliation mismatch

- record diagnostics;
- repair missing rows when possible;
- do not delete provider-backed messages based only on local assumptions.

## Verification and validation

Point 1 is not considered complete when code merely compiles. It is approved only when the following evidence is green.

### Source consistency tests

- One account: no duplicate or missing indexed messages in the tested scope.
- Multiple accounts: correct union without cross-account collisions.
- Stable global chronological ordering.
- Stable cursor pagination with no skipped or repeated messages across page boundaries.
- Account filtering returns only that account.
- Search is evaluated consistently against indexed data.
- Read/unread state matches indexed provider state.
- Received/sent direction is correct.

### Mutation tests

- Mark read/unread updates or refreshes the index.
- Archive/move updates or refreshes normalized folder state.
- Trash/restore updates or refreshes normalized folder state.
- Send/reply produces a corresponding sent indexed message after synchronization/refresh.

### Resilience tests

- Failure of one provider/account does not remove mail from other accounts.
- Already indexed mail remains visible during provider outage.
- Account freshness/error state is observable.
- Reconciliation can be rerun safely.

### Cross-module invariants

- `/mail/messages`, Control Center and Nexo Intelligence all read the same normalized indexed universe.
- No request-time Control Center or Intelligence read calls Gmail.
- Every Control Center item resolves to an indexed message/conversation.
- Every Intelligence result resolves to an indexed conversation.
- Priority/intelligence filters never create an independent mailbox.

### Provider-to-index reconciliation acceptance

For each test account and defined synchronized scope:

`provider IDs - indexed IDs = 0`

`duplicate indexed IDs = 0`

Any difference fails validation until explained or repaired.

## Test strategy

Use TDD for each implementation slice.

Required automated coverage:

- backend unit tests for cursor/filter/query logic;
- integration/smoke tests with relational storage;
- provider adapter tests using deterministic fake responses;
- reconciliation mismatch/repair scenarios;
- frontend tests proving `InboxPage` uses one list source for normal and intelligent filters;
- existing Control Center smoke suite;
- existing Nexo Intelligence smoke suite;
- commercial and draft regression suites;
- full frontend build.

Before completion, execute the repository-wide build and all existing critical workflows/smokes.

## Migration strategy

1. Add tests defining the normalized inbox contract and reconciliation invariants.
2. Extend normalized index fields only where required.
3. Make synchronization complete for the declared scope.
4. Add reconciliation and diagnostics.
5. Add index-backed Unified Inbox query service.
6. Validate it in shadow against current live-provider inbox reads.
7. Switch `/mail/messages` to the index-backed query after parity is demonstrated.
8. Remove the special Control Center-driven priority mailbox path from frontend.
9. Join/filter Intelligence metadata over the same list.
10. Run cross-module and regression validation.

The live-provider inbox path should remain available only as a temporary diagnostic comparison during migration and must be removed or isolated once parity is established.

## Completion criteria

Point 1 is complete only when all of the following are true:

- one normalized indexed universe feeds Unified Inbox, Control Center and Nexo Intelligence;
- the Unified Inbox no longer depends on live provider listing for ordinary reads;
- reconciliation reports zero unexplained missing or duplicate messages for validation accounts/scopes;
- intelligent filters operate over the same inbox data rather than separate Control Center result sets;
- provider outages do not silently remove indexed mail;
- account freshness is visible;
- mutation paths converge the index back to provider state;
- all new and existing critical tests/builds are green;
- no message body or attachment bytes are newly persisted.

## Non-goals

- Migrating the production database provider in this point.
- Implementing Microsoft/IMAP ingestion if those adapters are not yet complete.
- Changing commercial plans or billing.
- Persisting message bodies.
- Replacing Nexo Intelligence classification rules.
- Redesigning unrelated UI.
