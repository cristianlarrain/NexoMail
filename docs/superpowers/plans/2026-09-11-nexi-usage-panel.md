# Nexi Usage Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an Owner-only Nexi consumption subsystem that records real OpenAI usage, estimates USD/CLP cost, preserves 12 months of detail plus permanent monthly summaries, and exposes weekly/monthly/trial projections in an administrative dashboard.

**Architecture:** Centralize all Responses API calls behind a focused `AiResponseClient`, which records provider usage through `AiUsageTracker` without storing prompts, email content, or generated text. Persist detailed events and monthly aggregates in SQLite through EF Core, expose Owner-only query endpoints via `AiUsageAdminService`, and render the data in a dedicated React administration page.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core 10 + SQLite, OpenAI Responses API, React 19, TypeScript 5.9, React Router 7, TanStack Query 5, lucide-react, existing smoke-test scripts and GitHub Actions workflows.

**Spec:** `docs/superpowers/specs/2026-09-11-nexi-usage-panel-design.md`

## Global Constraints

- Panel access is exclusive to `IsOwner = true`; ordinary administrators do not gain access.
- Never persist prompts, email bodies, subjects, sender data, attachment content, Nexi responses, or user-generated text in usage telemetry.
- Detailed usage retention is 12 months; monthly aggregates remain permanent until the legal/commercial retention policy changes.
- Initial projected-cost thresholds are green `<= 1500 CLP`, yellow `1501–3000 CLP`, red `> 3000 CLP`; thresholds must be Owner-configurable.
- Token counts come from provider usage metadata when available; do not estimate token counts from characters.
- Unknown models record token usage but have a non-calculable cost rather than an invented cost.
- Every real provider call, including a retry that can be billed, records a separate usage event.
- Usage-tracking failures must never break the underlying Nexi feature.
- Historical event cost snapshots must not change when model prices or CLP/USD reference rates change later.
- No automatic Nexi suspension or billing by tokens is part of this implementation.

---

## File Structure

### Backend — new files

- `src/backend/NexoMail.Infrastructure/AiUsageModels.cs` — event, monthly summary, settings entities and DTO-like internal records.
- `src/backend/NexoMail.Infrastructure/AiUsageCostCalculator.cs` — model pricing lookup and deterministic USD/CLP cost calculation.
- `src/backend/NexoMail.Infrastructure/AiUsageTracker.cs` — writes events and monthly aggregates atomically.
- `src/backend/NexoMail.Infrastructure/AiResponseClient.cs` — one Responses API client that extracts output and usage and calls the tracker.
- `src/backend/NexoMail.Infrastructure/AiUsageAdminService.cs` — weekly/monthly/user/trial projections, semaphores, settings reads/writes.
- `src/backend/NexoMail.Infrastructure/AiUsageRetentionService.cs` — daily cleanup of detail older than 12 months.
- `src/backend/NexoMail.Api/AiUsageEndpoints.cs` — Owner-only administrative endpoints.
- `src/backend/NexoMail.CommercialSmokeTests/AiUsageSmoke.cs` — backend usage/cost/projection/retention smoke tests.

### Backend — modified files

- `src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs` — new DbSets, mappings, indexes.
- `src/backend/NexoMail.Infrastructure/AiWritingService.cs` — delegate provider calls to `AiResponseClient` and label operations.
- `src/backend/NexoMail.Infrastructure/AiSearchService.cs` — delegate provider calls and label `search_interpretation`.
- `src/backend/NexoMail.Infrastructure/AiContextService.cs` — delegate provider calls and label `mail_context_analysis`.
- `src/backend/NexoMail.Infrastructure/AiMailInsightsService.cs` — delegate provider calls and label summaries/reports/retries.
- `src/backend/NexoMail.Api/AiEndpoints.cs` — register the centralized client/tracker/query/retention services.
- `src/backend/NexoMail.Api/Program.cs` — map `AiUsageEndpoints` and register hosted retention cleanup if not registered by `AddNexoMailAi`.
- `src/backend/NexoMail.CommercialSmokeTests/Program.cs` — invoke `AiUsageSmoke`.
- `src/backend/NexoMail.Api/appsettings.json` — add server-side price/reference-rate defaults without secrets.

### Frontend — new files

