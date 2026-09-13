# Nexi Usage Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an Owner-only Nexi consumption subsystem that records real OpenAI usage, estimates USD/CLP cost, preserves 12 months of detail plus permanent monthly summaries, and exposes weekly/monthly/trial projections in an administrative dashboard.

**Architecture:** Centralize all Responses API calls behind `AiResponseClient`, which records provider usage through `AiUsageTracker` without storing prompts, email content, or generated text. Persist detailed events and monthly aggregates in SQLite through EF Core, calculate administrative projections in `AiUsageAdminService`, expose Owner-only endpoints, and render them in a dedicated React page.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core 10 + SQLite, OpenAI Responses API, React 19, TypeScript 5.9, React Router 7, TanStack Query 5, lucide-react, existing smoke-test scripts and GitHub Actions workflows.

**Spec:** `docs/superpowers/specs/2026-09-11-nexi-usage-panel-design.md`

## Global Constraints

- Access is exclusive to `IsOwner = true`; ordinary administrators do not gain access.
- Never persist prompts, email bodies, subjects, sender data, attachment content, Nexi responses, or user-generated text in usage telemetry.
- Detailed retention is 12 months; monthly aggregates remain permanent until the legal/commercial policy changes.
- Initial projected-cost thresholds are green `<= 1500 CLP`, yellow `1501–3000 CLP`, red `> 3000 CLP`; thresholds are Owner-configurable.
- Token counts come from provider usage metadata when available; never estimate them from characters.
- Unknown models record tokens but keep cost non-calculable.
- Every actual provider call, including a billable retry, records a separate event.
- Telemetry persistence failures must never break the underlying Nexi feature.
- Historical cost snapshots never change when prices or CLP/USD reference rates change later.
- No automatic Nexi suspension, token billing, Mercado Pago work, or user-facing usage counters are part of this block.

---

## File Map

### Backend — create
- `src/backend/NexoMail.Infrastructure/AiUsageModels.cs` — usage entities and internal records.
- `src/backend/NexoMail.Infrastructure/AiUsageCostCalculator.cs` — model price lookup and deterministic cost calculation.
- `src/backend/NexoMail.Infrastructure/AiUsageTracker.cs` — event + monthly aggregate persistence.
- `src/backend/NexoMail.Infrastructure/AiResponseClient.cs` — centralized Responses API calls and usage parsing.
- `src/backend/NexoMail.Infrastructure/AiUsageAdminService.cs` — summaries, projections, settings and per-user analytics.
- `src/backend/NexoMail.Infrastructure/AiUsageRetentionService.cs` — 12-month detail cleanup plus hosted wrapper.
- `src/backend/NexoMail.Api/AiUsageEndpoints.cs` — Owner-only admin endpoints.
- `src/backend/NexoMail.CommercialSmokeTests/AiUsageSmoke.cs` — usage/cost/projection/retention smoke tests.

### Backend — modify
- `src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs`
- `src/backend/NexoMail.Infrastructure/AiWritingService.cs`
- `src/backend/NexoMail.Infrastructure/AiSearchService.cs`
- `src/backend/NexoMail.Infrastructure/AiContextService.cs`
- `src/backend/NexoMail.Infrastructure/AiMailInsightsService.cs`
- `src/backend/NexoMail.Api/AiEndpoints.cs`
- `src/backend/NexoMail.Api/Program.cs`
- `src/backend/NexoMail.Api/appsettings.json`
- `src/backend/NexoMail.CommercialSmokeTests/Program.cs`
- `src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj` only if API-level authorization smoke requires an API project reference.

### Frontend — create
- `src/frontend/src/api/aiUsageApi.ts`
- `src/frontend/src/pages/AdminAiUsagePage.tsx`
- `src/frontend/src/styles/ai-usage-admin.css`
- `src/frontend/scripts/ai-usage-admin-smoke.mjs`

### Frontend — modify
- `src/frontend/src/router.tsx`
- `src/frontend/src/layouts/AppLayout.tsx`
- `src/frontend/src/main.tsx`
- `.github/workflows/frontend-build.yml`

---

