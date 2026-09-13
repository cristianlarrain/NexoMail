# Control Center Index Consolidation Design

## Goal

Make the local mail metadata index the exclusive read source for NexoMail Control Center operational metrics while preserving all existing business rules that exclude non-actionable or automated incoming mail.

## Architecture

The Gmail API is accessed only by the metadata synchronization path. `GmailMetadataIndexService` becomes the ingestion boundary and persists all metadata needed to reproduce Control Center semantics without storing message bodies. `GmailControlCenterService` and `GmailControlCenterActivityService` read only EF Core entities from the NexoMail database.

Data flow:

`Gmail API -> metadata synchronization -> MailMessageIndex -> Control Center queries`

Request-time Control Center endpoints must never call Gmail.

## Indexed metadata

Extend `MailMessageIndexEntity` with the minimum source metadata required for Control Center rules:

- Gmail labels or normalized booleans sufficient to represent INBOX, SENT, UNREAD, CATEGORY_PROMOTIONS, CATEGORY_SOCIAL, CATEGORY_FORUMS and CATEGORY_UPDATES.
- `Auto-Submitted`.
- `Precedence`.
- Whether `List-Unsubscribe` is present.

Existing `ProviderMessageId`, `ThreadId`, `Direction`, sender, recipients, subject and occurrence timestamp remain authoritative for thread reconstruction.

Message bodies and attachment bytes remain excluded from persistence.

## Business-rule preservation

Move the classification rules currently embedded in `GmailControlCenterService` into a focused `ControlCenterMessageClassifier` shared by synchronization/query tests. Preserve the current semantics for:

- Gmail promotion/social/forum categories.
- noreply/no-reply/do-not-reply/donotreply/mailer-daemon senders.
- `List-Unsubscribe`.
- `Auto-Submitted` except `no`.
- `Precedence` bulk/list/junk.
- notification-style sender signals.
- high-confidence transactional subjects such as payment, transfer, invoice, receipt, order, shipping, security and subscription notifications.
- informational transactional subjects when category/sender signals also indicate notification behavior.

The classifier must operate only on persisted metadata so rules can be changed later without re-fetching message bodies.

## Control Center snapshot

`GmailControlCenterService.GetSnapshotAsync` reads active Gmail accounts, indexed messages and `ControlCenterStates`. It reconstructs threads, chooses the newest indexed message in each thread, applies the classifier for received mail, applies resolved/snoozed state suppression, and computes:

- received without reply;
- sent without response;
- unread count;
- overdue items older than 48 hours;
- seven-day received/sent activity;
- priority/pending items;
- per-account summaries;
- index availability based on `MailIndexStates` freshness instead of live Gmail availability.

## Activity

`GmailControlCenterActivityService` reads `MailMessageIndex` exclusively and groups received/sent messages by UTC calendar day for 7, 14 or 30-day windows and account filters. It reports account availability from index state/freshness rather than HTTP success.

## Synchronization

`GmailMetadataIndexService` remains the Gmail integration boundary. Synchronization refreshes recent messages even when already indexed so label/read-state changes are reflected. It persists the additional headers and label metadata needed by the classifier.

A hosted background synchronization service periodically creates DI scopes and synchronizes active Gmail accounts independently of request-bound `IUserContext`. The synchronization interval is configurable, with five minutes as the production default.

For horizontal deployments, synchronization must not rely solely on process-local semaphores. A persistent per-account lease/state prevents two backend instances from synchronizing the same mailbox concurrently.

## Cache behavior

`/control-center` and `/control-center/activity` no longer rely on `MailReadCache` for correctness. Database/index state is the shared source across backend instances. Existing cache components may remain for unrelated endpoints.

## Tests

Regression coverage must include:

- personal received mail remains actionable;
- promotion/social/forum mail is excluded;
- noreply and other automated sender forms are excluded;
- `Auto-Submitted`, `Precedence` and `List-Unsubscribe` behavior;
- high-confidence transactional subjects are excluded;
- CATEGORY_UPDATES plus notification-style transactional content is excluded;
- sent-last thread becomes sent-without-response;
- received-last thread becomes received-without-reply only when actionable;
- unread/read behavior;
- 48-hour overdue behavior;
- resolved and snoozed state behavior;
- activity grouping for 7/14/30-day windows;
- account filtering;
- Control Center queries execute without any Gmail HTTP calls.

## Non-goals

- No message body persistence.
- No materialized pending-thread table in this phase.
- No unrelated frontend refactor.