- `src/frontend/src/api/aiUsageApi.ts` — typed Owner-only usage API client.
- `src/frontend/src/pages/AdminAiUsagePage.tsx` — cards, user table, detail drawer/section, thresholds form.
- `src/frontend/src/styles/ai-usage-admin.css` — panel styles using theme text tokens.
- `src/frontend/scripts/ai-usage-admin-smoke.mjs` — source-level route/access/privacy/UI regression assertions.

### Frontend — modified files

- `src/frontend/src/router.tsx` — add `/admin/ai-usage` route.
- `src/frontend/src/layouts/AppLayout.tsx` — show `Consumo Nexi` navigation only when effective access identifies Owner.
- `src/frontend/src/main.tsx` — import the new stylesheet if styles are imported centrally there.
- `.github/workflows/frontend-build.yml` — run the new frontend smoke script.
- `.github/workflows/commercial-smoke.yml` — ensure the backend smoke suite includes the new checks automatically through its existing executable.

---

### Task 1: Persist Usage Entities and Deterministic Cost Calculation

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/AiUsageModels.cs`
- Create: `src/backend/NexoMail.Infrastructure/AiUsageCostCalculator.cs`
- Modify: `src/backend/NexoMail.Infrastructure/Data/NexoMailDbContext.cs`
- Create: `src/backend/NexoMail.CommercialSmokeTests/AiUsageSmoke.cs`
- Modify: `src/backend/NexoMail.CommercialSmokeTests/Program.cs`
- Modify: `src/backend/NexoMail.Api/appsettings.json`

**Interfaces:**
- Produces: `AiUsageEventEntity`, `AiUsageMonthlySummaryEntity`, `AiUsageSettingsEntity`.
- Produces: `AiUsagePriceCatalogOptions` with `SectionName = "AI:UsagePricing"`, `ReferenceClpPerUsd`, and per-model input/output prices per million tokens.
- Produces: `AiUsageCostCalculator.Calculate(string model, long inputTokens, long outputTokens)` returning `AiUsageCostEstimate?`.

- [ ] **Step 1: Write failing smoke tests for schema and cost calculation**

Add `AiUsageSmoke.CostCalculationAsync()` with assertions equivalent to:

```csharp
var options = Options.Create(new AiUsagePriceCatalogOptions
{
    ReferenceClpPerUsd = 941.1m,
    Models = new Dictionary<string, AiUsageModelPrice>(StringComparer.OrdinalIgnoreCase)
    {
        ["gpt-5.6-luna"] = new(0.20m, 1.20m)
    }
});
var calculator = new AiUsageCostCalculator(options);
var cost = calculator.Calculate("gpt-5.6-luna", 1_000_000, 1_000_000);

Require(cost is not null, "Known model must have calculable cost.");
Require(cost!.EstimatedCostUsd == 1.40m, "Known model USD cost is wrong.");
Require(cost.EstimatedCostClp == decimal.Round(1.40m * 941.1m, 4), "CLP conversion is wrong.");
Require(calculator.Calculate("unknown-model", 1000, 1000) is null, "Unknown model must not invent cost.");
```

Also create an in-memory SQLite context, call `Database.EnsureCreatedAsync()`, and assert that the three new DbSets can insert/read an event, a monthly row, and global settings.

- [ ] **Step 2: Run the smoke suite and verify RED**

Run:

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
```

Expected: build/test failure because the usage types and calculator do not yet exist.

- [ ] **Step 3: Add entities and EF Core mappings**

Define focused entities similar to:

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

Map composite key `{ UserId, Year, Month }`, indexes from the spec, max lengths for technical strings, and a User foreign key for events. Do not add any prompt/message-content fields.

- [ ] **Step 4: Implement the cost calculator and configuration defaults**

Use exact per-million arithmetic:

```csharp
var inputUsd = inputTokens / 1_000_000m * price.InputUsdPerMillion;
var outputUsd = outputTokens / 1_000_000m * price.OutputUsdPerMillion;
var usd = inputUsd + outputUsd;
return new AiUsageCostEstimate(
    price.InputUsdPerMillion,
    price.OutputUsdPerMillion,
    usd,
    options.ReferenceClpPerUsd,
    decimal.Round(usd * options.ReferenceClpPerUsd, 4));
```

Add non-secret defaults to `appsettings.json`:

```json
"AI": {
  "Model": "gpt-5.6-luna",
  "UsagePricing": {
    "ReferenceClpPerUsd": 941.1,
    "Models": {
      "gpt-5.6-luna": {
        "InputUsdPerMillion": 0.20,
        "OutputUsdPerMillion": 1.20
      }
    }
  }
}
```

- [ ] **Step 5: Run smoke tests and verify GREEN**

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
```

Expected: PASS for schema/cost checks and all prior commercial smokes.

- [ ] **Step 6: Commit**

```powershell
git add src/backend/NexoMail.Infrastructure src/backend/NexoMail.CommercialSmokeTests src/backend/NexoMail.Api/appsettings.json
git commit -m "feat: add Nexi usage cost model"
```

---

### Task 2: Record Usage Events and Maintain Monthly Aggregates Atomically

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/AiUsageTracker.cs`
- Modify: `src/backend/NexoMail.CommercialSmokeTests/AiUsageSmoke.cs`

**Interfaces:**
- Consumes: `AiUsageCostCalculator` from Task 1.
- Produces: `IAiUsageTracker.RecordAsync(AiUsageRecord record, CancellationToken ct)`.
- Produces: `AiUsageRecord(Guid UserId, string OperationType, string Model, long InputTokens, long OutputTokens, long? ReasoningTokens, long DurationMs, bool Succeeded, string? ErrorCategory, DateTimeOffset OccurredAt)`.

- [ ] **Step 1: Add failing tracker tests**

Test that two records on the same user/month create two event rows but one monthly row with summed operations/tokens/costs and one active day. Add a third event on another day and assert `ActiveDays == 2`.

```csharp
await tracker.RecordAsync(new AiUsageRecord(
    user.Id, "mail_summary", "gpt-5.6-luna", 1000, 200, null, 120, true, null,
    new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero)), CancellationToken.None);
```

Also test a record for an unknown model: tokens persist, nullable cost fields stay null, and monthly numeric cost is unchanged.

- [ ] **Step 2: Run smoke tests and verify RED**

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
```

Expected: FAIL because tracker interfaces/types do not exist.

- [ ] **Step 3: Implement transactional tracking**

`AiUsageTracker.RecordAsync` must:

1. calculate cost once;
2. begin a DB transaction;
3. insert the detailed event;
4. load or create the monthly summary;
5. increment operation/success/failure/tokens/cost;
6. derive `ActiveDays` from distinct detail days for that user/month after inserting the event;
7. save and commit.

Keep telemetry failure handling outside this class; this class should throw on persistence failure so `AiResponseClient` can log and swallow it.

- [ ] **Step 4: Run smoke tests and verify GREEN**

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/backend/NexoMail.Infrastructure/AiUsageTracker.cs src/backend/NexoMail.CommercialSmokeTests/AiUsageSmoke.cs
git commit -m "feat: track Nexi usage events"
```

---

### Task 3: Centralize OpenAI Responses Calls and Capture Real Provider Usage

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

- [ ] **Step 1: Add failing parsing/tracking tests around a fake HTTP handler**

Return a Responses API-like payload:

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

Assert `AiResponseClient` returns `resultado` and records exactly one event with the supplied operation type and token counts. Add a non-2xx test that records a generic failure category without storing response bodies.

- [ ] **Step 2: Run smoke tests and verify RED**

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
```

Expected: FAIL because `AiResponseClient` is missing.

- [ ] **Step 3: Implement `AiResponseClient`**

The client should build the existing Responses request shape and extract output/usage centrally. Wrap telemetry only:

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

Do not swallow provider errors. Preserve the existing `HttpRequestException` behavior expected by API endpoints.

- [ ] **Step 4: Replace duplicated provider-call code in all four AI services**

Map operations explicitly:

```text
AiSearchService.InterpretAsync -> search_interpretation
AiContextService.AnalyzeAsync -> mail_context_analysis
AiMailInsightsService.SummarizeMessageAsync(includeThread=false) -> mail_summary
AiMailInsightsService.SummarizeMessageAsync(includeThread=true) -> thread_summary
AiMailInsightsService.GenerateReportAsync -> mail_report
AiWritingService write/rewrite/reply helpers -> writing_assistant
```

The compact retry inside report generation must make a second `SendAsync` call, producing a second billable event.

- [ ] **Step 5: Register new services in `AddNexoMailAi`**

```csharp
services.Configure<AiUsagePriceCatalogOptions>(configuration.GetSection("AI:UsagePricing"));
services.AddScoped<AiUsageCostCalculator>();
services.AddScoped<IAiUsageTracker, AiUsageTracker>();
services.AddScoped<AiResponseClient>();
```

Keep the existing named `OpenAI` client configuration.

- [ ] **Step 6: Run backend smoke tests and build**

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
dotnet build NexoMail.sln
```