### Task 1: Add Usage Storage and Deterministic Cost Calculation

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/AiUsageModels.cs`
- Create: `src/backend/NexoMail.Infrastructure/AiUsageCostCalculator.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs`
- Create: `src/backend/NexoMail.CommercialSmokeTests/AiUsageSmoke.cs`
- Modify: `src/backend/NexoMail.CommercialSmokeTests/Program.cs`
- Modify: `src/backend/NexoMail.Api/appsettings.json`

**Interfaces:**
- Produces: `AiUsageEventEntity`, `AiUsageMonthlySummaryEntity`, `AiUsageSettingsEntity`.
- Produces: `AiUsagePriceCatalogOptions` with `SectionName = "AI:UsagePricing"` and model prices per million tokens.
- Produces: `AiUsageCostCalculator.Calculate(string model, long inputTokens, long outputTokens, decimal clpPerUsd)` returning `AiUsageCostEstimate?`.

- [ ] **Step 1: Write the failing cost/schema smoke**

Add a test equivalent to:

```csharp
var calculator = new AiUsageCostCalculator(Options.Create(new AiUsagePriceCatalogOptions
{
    Models = new Dictionary<string, AiUsageModelPrice>(StringComparer.OrdinalIgnoreCase)
    {
        ["gpt-5.6-luna"] = new(0.20m, 1.20m)
    }
}));

var cost = calculator.Calculate("gpt-5.6-luna", 1_000_000, 1_000_000, 941.1m);
Require(cost is not null, "Known model must have calculable cost.");
Require(cost!.EstimatedCostUsd == 1.40m, "Known model USD cost is wrong.");
Require(cost.EstimatedCostClp == decimal.Round(1.40m * 941.1m, 4), "CLP conversion is wrong.");
Require(calculator.Calculate("unknown-model", 1000, 1000, 941.1m) is null, "Unknown model must not invent cost.");
```

Also create an in-memory SQLite context, run `EnsureCreatedAsync()`, and assert that an event, monthly summary and singleton settings row can be inserted/read.

- [ ] **Step 2: Run RED**

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
```

Expected: compile/test failure because usage types do not exist.

- [ ] **Step 3: Implement entities and EF mappings**

Use these core shapes:

```csharp
public sealed class AiUsageEventEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long? ReasoningTokens { get; set; }
    public long DurationMs { get; set; }
    public bool Succeeded { get; set; }
    public string? ErrorCategory { get; set; }
    public decimal? InputUsdPerMillion { get; set; }
    public decimal? OutputUsdPerMillion { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public decimal? ClpPerUsd { get; set; }
    public decimal? EstimatedCostClp { get; set; }
}

public sealed class AiUsageMonthlySummaryEntity
{
    public Guid UserId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public long OperationCount { get; set; }
    public long SuccessfulOperations { get; set; }
    public long FailedOperations { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public decimal EstimatedCostClp { get; set; }
    public int ActiveDays { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AiUsageSettingsEntity
{
    public int Id { get; set; } = 1;
    public decimal GreenMaxClp { get; set; } = 1500m;
    public decimal YellowMaxClp { get; set; } = 3000m;
    public decimal ReferenceClpPerUsd { get; set; } = 941.1m;
    public DateTimeOffset UpdatedAt { get; set; }
}
```

Map monthly composite key `{ UserId, Year, Month }`; event indexes `{ UserId, OccurredAt }`, `OccurredAt`, `{ OperationType, OccurredAt }`; and a User FK for event rows. No content-bearing fields are allowed.

- [ ] **Step 4: Implement model pricing**

```csharp
var inputUsd = inputTokens / 1_000_000m * price.InputUsdPerMillion;
var outputUsd = outputTokens / 1_000_000m * price.OutputUsdPerMillion;
var usd = inputUsd + outputUsd;
return new AiUsageCostEstimate(
    price.InputUsdPerMillion,
    price.OutputUsdPerMillion,
    usd,
    clpPerUsd,
    decimal.Round(usd * clpPerUsd, 4));
```

Add only model prices to server configuration; the initial exchange-rate fallback is used only to seed `AiUsageSettings`:

