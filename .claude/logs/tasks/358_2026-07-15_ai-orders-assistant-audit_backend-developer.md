# TASK-358 — Backend: Block 7 pre-launch audit — AI Orders / AI Assistant

**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-357

Block 7 of the pre-launch audit (`eager-pondering-tower.md`). Scope: `Features/AiOrders`,
`Features/AiAssistant`, `Infrastructure/AI` (`ClaudeOrderAdvisor.cs`, `BusinessAssistant/`,
`SupplierAdvisor/`).

## Found + fixed

**P0 — nightly AI order generation never ran; weather context never populated.**
`worker/src/jobs/ai-order.job.ts` and `worker/src/jobs/weather-fetch.job.ts` both queried
`FROM stores`, a table renamed to `locations` in migration
`20260615183318_V4LocationsRename` (v4 Store→Location rename). Every nightly run
(`ai-order-cron`, `0 5 * * *`) threw on the first query and silently failed for every store —
per v2-spec §7 ("Щодня 05:00 → BullMQ job → AI агент для кожного магазину"), automatic AI
order generation has never worked in this environment; only the manual "Згенерувати зараз"
button (which takes `storeId` from the frontend and never hits this query) ever worked.
Same root cause in `weather-fetch.job.ts` (`weather-fetch-cron`, `0 6 * * *`) meant
`weather_data` was never populated, so every `AiOrderService.GenerateAsync` call fed Claude
an empty `WeatherForecast` array regardless of the manual/cron path — a real (if quiet) data
quality gap, not literal fake/placeholder data like Block 3's KI-007 dashboard issue, but the
same failure shape: a whole context source silently absent. Fixed both queries to
`FROM locations` (`Id`/`TenantId`/`Name`/`IsActive`/`Latitude`/`Longitude` columns unchanged —
only the table was renamed). `worker` `tsc --noEmit` clean. A third file with the same
`stores`/`catalog_products` typo, `notification.job.ts` (IoT alert store-name lookup), is
unrelated to AI Orders/Assistant — left as-is, already flagged separately in TASK-352's log.