Expected: both commands succeed.

- [ ] **Step 7: Commit**

```powershell
git add src/backend/NexoMail.Infrastructure src/backend/NexoMail.Api/AiEndpoints.cs src/backend/NexoMail.CommercialSmokeTests/AiUsageSmoke.cs
git commit -m "refactor: centralize Nexi provider usage tracking"
```

---

### Task 4: Implement Owner Analytics, Trial Projections, Settings, and Retention

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/AiUsageAdminService.cs`
- Create: `src/backend/NexoMail.Infrastructure/AiUsageRetentionService.cs`
- Modify: `src/backend/NexoMail.CommercialSmokeTests/AiUsageSmoke.cs`
- Modify: `src/backend/NexoMail.Api/AiEndpoints.cs`

**Interfaces:**
- Produces: `AiUsageAdminService.GetSummaryAsync(AiUsagePeriod period, CancellationToken ct)`.
- Produces: `AiUsageAdminService.GetUsersAsync(string? sort, CancellationToken ct)`.
- Produces: `AiUsageAdminService.GetUserAsync(Guid userId, CancellationToken ct)`.
- Produces: `AiUsageAdminService.GetSettingsAsync(CancellationToken ct)`.
- Produces: `AiUsageAdminService.UpdateSettingsAsync(decimal greenMaxClp, decimal yellowMaxClp, decimal referenceClpPerUsd, CancellationToken ct)`.
- Produces: `AiUsageRetentionService` as a `BackgroundService` with one cleanup pass method that can be smoke-tested directly.

- [ ] **Step 1: Add failing analytics/projection tests**

Seed events spanning current week, previous week, and month. Assert:

```csharp
Require(summary.CurrentCostClp == expectedCurrent, "Current period cost is wrong.");
Require(summary.PreviousCostClp == expectedPrevious, "Previous period cost is wrong.");
Require(summary.VariationPercent == expectedVariation, "Week comparison is wrong.");
```

Seed an `admin_trial` subscription with `CurrentPeriodStart` and `TrialEndsAt`; assert accumulated trial cost, calendar-day projection, `IsInitialProjection` before one full day, seven-day trend after seven days, and threshold status.

Test settings validation:

```csharp
await RequireThrowsAsync<InvalidOperationException>(() =>
    service.UpdateSettingsAsync(3000m, 1500m, 941.1m, CancellationToken.None));
```

- [ ] **Step 2: Run smoke tests and verify RED**

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
```

Expected: FAIL because admin analytics service does not exist.

- [ ] **Step 3: Implement aggregate queries and projection rules**

For active trials, derive:

```csharp
var totalDays = Math.Max(1d, (trialEndsAt - trialStart).TotalDays);
var elapsedDays = Math.Max(1d / 24d, (now - trialStart).TotalDays);
var projected = accumulatedClp / (decimal)elapsedDays * (decimal)totalDays;
```

Mark `< 1 full day` as initial. Once seven calendar days of usage window exist, compute a separate last-seven-days daily average and trend indicator. For non-trial users, label projection as 30-day monthly estimate.

The cost-light must be computed from projected CLP and persisted settings, not hard-coded in the query service.

- [ ] **Step 4: Implement settings persistence**

Ensure singleton row `Id = 1` exists on first read. Validation:

```csharp
if (greenMaxClp < 0 || yellowMaxClp <= greenMaxClp)
    throw new InvalidOperationException("Los umbrales de consumo no son válidos.");
if (referenceClpPerUsd <= 0)
    throw new InvalidOperationException("El tipo de cambio de referencia debe ser mayor que cero.");
```

When the Owner changes the reference exchange rate, update both `AiUsageSettings` and the options source used for *future* event cost snapshots through a dedicated runtime settings read in the calculator/tracker; do not rewrite historical events.

- [ ] **Step 5: Implement 12-month retention**

Expose an internal/testable cleanup method:

```csharp
public async Task<int> DeleteExpiredDetailAsync(DateTimeOffset now, CancellationToken ct)
{
    var cutoff = now.AddMonths(-12);
    return await database.AiUsageEvents.Where(x => x.OccurredAt < cutoff).ExecuteDeleteAsync(ct);
}
```

The hosted loop runs once after startup delay and then every 24 hours. It never deletes monthly summaries.

- [ ] **Step 6: Register admin/retention services and run tests**

```csharp
services.AddScoped<AiUsageAdminService>();
services.AddScoped<AiUsageRetentionService>();
services.AddHostedService<AiUsageRetentionHostedService>();
```

If a scoped DbContext prevents the same class from being both scoped/testable and hosted, keep `AiUsageRetentionService` scoped and create a small `AiUsageRetentionHostedService(IServiceScopeFactory, ILogger<...>)` wrapper.

Run:

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
```

Expected: PASS.

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

**Interfaces:**
- Consumes: `AiUsageAdminService` from Task 4.
- Produces endpoints:
  - `GET /api/ai-usage/admin/summary?period=week|month`
  - `GET /api/ai-usage/admin/users?sort=projected|accumulated`
  - `GET /api/ai-usage/admin/users/{userId:guid}`
  - `GET /api/ai-usage/admin/settings`
  - `PATCH /api/ai-usage/admin/settings`

- [ ] **Step 1: Add failing source/security assertions**

Extend smoke checks to read `AiUsageEndpoints.cs` and assert the Owner check is server-side and every route is under an authenticated group. The helper must query the current active user and require `IsOwner` explicitly, not merely `IsAdministrator`.

Use a helper shaped like:

```csharp
private static async Task<bool> IsOwnerAsync(NexoMailDbContext db, IUserContext userContext, CancellationToken ct) =>
    await db.Users.AsNoTracking().AnyAsync(x => x.Id == userContext.UserId && x.IsActive && x.IsOwner, ct);
```

- [ ] **Step 2: Run smoke tests and verify RED**

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
```

Expected: FAIL because endpoint file/routes do not exist.

- [ ] **Step 3: Implement endpoints with one Owner gate per handler/group**

Map a group from the authenticated `/api` root:

```csharp
var usage = api.MapGroup("/ai-usage/admin").RequireAuthorization();
```

Each handler returns `Results.Forbid()` when `IsOwnerAsync` is false. Return 400 for invalid periods/settings and 404 for unknown users. DTOs contain only metrics and user identity fields required by the admin table; they contain no message/prompt/output content.

- [ ] **Step 4: Map endpoints in `Program.cs`**

Immediately after commercial endpoint mapping:

```csharp
NexoMail.Api.CommercialEndpoints.MapNexoMailCommercial(api);
NexoMail.Api.AiUsageEndpoints.Map(api);
```