```json
"AI": {
  "Model": "gpt-5.6-luna",
  "UsagePricing": {
    "DefaultReferenceClpPerUsd": 941.1,
    "Models": {
      "gpt-5.6-luna": {
        "InputUsdPerMillion": 0.20,
        "OutputUsdPerMillion": 1.20
      }
    }
  }
}
```

- [ ] **Step 5: Run GREEN**

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
```

- [ ] **Step 6: Commit**

```powershell
git add src/backend/NexoMail.Infrastructure src/backend/NexoMail.CommercialSmokeTests src/backend/NexoMail.Api/appsettings.json
git commit -m "feat: add Nexi usage cost model"
```

---

### Task 2: Record Events and Maintain Monthly Aggregates Atomically

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/AiUsageTracker.cs`
- Modify: `src/backend/NexoMail.CommercialSmokeTests/AiUsageSmoke.cs`

**Interfaces:**
- Consumes: `AiUsageCostCalculator`.
- Produces: `IAiUsageTracker.RecordAsync(AiUsageRecord record, CancellationToken ct)`.
- Produces: `AiUsageRecord(Guid UserId, string OperationType, string Model, long InputTokens, long OutputTokens, long? ReasoningTokens, long DurationMs, bool Succeeded, string? ErrorCategory, DateTimeOffset OccurredAt)`.

- [ ] **Step 1: Write failing tracker tests**

Seed a user, then:

```csharp
await tracker.RecordAsync(new AiUsageRecord(
    user.Id,
    "mail_summary",
    "gpt-5.6-luna",
    1000,
    200,
    null,
    120,
    true,
    null,
    new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero)),
    CancellationToken.None);
```

Assert two same-month calls create two events but one accumulated monthly row. Add a third call on another calendar day and assert `ActiveDays == 2`. For an unknown model, assert tokens persist and cost fields remain null while monthly cost is unchanged.

- [ ] **Step 2: Run RED**

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
```

- [ ] **Step 3: Implement transactional tracking**

`RecordAsync` must:

```text
1. Load or create AiUsageSettings row Id=1.
2. Pass settings.ReferenceClpPerUsd into AiUsageCostCalculator.
3. Begin transaction.
4. Insert the detailed event with price/rate snapshots.
5. Load or create the monthly aggregate.
6. Increment operation/success/failure/token/cost totals.
7. Recompute active-day count for that user/month.
8. Save and commit.
```

Keep persistence exceptions visible to callers; `AiResponseClient` will be responsible for logging/swallowing telemetry failures.

- [ ] **Step 4: Run GREEN**

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
```

- [ ] **Step 5: Commit**

```powershell
git add src/backend/NexoMail.Infrastructure/AiUsageTracker.cs src/backend/NexoMail.CommercialSmokeTests/AiUsageSmoke.cs
git commit -m "feat: track Nexi usage events"
```

---

### Task 3: Centralize Responses API Calls and Capture Real Usage

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/AiResponseClient.cs`
- Modify: `src/backend/NexoMail.Infrastructure/AiWritingService.cs`
- Modify: `src/backend/NexoMail.Infrastructure/AiSearchService.cs`
- Modify: `src/backend/NexoMail.Infrastructure/AiContextService.cs`
- Modify: `src/backend/NexoMail.Infrastructure/AiMailInsightsService.cs`
- Modify: `src/backend/NexoMail.Api/AiEndpoints.cs`
- Modify: `src/backend/NexoMail.CommercialSmokeTests/AiUsageSmoke.cs`

**Interfaces:**
- Consumes: `IUserContext`, `IAiUsageTracker`, named `OpenAI` `HttpClient`.
- Produces: `AiResponseClient.SendAsync(AiResponseRequest request, CancellationToken ct)`.
- Produces: `AiResponseRequest(string OperationType, string Model, string Instructions, string Input, string ReasoningEffort, int MaxOutputTokens)`.
- Produces: `AiResponseResult(string Text, long InputTokens, long OutputTokens, long? ReasoningTokens)`.

- [ ] **Step 1: Write failing fake-HTTP parsing/tracking tests**

Return:

```json
{
  "output": [{"type":"message","content":[{"type":"output_text","text":"resultado"}]}],
  "usage": {
    "input_tokens": 123,
    "output_tokens": 45,
    "output_tokens_details": {"reasoning_tokens": 6}
  }
}
```

Assert one successful event is recorded with exact token counts. Add a non-2xx case that records a generic failure category but never stores provider response text.

- [ ] **Step 2: Run RED**

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
```

