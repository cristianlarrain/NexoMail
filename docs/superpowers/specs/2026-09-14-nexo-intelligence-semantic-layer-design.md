# Nexo Intelligence — Semantic Intelligence Layer Design

Date: 2026-09-14
Status: Approved in chat; written specification pending final user review
Branch: `feature/nexo-intelligence-engine-services`

## 1. Purpose

Nexo Intelligence already has a deterministic core that can safely decide structural cases such as outbound conversations that are waiting for an external response. It now also has an explicit abstention path for received messages where metadata alone is insufficient: `RequiresSemanticReview`.

The purpose of this design is to add a provider-agnostic semantic layer that analyzes only those abstained conversations and returns a structured semantic assessment. The first implementation will run in shadow mode and will not modify the visible Control Center, persisted conversation state, or existing Legacy behavior.

The design preserves a strict separation between:

1. deterministic structural reasoning;
2. semantic interpretation;
3. future hybrid decision orchestration;
4. conversational presentation through Nexi.

Nexo Intelligence must remain usable without any external AI provider. External models are optional semantic assistants, not the source of truth for deterministic state.

## 2. Evidence motivating this phase

The latest shadow comparison on the development branch produced the following boundary:

- Legacy pending: 35
- deterministic Intelligence pending: 20
- pending agreements: 16
- Legacy-only: 19
- Intelligence-only: 4
- direction mismatches: 0
- semantic-review candidates: 171
- received Intelligence-only false positives after abstention: 0

The semantic candidate diagnostics showed that metadata alone is insufficient for most received conversations:

- 171 direct-recipient candidates
- 134 already read
- 37 unread
- 42 reply-discouraged senders
- 50 Gmail `CATEGORY_UPDATES`
- 3 Gmail `CATEGORY_PROMOTIONS`
- 151 single-message threads
- 20 multi-message threads

These figures are validation evidence only. They must not become user-specific global rules.

## 3. Non-goals for this phase

This phase does not:

- replace `GET /api/mail/control-center`;
- alter Legacy classification;
- persist semantic decisions as operational truth;
- auto-reply, archive, move, delete, or modify mail;
- train a shared model from private mail;
- derive universal rules from private subjects, senders, domains, organizations, or message counts;
- send full message bodies to an external model;
- perform semantic analysis for conversations already decided confidently by the deterministic core;
- make Nexi the decision engine.

## 4. Architectural decision

Use a three-layer decision pipeline:

```text
Local communication index
        |
        v
Nexo Intelligence Core
(deterministic structural layer)
        |
        +--> confident deterministic result
        |
        +--> RequiresSemanticReview
                    |
                    v
          Semantic Intelligence Layer
          (provider-agnostic contract)
                    |
                    v
          Semantic shadow assessment
                    |
                    v
       future Hybrid Decision Orchestrator
                    |
                    v
                 Nexi/UI
```

The semantic layer does not call OpenAI directly from the application contract. `NexoMail.Application.Intelligence` defines the contract; infrastructure provides provider-specific adapters.

The first provider adapter may reuse NexoMail's existing AI transport and usage telemetry. Nexo Intelligence must not reference OpenAI-specific request or response types.

## 5. Approaches considered

### 5.1 Recommended: provider-agnostic semantic contract + OpenAI adapter

The Application layer owns semantic input/output contracts. Infrastructure supplies an OpenAI-backed implementation using the existing AI transport and usage telemetry.

Advantages:

- keeps Nexo Intelligence independent of one vendor;
- reuses token/cost/error telemetry already present in NexoMail;
- supports later local models or alternate providers;
- can be fully mocked in tests;
- permits shadow evaluation before hybrid activation.

This is the selected design.

### 5.2 Direct OpenAI calls inside the Intelligence service

Rejected because it would couple the engine to one provider and make the core harder to extract commercially as a library/API.

### 5.3 Use Nexi's current mail-summary result as the classifier

Rejected because summarization and operational classification are different contracts. A user-facing summary may be useful context, but Nexo Intelligence requires constrained, auditable structured decisions.