**P1 — real N+1 in `AiOrderService.GetListAsync`** (previously flagged, not fixed, in
TASK-355's log). Looped over up to 30 suggestions and called `_repo.GetByIdAsync` per row
just to read `Items.Count` — up to 30 extra full round-trips (each pulling Store + Items +
Product) per list request. Fixed: `AiOrderRepository.GetListAsync` now eager-loads
`.Include(s => s.Items)` (no `ThenInclude(Product)` — count only, product name isn't shown in
the list DTO), and the service reads `s.Items.Count` directly. New regression test
(`GetListAsync_ReadsItemCountFromEagerLoadedList_NeverCallsGetByIdPerRow`) asserts
`GetByIdAsync` is never called from the list path.

**P2 — unbounded Claude API timeout on synchronous requests.** `ClaudeOrderAdvisor`,
`BusinessAssistantAdvisor`, and `SupplierAdvisor` all did `new AnthropicClient { ApiKey =
apiKey }` with no `Timeout` override. Per the Anthropic C# SDK's documented defaults, that's a
10-minute per-attempt timeout with up to 2 automatic retries on 408/409/429/5xx/connection
errors (not the instant-infinite-retry the audit asked to check for — the SDK's own retry
policy is already bounded and reasonable) — but a Claude outage could still hold
`POST /api/ai-orders/generate` or `POST /api/ai/assistant` open for up to ~30 minutes
(`timeout × (max_retries+1)`), tying up the request thread behind a "Claude аналізує…" spinner
with no way for the user to know it's stuck. Set an explicit `Timeout = TimeSpan.FromSeconds(60)`
on all three clients (worst case now ~3 min, still bounded, matches the existing
graceful-degradation error path already in place — see below).

## Reviewed, found correct — no changes

- **AI isolation (CLAUDE.md rule):** Claude API client, prompt templates, and response
  parsing live exclusively in `Infrastructure/AI/*`; `Application/Features/AiOrders` and
  `AiAssistant` only see `IAiOrderAdvisor`/`IBusinessAssistantAdvisor` (Domain interfaces) —
  zero references to the `Anthropic` SDK namespace outside `Infrastructure`.
- **Error/timeout degradation:** both `AiOrderService.GenerateAsync` and
  `AiAssistantService.AskAsync` already wrap the advisor call in
  `catch (Exception ex) when (ex is not OperationCanceledException)` and return a readable
  `(null, error)` tuple — a Claude failure returns 400 with a Ukrainian message (with a
  specific "поповніть баланс" message for credit-balance errors), never a 500, never a hang
  past the new 60s timeout. New tests lock this in for both services (advisor throws generic
  exception → graceful error; advisor throws credit-balance message → billing-specific
  message; not-configured → short-circuits before any DB/API work).
- **No duplicate/wasted Claude calls from the frontend:** `useGenerateAiOrder` and
  `useAiAssistant` (`frontend/features/ai-orders`, `ai-assistant`) are React Query
  **mutations** (only fire on explicit user action, never on refetch/refocus/stale-time); both
  "Generate"/"Send" buttons are `disabled={mutation.isPending}` — no double-click duplicate
  spend. Read-only list/detail queries (`useAiOrders`, `useAiOrder`) never call Claude.
- **API key handling:** already masked on GET (`GenericIntegrationSecrets`, last-4-chars,
  fixed in TASK-347) and never logged anywhere in the three advisors (no `ILogger` usage at
  all in that layer; no request-body logging middleware in the API).
- **RLS / cross-tenant isolation:** `BusinessAssistantAdvisor.AdviseAsync` runs inside the
  normal per-HTTP-request `AppDbContext` (connection-scoped `SET app.tenant_id` via
  `TenantConnectionInterceptor`, same as every other request) — no direct SQL, no superuser
  connection, no `Task.Run`-detached scope (the exact bug class TASK-356 found in POS). Every
  query additionally filters `TenantId == tenantId` explicitly (defense-in-depth beyond RLS).
  `AiOrdersController`/`AiAssistantController` both resolve `tenantId` from the JWT claim.
- **No N+1 in AI-prompt context assembly itself:** `AiOrderService.GenerateAsync`'s
  weather/events/promos/ADU/schedule calls are one query per data source (not per-item);
  `BusinessAssistantAdvisor.AdviseAsync`'s 4 context queries (critical stock, pending orders,
  7-day sales, suppliers) are each a single bounded (`Take(20)`/`Take(5)`/`Take(15)`) query.
- **Generation cadence:** once-per-store-per-night matches v2-spec §7 by design (a fresh daily
  suggestion snapshot for manager review), not a duplicate-call bug. Noted but not fixed as a
  separate low-severity ordering nit: `weather-fetch-cron` runs at 06:00, *after*
  `ai-order-cron`'s 05:00 — the morning AI-order run always reads the previous day's weather
  fetch (up to ~23h stale for a 7-day forecast). Low impact, not touched.

## Tests / build

12 new tests: `ShelfGuard.Tests/AiOrders/AiOrderServiceTests.cs` (6 — N+1 regression ×2,
not-configured, advisor-throws generic + credit-balance, nothing-to-order skips the API call)
and `ShelfGuard.Tests/AiAssistant/AiAssistantServiceTests.cs` (6 — empty/whitespace message,
not-configured, advisor-throws generic + credit-balance, success path). `dotnet build` 0
err/0 warn, `dotnet test` 842/842 green (was 830). Worker `tsc --noEmit` clean.

## Needs a decision

Nothing blocking. The `weather-fetch` vs `ai-order` cron ordering nit above is a candidate
for a follow-up if the user wants same-day-fresh weather in the morning AI order run
(swap to `weather-fetch-cron` firing before 05:00, e.g. `0 4 * * *`).