- [ ] **Step 3: Implement `AiResponseClient`**

Build the current Responses payload, time the request, parse output and usage, and report telemetry. Telemetry failure is isolated:

```csharp
try
{
    await tracker.RecordAsync(record, ct);
}
catch (Exception exception)
{
    logger.LogWarning(exception, "Nexi usage telemetry could not be persisted for {OperationType}.", request.OperationType);
}
```

Provider errors continue to surface as the same exception families expected by existing endpoints.

- [ ] **Step 4: Route all four existing AI services through the client**

Use stable operation types:

```text
AiSearchService.InterpretAsync -> search_interpretation
AiContextService.AnalyzeAsync -> mail_context_analysis
AiMailInsightsService.SummarizeMessageAsync(false) -> mail_summary
AiMailInsightsService.SummarizeMessageAsync(true) -> thread_summary
AiMailInsightsService.GenerateReportAsync -> mail_report
AiWritingService writing/rewrite/reply helpers -> writing_assistant
```

A compact retry inside report generation must call `SendAsync` again and therefore create a second event.

- [ ] **Step 5: Register services**

In `AddNexoMailAi`:

```csharp
services.Configure<AiUsagePriceCatalogOptions>(configuration.GetSection("AI:UsagePricing"));
services.AddScoped<AiUsageCostCalculator>();
services.AddScoped<IAiUsageTracker, AiUsageTracker>();
services.AddScoped<AiResponseClient>();
```

- [ ] **Step 6: Verify backend**

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
dotnet build NexoMail.sln
```

- [ ] **Step 7: Commit**

```powershell
git add src/backend/NexoMail.Infrastructure src/backend/NexoMail.Api/AiEndpoints.cs src/backend/NexoMail.CommercialSmokeTests/AiUsageSmoke.cs
git commit -m "refactor: centralize Nexi provider usage tracking"
```

---

### Task 4: Build Analytics, Trial Projection, Settings, and Retention

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/AiUsageAdminService.cs`
- Create: `src/backend/NexoMail.Infrastructure/AiUsageRetentionService.cs`
- Modify: `src/backend/NexoMail.CommercialSmokeTests/AiUsageSmoke.cs`
- Modify: `src/backend/NexoMail.Api/AiEndpoints.cs`

**Interfaces:**
- Produces: `GetSummaryAsync(AiUsagePeriod period, CancellationToken ct)`.
- Produces: `GetUsersAsync(string? sort, CancellationToken ct)`.
- Produces: `GetUserAsync(Guid userId, CancellationToken ct)`.
- Produces: `GetSettingsAsync(CancellationToken ct)`.
- Produces: `UpdateSettingsAsync(decimal greenMaxClp, decimal yellowMaxClp, decimal referenceClpPerUsd, CancellationToken ct)`.
- Produces: `AiUsageRetentionService.DeleteExpiredDetailAsync(DateTimeOffset now, CancellationToken ct)` and a small hosted wrapper running daily.

- [ ] **Step 1: Write failing analytics/projection/settings tests**

Seed current week, previous week and current month events; assert current cost, previous cost, variation, operations, active users and average cost.

Seed an `admin_trial` row with `CurrentPeriodStart` and `TrialEndsAt`; assert accumulated trial cost, calendar-day projection, `<1 full day` initial state, seven-day trend after sufficient history, and green/yellow/red classification.

Add invalid settings test:

```csharp
await RequireThrowsAsync<InvalidOperationException>(() =>
    service.UpdateSettingsAsync(3000m, 1500m, 941.1m, CancellationToken.None));
```