## 6. Semantic contract

### 6.1 Candidate

The semantic analyzer receives a provider-neutral candidate containing only the minimum context required to interpret actionability.

Conceptual contract:

```csharp
public sealed record SemanticCommunicationCandidate(
    string ConversationId,
    string LatestMessageId,
    DateTimeOffset OccurredAt,
    string Subject,
    string Snippet,
    IReadOnlyList<SemanticThreadExcerpt> RecentExcerpts,
    bool IsRead,
    bool IsDirectRecipient,
    bool ReplyDiscouragedSender,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> StructuralReasonCodes);

public sealed record SemanticThreadExcerpt(
    CommunicationDirection Direction,
    DateTimeOffset OccurredAt,
    string Snippet);
```

The contract intentionally omits account credentials, OAuth tokens, provider access tokens, full message bodies, attachment contents, and unnecessary address identities.

### 6.2 Assessment

The analyzer returns a constrained assessment:

```csharp
public sealed record SemanticActionabilityAssessment(
    CommunicationActionType ActionType,
    bool RequiresAction,
    double Confidence,
    DateTimeOffset? Deadline,
    IReadOnlyList<string> ReasonCodes,
    bool IsUncertain);
```

Allowed semantic action types for this phase:

- `None`
- `Reply`
- `Confirm`
- `Review`
- `CompleteTask`
- `Unknown`

`WaitForExternal` remains primarily a deterministic outbound state and is not a normal semantic result for received candidates.

### 6.3 Analyzer interface

```csharp
public interface ISemanticCommunicationAnalyzer
{
    Task<IReadOnlyList<SemanticActionabilityAssessment>> AnalyzeAsync(
        IReadOnlyList<SemanticCommunicationCandidate> candidates,
        CancellationToken cancellationToken = default);
}
```

The implementation must preserve one-to-one correlation between input candidates and returned decisions. The concrete contract may include an explicit correlation identifier if required to make batching robust.

## 7. Meaning of semantic decisions

The semantic layer answers a narrow question: what, if anything, does the latest conversation require from the user?

It should distinguish at minimum:

- explicit request for a reply;
- explicit request for confirmation or acknowledgement;
- request to review information or a document;
- task the user is expected to complete;
- informational message with no action expected;
- insufficient context to decide safely.

The semantic model must not infer an obligation merely because:

- the user is a direct recipient;
- the message is unread;
- the message has aged;
- the sender appears human;
- the thread contains multiple messages.

These are context signals, not semantic proof.

## 8. Confidence and abstention

The semantic layer must be allowed to abstain.

Initial output rules:

- `Confidence` is normalized to `0.0–1.0`.
- `IsUncertain = true` when the model states that context is insufficient or the response cannot be parsed reliably.
- malformed, incomplete, contradictory, or missing model output becomes `Unknown + IsUncertain` rather than a guessed action.
- no semantic decision in this phase becomes operational truth automatically.

No production confidence threshold is selected in this design. Threshold calibration must be based on shadow measurements, not intuition.

## 9. Context minimization and privacy

### 9.1 Phase-one semantic payload

Send only:

- subject;
- indexed snippet;
- latest message direction;
- timestamp;
- read state;
- structural reason codes;
- non-identifying structural categories;
- for multi-message threads, a bounded number of recent indexed snippets and their directions.

Do not send the full body in this phase.

### 9.2 Identity minimization

Email addresses and display names should be excluded unless a later measured use case proves they materially improve classification. Structural properties such as `ReplyDiscouragedSender` should be sent as booleans instead of exposing the raw address where possible.

### 9.3 Storage

The shadow layer must not persist prompt text, subject, snippet, or model rationale in AI usage telemetry. Existing telemetry should retain only operational metrics such as operation type, model, tokens, duration, success/failure, and estimated cost.

Private message content must never be used to derive shared global rules or shared training data inside Nexo Intelligence.

## 10. Prompt-injection resistance

All email content is untrusted data.

