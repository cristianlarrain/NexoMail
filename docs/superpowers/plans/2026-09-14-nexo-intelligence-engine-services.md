# Nexo Intelligence Engine Services Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the first reusable, provider-agnostic Nexo Intelligence Engine services for actionability, conversation work state, priority and explainable orchestration without replacing the current Control Center classifier.

**Architecture:** Public contracts live in `NexoMail.Application.Intelligence`; deterministic baseline implementations live in `NexoMail.Infrastructure.Intelligence`. A dedicated console smoke-test project exercises behavior independently from Gmail, EF Core and user-specific data, and its own GitHub Actions workflow runs on every relevant change.

**Tech Stack:** .NET 10, C#, existing NexoMail Application/Infrastructure projects, console smoke tests, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-14-nexo-intelligence-engine-services-design.md`

## Global Constraints

- No global rule may depend on names, addresses, domains, institutions or exact subjects from development accounts.
- Gmail is an adapter, never a dependency of the intelligence core.
- Conversation state is deterministic and auditable.
- No new external AI provider, fine-tuning, embedding store, database migration or production integration in Phase 1.
- Every decision exposes reason codes.
- Existing Control Center behavior remains untouched in this phase.

---

### Task 1: Intelligence test harness and public contracts

**Files:**
- Create: `src/backend/NexoMail.IntelligenceSmokeTests/NexoMail.IntelligenceSmokeTests.csproj`
- Create: `src/backend/NexoMail.IntelligenceSmokeTests/Program.cs`
- Create: `src/backend/NexoMail.Application/Intelligence/CommunicationModels.cs`
- Create: `src/backend/NexoMail.Application/Intelligence/IntelligenceReasonCodes.cs`
- Create: `src/backend/NexoMail.Application/Intelligence/IActionabilityAnalyzer.cs`
- Create: `src/backend/NexoMail.Application/Intelligence/IConversationStateResolver.cs`
- Create: `src/backend/NexoMail.Application/Intelligence/IPriorityScorer.cs`
- Create: `src/backend/NexoMail.Application/Intelligence/ICommunicationIntelligenceService.cs`
- Modify: `NexoMail.sln`
- Create: `.github/workflows/intelligence-smoke.yml`

**Interfaces:**
- Produces: `CommunicationMessage`, `CommunicationConversation`, `ConversationStateEvidence`, `ActionabilityAssessment`, `ConversationStateAssessment`, `PriorityAssessment`, `CommunicationIntelligenceResult`, `IActionabilityAnalyzer`, `IConversationStateResolver`, `IPriorityScorer`, `ICommunicationIntelligenceService`.

- [ ] **Step 1: Add a reflection smoke test that expects the public contracts to exist.**

```csharp
var applicationAssembly = typeof(NexoMail.Application.IUserContext).Assembly;
Ensure(applicationAssembly.GetType("NexoMail.Application.Intelligence.IActionabilityAnalyzer") is not null,
    "IActionabilityAnalyzer debe existir como contrato público independiente del proveedor.");
```

- [ ] **Step 2: Run `dotnet run --project src/backend/NexoMail.IntelligenceSmokeTests/NexoMail.IntelligenceSmokeTests.csproj` and verify RED because the contract is absent.**

- [ ] **Step 3: Add the contracts and models with provider-agnostic enums and records.**

```csharp
public interface IActionabilityAnalyzer
{
    ActionabilityAssessment Analyze(CommunicationConversation conversation);
}

public interface IConversationStateResolver
{
    ConversationStateAssessment Resolve(
        CommunicationConversation conversation,
        ActionabilityAssessment actionability,
        ConversationStateEvidence? evidence = null);
}

public interface IPriorityScorer
{
    PriorityAssessment Score(
        CommunicationConversation conversation,
        ActionabilityAssessment actionability,
        ConversationStateAssessment state);
}
```

- [ ] **Step 4: Run the intelligence smoke test and verify the contract test is GREEN.**

- [ ] **Step 5: Commit the test harness and public contracts.**

---

### Task 2: Deterministic actionability analyzer

**Files:**
- Modify: `src/backend/NexoMail.IntelligenceSmokeTests/Program.cs`
- Create: `src/backend/NexoMail.Infrastructure/Intelligence/DeterministicActionabilityAnalyzer.cs`

**Interfaces:**
- Consumes: `CommunicationConversation`.
- Produces: `ActionabilityAssessment Analyze(CommunicationConversation conversation)`.

- [ ] **Step 1: Add failing behavior tests for a direct unread received message, automated/bulk mail, own-account-only sent mail and external sent mail.**

```csharp
Ensure(directUnread.IsActionable && directUnread.ActionType == CommunicationActionType.Reply,
    "Un recibido directo y no leído debe ser accionable sin conocer remitente o dominio.");
Ensure(!bulk.IsActionable && bulk.ReasonCodes.Contains(IntelligenceReasonCodes.Bulk),
    "Una comunicación bulk debe descartarse mediante metadatos generales.");
Ensure(!ownOnly.IsActionable && ownOnly.ReasonCodes.Contains(IntelligenceReasonCodes.OwnAccountOnly),
    "Un envío exclusivamente a cuentas propias no debe esperar respuesta.");
Ensure(external.ActionType == CommunicationActionType.WaitForExternal,
    "Un envío a un tercero sin respuesta debe quedar esperando al externo.");
```

- [ ] **Step 2: Run the intelligence smoke test and verify RED because the deterministic analyzer does not exist.**