- [ ] **Step 5: Run backend verification**

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
dotnet build NexoMail.sln
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/backend/NexoMail.Api src/backend/NexoMail.CommercialSmokeTests/AiUsageSmoke.cs
git commit -m "feat: expose Owner Nexi usage API"
```

---

### Task 6: Add Typed Frontend API and Owner-Only Route/Navigation

**Files:**
- Create: `src/frontend/src/api/aiUsageApi.ts`
- Create: `src/frontend/src/pages/AdminAiUsagePage.tsx`
- Modify: `src/frontend/src/router.tsx`
- Modify: `src/frontend/src/layouts/AppLayout.tsx`
- Create: `src/frontend/scripts/ai-usage-admin-smoke.mjs`

**Interfaces:**
- Produces: `aiUsageApi.summary(period)`, `aiUsageApi.users(sort)`, `aiUsageApi.user(userId)`, `aiUsageApi.settings()`, `aiUsageApi.updateSettings(payload)`.
- Route: `/admin/ai-usage`.

- [ ] **Step 1: Write failing frontend smoke assertions**

The script must assert:

```js
assert(router.includes("/admin/ai-usage"), 'Owner usage route is required')
assert(layout.includes('Consumo Nexi'), 'Owner navigation entry is required')
assert(page.includes('Costo estimado'), 'Usage page must show cost metrics')
assert(page.includes('Proyección'), 'Usage page must show projections')
assert(!page.includes('prompt'), 'Usage page must not expose prompts')
```

Also require that the navigation condition checks the effective commercial access code for Owner and does not render the entry merely because `adminStatus()` is true.

- [ ] **Step 2: Run smoke script and verify RED**

```powershell
node src/frontend/scripts/ai-usage-admin-smoke.mjs
```

Expected: FAIL because page/API/route do not exist.

- [ ] **Step 3: Implement typed `aiUsageApi.ts`**

Use existing `csrfFetch`/JSON patterns. Define types for summary, user row, user detail, settings and update payload. Fail with the backend `error` message when available.

- [ ] **Step 4: Add route and Owner-aware navigation**

Use existing `commercialApi.subscription()` data in `AppLayout`:

```tsx
const isOwner = commercialSubscription?.effectivePlanCode === 'owner'
```

Add a `Consumo Nexi` navigation item only when `isOwner` is true. Do not expose it to ordinary admins. Add the route in `router.tsx`; the backend remains the security boundary even if someone manually enters the URL.

- [ ] **Step 5: Build the minimal page shell**

Create `AdminAiUsagePage` with TanStack Query calls and these states: loading, backend error/403, empty data, populated data. Initially render the summary cards and raw user table; settings/detail interactions are Task 7.

- [ ] **Step 6: Run smoke and TypeScript build**

```powershell
node src/frontend/scripts/ai-usage-admin-smoke.mjs
cd src/frontend
pnpm build
```

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/frontend/src/api/aiUsageApi.ts src/frontend/src/pages/AdminAiUsagePage.tsx src/frontend/src/router.tsx src/frontend/src/layouts/AppLayout.tsx src/frontend/scripts/ai-usage-admin-smoke.mjs
git commit -m "feat: add Owner Nexi usage route"
```

---

### Task 7: Complete Dashboard UI, User Detail, Threshold Editing, and Theme Safety

**Files:**
- Modify: `src/frontend/src/pages/AdminAiUsagePage.tsx`
- Create: `src/frontend/src/styles/ai-usage-admin.css`
- Modify: `src/frontend/src/main.tsx`
- Modify: `src/frontend/scripts/ai-usage-admin-smoke.mjs`

**Interfaces:**
- Consumes: all `aiUsageApi` methods from Task 6.
- Produces: completed Owner dashboard.

- [ ] **Step 1: Extend failing smoke assertions for complete UI**

Require visible labels/controls for:

```text
Esta semana
Semana anterior
Variación semanal
Este mes
Usuarios activos
Costo promedio
Costo acumulado
Proyección 30 días / Proyección de prueba
Operaciones
Tokens
Ver detalle
Umbral verde
Umbral amarillo
```

Assert stylesheet contains `var(--muted-foreground)` for secondary text and does not contain `color: var(--muted)`.

- [ ] **Step 2: Run smoke and verify RED**

```powershell
node src/frontend/scripts/ai-usage-admin-smoke.mjs
```

Expected: FAIL until the complete dashboard is implemented.

- [ ] **Step 3: Implement summary cards and user table**

Use `Intl.NumberFormat('es-CL', { style: 'currency', currency: 'CLP', maximumFractionDigits: 0 })` for CLP and compact integer formatting for tokens. Add semantic status text alongside color so the traffic light is not color-only.

Table sort options:

```tsx
<option value="projected">Mayor costo proyectado</option>
<option value="accumulated">Mayor costo acumulado</option>
```

- [ ] **Step 4: Implement selected-user detail**

On `Ver detalle`, query `aiUsageApi.user(id)` and show 30-day daily usage, operation distribution, trial dates, accumulated cost, average daily cost, main projection, optional seven-day trend, and status. Do not show message metadata.

- [ ] **Step 5: Implement threshold settings form**

Load settings and submit:

```ts
await aiUsageApi.updateSettings({
  greenMaxClp: Number(greenMaxClp),
  yellowMaxClp: Number(yellowMaxClp),
  referenceClpPerUsd: Number(referenceClpPerUsd),
})
```

On success invalidate summary/users/user/settings queries so semaphores and future cost reference display refresh without exposing any sensitive data.

- [ ] **Step 6: Add theme-safe styling**

Use existing theme variables (`--foreground`, `--muted-foreground`, borders/background tokens). Do not set text to `var(--muted)`, because that token is a background color in dark mode. Keep cards/table responsive and use one-column layout at narrow widths.