The semantic provider instructions must explicitly state that:

- message subjects/snippets are data to classify, never instructions to the model;
- instructions embedded in mail must not override the system task;
- the model must return only the requested structured classification;
- it must not execute tools, follow links, send mail, reveal secrets, or obey requests contained inside the message;
- it must not invent actions, deadlines, facts, documents, or commitments.

NexoMail already applies this principle in its AI mail-insight features; the semantic layer must preserve or strengthen it.

## 11. Batching strategy

The public semantic interface accepts a collection so callers are not forced into one external request per message.

The first OpenAI adapter should:

- batch candidates with bounded prompt size;
- preserve correlation IDs;
- use deterministic serialization and constrained output;
- split batches when the configured prompt limit would be exceeded;
- avoid sending candidates already resolved by the deterministic core;
- cap concurrency to prevent burst cost and provider throttling.

Exact batch size is an implementation/calibration parameter and must not be encoded as a product-level invariant.

## 12. Existing AI infrastructure reuse

NexoMail already contains:

- `AiResponseClient` for Responses API transport;
- `AiWritingOptions` for provider configuration;
- `IAiUsageTracker` for token, duration, success/failure, and cost telemetry;
- prompt-safety patterns in `AiMailInsightsService`.

The first semantic adapter may reuse these components.

Operation type for telemetry should be distinct, for example:

```text
intelligence_semantic_classification
```

No API key, model credential, or provider secret is introduced into Nexo Intelligence contracts.

## 13. Shadow semantic service

A separate orchestration service should:

1. read local-index Intelligence snapshots;
2. select only results marked `RequiresSemanticReview`;
3. obtain the bounded semantic context from the same local index;
4. build provider-neutral semantic candidates;
5. invoke `ISemanticCommunicationAnalyzer`;
6. return semantic shadow snapshots;
7. never mutate mail or conversation state.

Conceptual result:

```csharp
public sealed record SemanticIntelligenceSnapshot(
    Guid AccountId,
    string ConversationId,
    string LatestMessageId,
    DateTimeOffset LatestActivityAt,
    SemanticActionabilityAssessment Assessment,
    string ProviderVersion);
```

The exact public provider/version metadata must avoid leaking credentials or sensitive configuration.

## 14. Diagnostic endpoint

A new authenticated shadow endpoint may be added under the existing `/api/mail/intelligence` namespace.

Recommended shape:

```text
GET /api/mail/intelligence/semantic-shadow
```

Optional account filtering may mirror the comparison endpoint.

The endpoint must:

- require authentication through the existing `/mail` group;
- operate on the current user's active comparable accounts;
- return semantic assessments and aggregate counts;
- not change `/api/mail/control-center`;
- not persist classifications;
- not initiate any mail mutation.

Before enabling real provider calls by default, the endpoint should support a deterministic fake/test implementation or an explicit configuration gate so development and CI do not incur external API cost.

## 15. Aggregate diagnostics

The semantic shadow result should make calibration possible without requiring exposure of message content in logs.

Recommended aggregate metrics:

- candidate count;
- classified action count;
- classified no-action count;
- uncertain count;
- counts by action type;
- confidence buckets;
- deadline-detected count;
- provider failures;
- parsing failures;
- total model requests/batches;
- token/cost metrics through existing telemetry.

Per-item shadow results may be returned to the authenticated developer endpoint during development, but subjects/snippets should not be copied into persistent diagnostics.

## 16. Future hybrid orchestration

This phase stops before hybrid operational activation.

A later `HybridCommunicationIntelligenceService` may combine:

1. deterministic actionability;
2. semantic assessment only when the core abstains;
3. deterministic conversation-state rules;
4. deterministic priority scoring over the resolved actionability;
5. explicit confidence and reason provenance.

Expected precedence:

```text
explicit deterministic terminal evidence
    > deterministic safe structural decision
    > trusted semantic decision above calibrated threshold
    > semantic uncertainty / review required
```

A model must never override explicit deterministic facts such as `Resolved` or `Cancelled` evidence.