- [ ] **Step 2: Run RED**

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
```

- [ ] **Step 3: Implement aggregate and projection rules**

For active trials:

```csharp
var totalDays = Math.Max(1d, (trialEndsAt - trialStart).TotalDays);
var elapsedDays = Math.Max(1d / 24d, (now - trialStart).TotalDays);
var projected = accumulatedClp / (decimal)elapsedDays * (decimal)totalDays;
```

Mark the first full-day window as `IsInitialProjection`. After seven days, compute a separate last-seven-days projection/trend. Non-trial users receive a clearly labelled 30-day monthly estimate.

Semaphore status comes from persisted thresholds, never hard-coded in query code.

- [ ] **Step 4: Implement settings persistence**

Validation:

```csharp
if (greenMaxClp < 0 || yellowMaxClp <= greenMaxClp)
    throw new InvalidOperationException("Los umbrales de consumo no son válidos.");
if (referenceClpPerUsd <= 0)
    throw new InvalidOperationException("El tipo de cambio de referencia debe ser mayor que cero.");
```

Changing `ReferenceClpPerUsd` affects only future events because Task 2 reads the singleton row for every new usage record. Historical event snapshots remain untouched.

- [ ] **Step 5: Implement 12-month retention**

```csharp
public async Task<int> DeleteExpiredDetailAsync(DateTimeOffset now, CancellationToken ct)
{
    var cutoff = now.AddMonths(-12);
    return await database.AiUsageEvents
        .Where(x => x.OccurredAt < cutoff)
        .ExecuteDeleteAsync(ct);
}
```

The hosted wrapper creates a scope, runs cleanup, then waits 24 hours. It never deletes `AiUsageMonthlySummaries`.

- [ ] **Step 6: Register services and run GREEN**

```csharp
services.AddScoped<AiUsageAdminService>();
services.AddScoped<AiUsageRetentionService>();
services.AddHostedService<AiUsageRetentionHostedService>();
```

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
```

- [ ] **Step 7: Commit**

```powershell
git add src/backend/NexoMail.Infrastructure src/backend/NexoMail.Api/AiEndpoints.cs src/backend/NexoMail.CommercialSmokeTests/AiUsageSmoke.cs
git commit -m "feat: add Nexi usage analytics and retention"
```

---

### Task 5: Expose Strict Owner-Only Administrative API

**Files:**
- Create: `src/backend/NexoMail.Api/AiUsageEndpoints.cs`
- Modify: `src/backend/NexoMail.Api/Program.cs`
- Modify: `src/backend/NexoMail.CommercialSmokeTests/AiUsageSmoke.cs`
- Modify: `src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj` only if required to execute API authorization smoke directly.

**Interfaces:**
- Produces:
  - `GET /api/ai-usage/admin/summary?period=week|month`
  - `GET /api/ai-usage/admin/users?sort=projected|accumulated`
  - `GET /api/ai-usage/admin/users/{userId:guid}`
  - `GET /api/ai-usage/admin/settings`
  - `PATCH /api/ai-usage/admin/settings`

- [ ] **Step 1: Write failing Owner authorization tests**

The test must exercise the authorization helper or endpoint delegate with three seeded users and prove:

```text
Owner (IsOwner=true) -> allowed
Administrator only (IsAdministrator=true, IsOwner=false) -> forbidden
Normal user -> forbidden
```

The implementation must query the active current user and require `IsOwner` explicitly:

```csharp
private static async Task<bool> IsOwnerAsync(NexoMailDbContext db, IUserContext userContext, CancellationToken ct) =>
    await db.Users.AsNoTracking()
        .AnyAsync(x => x.Id == userContext.UserId && x.IsActive && x.IsOwner, ct);
```

If direct endpoint smoke requires it, add a project reference to `NexoMail.Api` rather than weakening this test into a text-only assertion.

- [ ] **Step 2: Run RED**

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
```

- [ ] **Step 3: Implement endpoints**

```csharp
var usage = api.MapGroup("/ai-usage/admin").RequireAuthorization();
```

Each handler returns `Results.Forbid()` when the Owner check fails. Invalid period/settings -> 400; unknown user -> 404. DTOs contain only administrative identity/plan/trial fields and numeric metrics, never mail or prompt content.

- [ ] **Step 4: Map in `Program.cs`**

```csharp
NexoMail.Api.CommercialEndpoints.MapNexoMailCommercial(api);
NexoMail.Api.AiUsageEndpoints.Map(api);
```

- [ ] **Step 5: Verify**

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
dotnet build NexoMail.sln
```