- [ ] **Step 7: Run frontend smoke, lint/build**

```powershell
node src/frontend/scripts/ai-usage-admin-smoke.mjs
cd src/frontend
pnpm build
```

Expected: PASS. If `pnpm lint` is currently green project-wide, run it too; do not turn unrelated existing lint debt into this feature's scope.

- [ ] **Step 8: Commit**

```powershell
git add src/frontend/src/pages/AdminAiUsagePage.tsx src/frontend/src/styles/ai-usage-admin.css src/frontend/src/main.tsx src/frontend/scripts/ai-usage-admin-smoke.mjs
git commit -m "feat: complete Nexi usage dashboard"
```

---

### Task 8: Wire CI Regression Coverage and Perform Exact-Final Verification

**Files:**
- Modify: `.github/workflows/frontend-build.yml`
- Verify: `.github/workflows/commercial-smoke.yml`
- Verify: all files changed in Tasks 1–7

**Interfaces:**
- Produces: CI coverage for usage panel smoke and existing builds.

- [ ] **Step 1: Add the frontend usage smoke to the existing frontend workflow**

Add a named step alongside current commercial/UI checks:

```yaml
- name: Verify Nexi usage admin panel
  run: node scripts/ai-usage-admin-smoke.mjs
```

Use the workflow's existing `working-directory: src/frontend` convention if configured at job level.

- [ ] **Step 2: Confirm backend workflow already executes the commercial smoke executable**

If `commercial-smoke.yml` runs:

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
```

no new workflow command is needed because `Program.cs` now invokes `AiUsageSmoke`. Only modify the workflow if that assumption is false.

- [ ] **Step 3: Run exact-final local verification**

From repository root:

```powershell
dotnet run --project src/backend/NexoMail.CommercialSmokeTests/NexoMail.CommercialSmokeTests.csproj
dotnet build NexoMail.sln
node src/frontend/scripts/ai-usage-admin-smoke.mjs
cd src/frontend
pnpm build
```

Expected: every command passes on the exact final tree.

- [ ] **Step 4: Run privacy scan over the new telemetry model**

Use repository search to confirm no telemetry entity/property contains content-bearing fields:

```powershell
git grep -n -E "(Prompt|EmailBody|HtmlBody|Subject|Sender|GeneratedText|ResponseText)" -- src/backend/NexoMail.Infrastructure/AiUsage* src/backend/NexoMail.Api/AiUsageEndpoints.cs
```

Expected: no matches except explanatory comments/tests that explicitly assert absence; production usage entities/DTOs must have none of these fields.

- [ ] **Step 5: Review git diff and status**

```powershell
git status --short
git diff --check
git log -8 --oneline
```

Expected: no unintended files, no whitespace errors, and no modification to unrelated local files such as an untracked `nexo.png`.

- [ ] **Step 6: Commit CI wiring**

```powershell
git add .github/workflows/frontend-build.yml
git commit -m "test: cover Nexi usage dashboard in CI"
```

- [ ] **Step 7: Verify CI on the final commit before completion claim**

Wait for/fetch the GitHub Actions runs attached to the exact final commit and require the backend commercial smoke and frontend build workflows to be green. Do not claim implementation complete while either workflow is pending or failing.

---

## Plan Self-Review

### Spec coverage

- Centralized provider usage capture: Tasks 2–3.
- Real input/output token counts and retry accounting: Task 3.
- Per-model historical price snapshots and CLP conversion: Tasks 1–2.
- Weekly/monthly summary and prior-week comparison: Task 4.
- Trial accumulated cost, 30-day/end-of-trial projection, seven-day trend and initial-projection state: Task 4.
- Configurable 1500/3000 CLP semaphore thresholds: Tasks 4 and 7.
- Owner-only backend and UI: Tasks 5–7.
- 12-month detail plus permanent monthly aggregates: Tasks 2 and 4.
- No prompt/email/generated-content telemetry: Tasks 1, 3, 5 and final privacy scan.
- Telemetry failure does not break Nexi: Task 3.
- Light/dark mode safety: Task 7.
- CI and exact-final verification: Task 8.

### Explicit non-goals preserved

No automatic suspension, user-facing token counters, token billing, Mercado Pago changes, model-price editor, email alerts, NexoMail Empresas work, or database-engine migration are introduced by this plan.