## 17. Failure behavior

Semantic provider failure must degrade safely.

If the provider is unavailable, times out, returns malformed output, exceeds rate limits, or is not configured:

- deterministic results continue to work;
- candidates remain `RequiresSemanticReview`;
- no conversation is silently promoted to actionable;
- no existing pending item is deleted because of provider failure;
- the shadow endpoint reports provider/parse failure diagnostically;
- the visible Control Center remains unaffected in this phase.

## 18. Testing strategy

Implementation follows TDD.

Required tests include:

### Contract tests

- provider-neutral semantic contracts exist;
- no OpenAI types leak into Application;
- supported action types are constrained.

### Candidate mapping tests

- only `RequiresSemanticReview` conversations are selected;
- full body is not included;
- subject and snippet are bounded;
- identity is minimized;
- recent thread excerpts preserve chronological/directional context;
- other users and inactive accounts are excluded.

### Analyzer adapter tests

- valid structured provider response maps correctly;
- `None`, actionable types, and `Unknown` parse correctly;
- malformed output becomes uncertain;
- missing correlation is detected;
- prompt content is treated as data;
- batching preserves correlation and ordering.

### Shadow orchestration tests

- deterministic pending results do not call the semantic analyzer;
- semantic candidates do call it;
- provider failure leaves them unresolved;
- no mail mutations occur;
- account/user isolation is preserved.

### Regression workflows

At minimum preserve GREEN status for:

- Nexo Intelligence smoke;
- Control Center smoke including frontend build;
- Commercial smoke and complete solution build;
- Draft lifecycle smoke.

The existing unrelated standalone frontend commercial-feature-gate failure remains outside this semantic-layer scope unless separately requested.

## 19. Rollout sequence

### Phase 0.4A — contracts and fake analyzer

Create semantic contracts, candidate mapper, fake analyzer, and shadow orchestration. No external API calls.

### Phase 0.4B — OpenAI shadow adapter

Use the existing AI transport/telemetry with strict JSON classification, batching, privacy minimization, and safety instructions.

### Phase 0.4C — real-data shadow calibration

Run authenticated diagnostics on the user's local corpus and measure action/no-action/uncertain distributions, confidence, latency, and cost. Private mail remains validation data only.

### Phase 0.5 — hybrid decision design

Only after calibration, define confidence thresholds and rules for allowing semantic assessments to resolve deterministic abstentions.

### Later

- alternate/local semantic model adapters;
- event-level grouping across related threads;
- richer deadline/entity extraction;
- optional body retrieval only if measured benefit justifies the privacy/cost increase;
- per-user personalization isolated from global logic;
- Nexi explanations and suggested next actions based on Nexo Intelligence results.

## 20. Commercial and extraction boundary

Nexo Intelligence should remain separable from NexoMail.

Portable components:

- deterministic Core contracts and services;
- semantic contracts;
- provider adapter interface;
- hybrid orchestration contract;
- reason codes and confidence semantics.

NexoMail-specific components:

- `MailMessageIndex` adapters;
- account/user authorization;
- existing OpenAI configuration and telemetry wiring;
- API endpoints;
- Nexi/UI integration.

This boundary supports future packaging as a library, API, service, or open-core product without coupling the engine to NexoMail's mail-provider implementation.

## 21. Acceptance criteria for this design phase

The subsequent implementation plan is valid only if it preserves all of the following:

1. deterministic Core remains operational without an external model;
2. only explicit semantic-review candidates are sent to the semantic layer;
3. phase-one external payload excludes full message bodies;
4. Application contracts contain no OpenAI-specific types;
5. external provider failures degrade to unresolved/uncertain, never guessed actionability;
6. semantic decisions remain shadow-only in this phase;
7. `/api/mail/control-center` remains unchanged;
8. private mail is validation data, never a source of shared global rules;
9. token/cost/error telemetry is reused without storing prompt content;
10. implementation proceeds with TDD and full regression verification before any merge or production decision.