- [ ] **Step 6: Commit**

```powershell
git add src/backend/NexoMail.Api src/backend/NexoMail.CommercialSmokeTests
git commit -m "feat: expose Owner Nexi usage API"
```

---

### Task 6: Add Typed Frontend API, Route, and Owner Navigation

**Files:**
- Create: `src/frontend/src/api/aiUsageApi.ts`
- Create: `src/frontend/src/pages/AdminAiUsagePage.tsx`
- Modify: `src/frontend/src/router.tsx`
- Modify: `src/frontend/src/layouts/AppLayout.tsx`
- Create: `src/frontend/scripts/ai-usage-admin-smoke.mjs`

**Interfaces:**
- Produces: `aiUsageApi.summary(period)`, `users(sort)`, `user(userId)`, `settings()`, `updateSettings(payload)`.
- Produces route: `/admin/ai-usage`.

- [ ] **Step 1: Write failing frontend smoke**

```js
assert(router.includes("/admin/ai-usage"), 'Owner usage route is required')
assert(layout.includes('Consumo Nexi'), 'Owner navigation entry is required')
assert(page.includes('Costo estimado'), 'Usage page must show cost metrics')
assert(page.includes('Proyección'), 'Usage page must show projections')
```

Also assert navigation is based on effective Owner code, not generic administrator status.

- [ ] **Step 2: Run RED**

```powershell
node src/frontend/scripts/ai-usage-admin-smoke.mjs
```

- [ ] **Step 3: Implement typed API client**

Follow the existing `csrfFetch`/JSON error pattern. Define exact TypeScript types for summary, user row, user detail, settings and patch payload.

- [ ] **Step 4: Add route and Owner-only navigation visibility**

Use existing commercial subscription data:

```tsx
const isOwner = commercialSubscription?.effectivePlanCode === 'owner'
```

Render `Consumo Nexi` only for `isOwner`. The backend from Task 5 remains the actual security boundary if a non-Owner manually enters the URL.

- [ ] **Step 5: Implement minimal page shell**

Use TanStack Query for summary/users; provide loading, error/403, empty and populated states. Render summary cards and basic user rows. Full interactions come in Task 7.

- [ ] **Step 6: Verify**

```powershell
node src/frontend/scripts/ai-usage-admin-smoke.mjs
cd src/frontend
pnpm build
```

- [ ] **Step 7: Commit**

```powershell
git add src/frontend/src/api/aiUsageApi.ts src/frontend/src/pages/AdminAiUsagePage.tsx src/frontend/src/router.tsx src/frontend/src/layouts/AppLayout.tsx src/frontend/scripts/ai-usage-admin-smoke.mjs
git commit -m "feat: add Owner Nexi usage route"
```

---

### Task 7: Complete Dashboard UI, User Detail, Settings, and Theme Safety

**Files:**
- Modify: `src/frontend/src/pages/AdminAiUsagePage.tsx`
- Create: `src/frontend/src/styles/ai-usage-admin.css`
- Modify: `src/frontend/src/main.tsx`
- Modify: `src/frontend/scripts/ai-usage-admin-smoke.mjs`

**Interfaces:**
- Consumes: all Task 6 API methods.
- Produces: complete Owner dashboard.

- [ ] **Step 1: Extend failing UI smoke**

Require these labels/controls:

```text
Esta semana
Semana anterior
Variación semanal
Este mes
Usuarios activos
Costo promedio
Costo acumulado
Proyección
Operaciones
Tokens
Ver detalle
Umbral verde
Umbral amarillo
```

Also assert CSS uses `var(--muted-foreground)` for secondary text and does not contain `color: var(--muted)`.

- [ ] **Step 2: Run RED**

```powershell
node src/frontend/scripts/ai-usage-admin-smoke.mjs
```

- [ ] **Step 3: Implement summary cards and user table**

Format CLP with:

```ts
new Intl.NumberFormat('es-CL', {
  style: 'currency',
  currency: 'CLP',
  maximumFractionDigits: 0,
})
```