- [ ] **Step 3: Add the minimal analyzer using only direction, recipients and automation/list metadata.**

```csharp
if (latest.Direction == CommunicationDirection.Sent)
{
    var external = recipients.Any(x => !ownAddresses.Contains(Normalize(x)));
    return external
        ? ActionabilityAssessment.WaitingExternal()
        : ActionabilityAssessment.NotActionable(IntelligenceReasonCodes.OwnAccountOnly);
}
```

- [ ] **Step 4: Run the intelligence smoke test and verify GREEN.**

- [ ] **Step 5: Commit the actionability analyzer.**

---

### Task 3: Deterministic conversation state resolver

**Files:**
- Modify: `src/backend/NexoMail.IntelligenceSmokeTests/Program.cs`
- Create: `src/backend/NexoMail.Infrastructure/Intelligence/DeterministicConversationStateResolver.cs`

**Interfaces:**
- Consumes: conversation + actionability + optional `ConversationStateEvidence`.
- Produces: `ConversationStateAssessment`.

- [ ] **Step 1: Add failing tests for `PendingUser`, `WaitingExternal`, external reply transition, explicit `Resolved`/`Cancelled`, and overdue work.**

```csharp
Ensure(pending.State == ConversationWorkState.PendingUser, "El último recibido accionable debe quedar PendingUser.");
Ensure(waiting.State == ConversationWorkState.WaitingExternal, "El último enviado externo debe quedar WaitingExternal.");
Ensure(replied.State == ConversationWorkState.PendingUser, "Una respuesta externa posterior debe devolver el trabajo al usuario.");
Ensure(resolved.State == ConversationWorkState.Resolved, "La evidencia explícita de resolución debe cerrar el trabajo.");
```

- [ ] **Step 2: Run the smoke test and verify RED because the resolver is absent.**

- [ ] **Step 3: Implement deterministic precedence: Cancelled > Resolved > Overdue > PendingUser/WaitingExternal > New.**

- [ ] **Step 4: Run the smoke test and verify GREEN.**

- [ ] **Step 5: Commit the conversation-state resolver.**

---

### Task 4: Explainable priority scorer

**Files:**
- Modify: `src/backend/NexoMail.IntelligenceSmokeTests/Program.cs`
- Create: `src/backend/NexoMail.Infrastructure/Intelligence/DeterministicPriorityScorer.cs`

**Interfaces:**
- Consumes: conversation + actionability + state.
- Produces: normalized `PriorityAssessment` with score 0..100, band, reason codes and signal breakdown.

- [ ] **Step 1: Add failing tests proving non-actionable work scores zero and a recent direct unread request outranks an old read request.**

```csharp
Ensure(nonActionable.Score == 0 && nonActionable.Band == PriorityBand.Low,
    "Lo no accionable no debe consumir prioridad de trabajo.");
Ensure(recentDirectUnread.Score > oldRead.Score,
    "La prioridad no puede depender exclusivamente de la antigüedad.");
```

- [ ] **Step 2: Run the smoke test and verify RED because the scorer is absent.**

- [ ] **Step 3: Implement explicit bounded weights: pending-user, waiting-external, unread, direct-recipient, age buckets and deadline/overdue signals.**

- [ ] **Step 4: Run the smoke test and verify GREEN.**

- [ ] **Step 5: Commit the priority scorer.**

---

### Task 5: Orchestration service and engine versioning

**Files:**
- Modify: `src/backend/NexoMail.IntelligenceSmokeTests/Program.cs`
- Create: `src/backend/NexoMail.Infrastructure/Intelligence/CommunicationIntelligenceService.cs`

**Interfaces:**
- Consumes: the three analysis services.
- Produces: `CommunicationIntelligenceResult Analyze(CommunicationConversation conversation, ConversationStateEvidence? evidence = null)`.

- [ ] **Step 1: Add a failing end-to-end test that checks actionability, state, priority, reason codes and a non-empty engine version.**

```csharp
var result = service.Analyze(conversation);
Ensure(result.Actionability.IsActionable, "El orquestador debe conservar accionabilidad.");
Ensure(result.State.State == ConversationWorkState.PendingUser, "El orquestador debe resolver estado.");
Ensure(result.Priority.Score > 0, "El orquestador debe calcular prioridad.");
Ensure(!string.IsNullOrWhiteSpace(result.EngineVersion), "Todo resultado debe identificar versión del motor.");
```

- [ ] **Step 2: Run the smoke test and verify RED because the orchestrator is absent.**

- [ ] **Step 3: Implement the minimal composition service with engine version `nexo-intelligence/0.1-deterministic`.**

- [ ] **Step 4: Run the smoke test and verify GREEN.**

- [ ] **Step 5: Commit the orchestration service.**

---

### Task 6: Full verification and scope guard

**Files:**
- Review all files changed in this branch.

- [ ] **Step 1: Run `dotnet run --project src/backend/NexoMail.IntelligenceSmokeTests/NexoMail.IntelligenceSmokeTests.csproj --configuration Release`.**
- [ ] **Step 2: Run `dotnet build NexoMail.sln --configuration Release`.**
- [ ] **Step 3: Run existing Control Center smoke tests to prove no regression.**
- [ ] **Step 4: Inspect the branch diff and verify no Gmail classifier, database migration, frontend or production deployment files changed.**
- [ ] **Step 5: Verify no test or production rule contains personal names, personal domains, institutions or exact development-account subjects as decision criteria.**
- [ ] **Step 6: Report CI state and leave integration/PR/production unchanged until explicit authorization.**