Add sort values `projected` and `accumulated`. Show semantic status text (`Verde`, `Amarillo`, `Rojo`) in addition to color.

- [ ] **Step 4: Implement user detail**

On `Ver detalle`, query `aiUsageApi.user(id)` and show 30-day daily usage, operation distribution, trial dates, accumulated cost, average daily cost, main projection, optional seven-day trend and status. No mail metadata.

- [ ] **Step 5: Implement settings form**

```ts
await aiUsageApi.updateSettings({
  greenMaxClp: Number(greenMaxClp),
  yellowMaxClp: Number(yellowMaxClp),
  referenceClpPerUsd: Number(referenceClpPerUsd),
})
```

On success invalidate summary/users/user/settings queries.

- [ ] **Step 6: Add responsive, theme-safe CSS**

Use existing theme tokens. Never use `color: var(--muted)` for text; use `--foreground` or `--muted-foreground`.

- [ ] **Step 7: Verify**

```powershell
node src/frontend/scripts/ai-usage-admin-smoke.mjs
cd src/frontend
pnpm build
```

- [ ] **Step 8: Commit**

```powershell
git add src/frontend/src/pages/AdminAiUsagePage.tsx src/frontend/src/styles/ai-usage-admin.css src/frontend/src/main.tsx src/frontend/scripts/ai-usage-admin-smoke.mjs
git commit -m "feat: complete Nexi usage dashboard"
```

---

### Task 8: CI Wiring and Exact-Final Verification

**Files:**
- Modify: `.github/workflows/frontend-build.yml`
- Verify: `.github/workflows/commercial-smoke.yml`
- Verify: all Task 1–7 files.

- [ ] **Step 1: Add frontend usage smoke to CI**

Add:

```yaml
- name: Verify Nexi usage admin panel
  run: node scripts/ai-usage-admin-smoke.mjs
```

Use the workflow's existing frontend working directory convention.

- [ ] **Step 2: Confirm backend workflow already runs the smoke executable**

If `commercial-smoke.yml` already runs:

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
```

no extra command is needed because `Program.cs` invokes `AiUsageSmoke`.

- [ ] **Step 3: Run exact-final verification**

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
dotnet build NexoMail.sln
node src/frontend/scripts/ai-usage-admin-smoke.mjs
cd src/frontend
pnpm build
```

Every command must pass on the exact final tree.

- [ ] **Step 4: Run privacy scan**

```powershell
git grep -n -E "(Prompt|EmailBody|HtmlBody|Subject|Sender|GeneratedText|ResponseText)" -- src/backend/NexoMail.Infrastructure/AiUsage* src/backend/NexoMail.Api/AiUsageEndpoints.cs
```

Expected: no content-bearing production telemetry fields. Explanatory test/comment matches must be inspected rather than ignored blindly.

- [ ] **Step 5: Review repository state**

```powershell
git status --short
git diff --check
git log -8 --oneline
```

Do not touch unrelated local files such as an untracked `nexo.png`.

- [ ] **Step 6: Commit CI wiring**

```powershell
git add .github/workflows/frontend-build.yml
git commit -m "test: cover Nexi usage dashboard in CI"
```

- [ ] **Step 7: Verify GitHub Actions on exact final commit**

Require both backend commercial smoke and frontend build workflows to be green before claiming the implementation complete.

---

## Self-Review Results

- Spec coverage: central capture, actual token usage, retry accounting, historical cost snapshots, weekly/monthly comparisons, trial projections, seven-day trend, configurable CLP semaphore, Owner-only access, 12-month retention, permanent monthly history, privacy minimization, theme safety and CI are each assigned to a concrete task.
- Type consistency: `AiUsageCostCalculator` receives the runtime CLP/USD rate explicitly; `AiUsageTracker` loads that rate from the singleton settings row for each new event, so Owner changes affect only future event snapshots.
- Authorization test strength: the plan requires executable Owner/admin/user authorization coverage rather than only source-text assertions.
- Placeholder scan: no `TBD`, `TODO`, “implement later”, or undefined follow-up step remains.
- Scope: Mercado Pago, Microsoft/IMAP, legal implementation, production migration and NexoMail Empresas remain outside this implementation plan exactly as specified.
