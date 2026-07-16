# TASK-356 — Backend: Block 6 pre-launch audit — POS & Фіскалізація (Checkbox ПРРО)

**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-355

Block 6 of the pre-launch audit (`eager-pondering-tower.md`). Reviewed
`Features/Pos`, `Infrastructure/Integrations/Prro`, `worker/src/jobs/fiscalization-retry.job.ts`
against v3-spec.md §3 + `.claude/docs/integrations.md` (Checkbox ПРРО section). Highest
financial/legal risk area in the product. Found and fixed 3 real bugs (2 P0, 1 P0-adjacent),
confirmed several things already correct, flagged 1 out-of-scope bug + 1 product-decision gap.

## P0 — Online fiscalization silently never completed (detached Task.Run)

`PosService.CreateSaleAsync` ran the Checkbox `CreateReceiptAsync` call + status write-back
on a fire-and-forget `_ = Task.Run(...)` that captured this HTTP request's *scoped*
`IPosRepository` (backed by the request's `AppDbContext`) and, transitively via
`IFiscalServiceFactory`, a fresh `AppDbContext` whose RLS tenant context
(`TenantConnectionInterceptor`) is resolved from `IHttpContextAccessor.HttpContext`. Because
the task was never awaited, the HTTP response completed (and the request's DI scope was
disposed) before the detached task's network round-trip to Checkbox finished — every
run risked `ObjectDisposedException` on `_pos.SaveChangesAsync()`, silently swallowed by the
existing catch block, and separately risked reading a stale/already-recycled pooled
`HttpContext` for RLS purposes (Kestrel reuses `HttpContext` instances across requests).
Net effect in practice: `tx.Status`/`FiscalNumber` were (at best, unreliably) never updated
inline — every sale depended entirely on the 5-minute `fiscalization-retry.job.ts` cron to
actually fiscalize, contradicting the "instant fiscal receipt on the printed check"
requirement (v3-spec §3) and ADR-011/013's documented online flow. No double-fiscalization
risk materialized from this specific bug (Checkbox's receipt `id` idempotency, confirmed
live in TASK-067, protects the eventual retry), but it explains why fiscalization always
looked "async/delayed" instead of "instant when Checkbox is healthy."

**Fix:** removed the detached `Task.Run`; the fiscalization attempt now runs inline,
awaited, bounded by an 8s linked `CancellationTokenSource` so a slow/unreachable Checkbox
still can't meaningfully stall checkout (ADR-011's "sale never blocks on fiscal" still
holds — the DB commit already happened before this step). A healthy Checkbox response is
now reflected directly in the `SaleDto` (`fiscalStatus: "fiscalized"`, real `fiscalNumber`)
instead of always showing pending. On timeout/failure it stays `pending_fiscalization`
exactly as before, retried by the same 5-minute worker job — unchanged and still correct.

## P0 — `ProductStock.Quantity` had no concurrency protection (oversell race)

No optimistic-concurrency token existed anywhere on `product_stock`. Two concurrent writers
decrementing the same batch's `Quantity` (e.g. two cashiers selling the last unit at the
same moment) both succeed with a last-write-wins `UPDATE` — a silent lost update that can
oversell stock while `stock_events` still logs two `-1` deltas against a single real
decrement. Reproduced deterministically against the real dev Postgres (see test below).

**Fix:** `AppDbContext` — `ProductStock` entity now maps Postgres's built-in `xmin` system
column as an EF Core concurrency token (`e.Property<uint>("xmin").IsRowVersion()`; the
non-deprecated replacement for `UseXminAsConcurrencyToken()`, which is obsolete in this
Npgsql EF Core provider version). Migration `20260715054917_AddProductStockXminConcurrencyToken`
is a genuine no-op (`xmin` already exists on every Postgres row — EF's scaffolded
`AddColumn` would have failed with "column name is reserved", rewritten to an empty
Up/Down with an explanatory comment) — applied to dev DB, confirmed in
`__EFMigrationsHistory`. `PosRepository.SaveChangesAsync` now catches
`DbUpdateConcurrencyException` and rethrows as a new Domain-level
`ConcurrencyConflictException` (`ShelfGuard.Domain/Exceptions/ConcurrencyConflictException.cs`)
so `PosService` — which must not reference EF Core per CLAUDE.md's layer rules — can catch
it without an EF Core dependency. `PosService.CreateSaleAsync` now wraps the main commit in
a try/catch and returns a clean `409 "Stock was updated concurrently by another sale. Please
retry."` instead of letting the conflict propagate as an unhandled 500 or (before the token
existed) silently corrupting `Quantity`. This concurrency check applies to every
`ProductStock` write, not just POS — Transfers/WriteOffs/Receipts get the same protection
for free, though those services don't yet have their own friendly catch/409 (an unhandled
`DbUpdateConcurrencyException` there now surfaces as a 500 instead of silent corruption —
strictly safer than before, but wiring a clean error message into those services is a
follow-up, not done here — out of scope for the POS block).

## P0-adjacent — `ItemRepository.GetByBarcodeAsync` throws against real Postgres

Discovered while building the concurrency regression test (the test needed a real barcode
lookup and immediately hit this). `_db.Items.FirstOrDefaultAsync(p => p.Barcodes.Contains(barcode))`
— `Item.Barcodes` is `List<string>` mapped `jsonb` — does not translate correctly; Npgsql's
default `Contains` translation assumes native Postgres array semantics and generates SQL that
tries to cast a `text[]` value to `jsonb`, throwing `PostgresException 42846: cannot cast
type text[] to jsonb` on every call. This method is the *only* way `PosService.CreateSaleAsync`
resolves a scanned barcode to a product, and is also used directly by
`GET /api/items/barcode/{code}` (`ItemsController`) — both call sites were broken against a
real database. Every existing test for this path used an in-memory fake, so this had never
been caught; same root cause as the earlier BUG-008 (`AnalyticsRepository`, different LINQ
shape — `.Count`/indexer instead of `.Contains`). This is as severe as it sounds: **core
POS barcode scanning could not have worked against production Postgres** — worth stating
plainly since it's the actual "scan and sell" entry point for every POS sale.

**Fix:** rewritten to use `EF.Functions.JsonContains(p.Barcodes, JsonSerializer.Serialize(new[] { barcode }))`,
which Npgsql translates to the jsonb containment operator `"Barcodes" @> @value` — verified
correct against real Postgres (generated SQL confirmed via `LogTo` during debugging).

**Related, NOT fixed (out of scope, flagged as a background task):** the same jsonb-array
translation problem exists in `DailySalesRepository.GetProductIdsByBarcodesAsync`
(`.Barcodes.Any(b => barcodes.Contains(b))` → `PostgresException 42883: operator does not
exist: jsonb && text[]`, confirmed live, same reproduction method) — used by the bulk
daily-sales/ADU import flow, unrelated to POS. Spawned as a separate task
(`task_1f85ac74`), not fixed here.

## Verified correct, no changes needed

- **Money/rounding:** `decimal` used throughout `Pos`/`Prro` — no `double`/`float`
  anywhere in the sale, VAT, or Checkbox kopecks/thousandths conversion paths.
  `CheckboxFiscalClient.ToKopecks`/`ToQuantity` round `MidpointRounding.AwayFromZero`.
- **`IFiscalServiceFactory`** — correctly per-tenant (`ADR-013`), resolved at call time via
  a fresh `IServiceScopeFactory`-created scope for the `integration_configs` lookup, never
  injected statically at startup. `FiscalServiceFactory` itself is the reference pattern
  the online-fiscalization fix above now also (indirectly) benefits from, since the fix
  keeps everything inside one valid request scope instead of trying to replicate that
  pattern awkwardly for `_pos`.
- **Offline-retry queue** (`worker/src/jobs/fiscalization-retry.job.ts`, every 5 min):
  fetches `pending_fiscalization` transactions older than 30s with `RetryCount < 5` via
  `GET /api/pos/sales/pending-fiscalization`, retries each via
  `POST /api/pos/sales/{id}/fiscalize` sequentially. That endpoint (`PosService.
  FiscalizeTransactionAsync`) runs inside a real, properly-authenticated HTTP request
  (service account JWT) — correctly scoped, no changes needed. Double-fiscalization is
  prevented at the provider level: Checkbox honors `LocalReceiptId` (our `pos_transaction`
  id) as an idempotency key for `receipts/sell` (confirmed live in TASK-067's E2E test,
  `Assert.Equal(localId, receipt.ProviderReceiptId)`), so repeated retries of the same
  transaction can't create two fiscal receipts even before this task's fixes.
- **FEFO in POS sales:** `CreateSaleAsync` consumes batches via the same
  `IStockRepository.GetFefoOrderedAsync` ordering (`ORDER BY ExpiryDate`) used by the rest
  of the app (Block 3, TASK-353) — nearest-expiry-first confirmed by existing
  `CreateSale_applies_fefo_order`/`CreateSale_fefo_spans_multiple_batches` tests.
  `expiry_date`/`batch_number` untouched by the sale (only `Quantity`/`Status`), consistent
  with the "FEFO is sacred" rule.
- **DB indexes:** `pos_transactions`/`pos_transaction_items`/`pos_shifts` are already
  well-indexed — `(TenantId, StoreId, CreatedAt)`, a partial index on
  `Status = 'pending_fiscalization'`, a unique `(TenantId, ReceiptNumber)`, a filtered
  "exclude fiscalization_failed" reporting index, a covering index on
  `pos_transaction_items(TransactionId)` including `ProductId/PriceFinal/Quantity`, and a
  unique partial index enforcing one open shift per store. No gaps found (this module was
  already the best-indexed area in the codebase before this audit).
- **N+1:** shift/day report paths (`GetTransactionsByShiftAsync`,
  `GetPendingFiscalizationAsync`, and the `PosAnalyticsRepository` summary/top-products/
  revenue-trend/cashier-stats queries) all eager-`.Include()` or use single projected
  queries — none loop-and-query per row.

## Load/concurrency test (real Postgres, not fakes)

New `ShelfGuard.Tests/Pos/PosConcurrencySalesIntegrationTests.cs` — real-DB test (same
soft-skip pattern as Block 2's `RlsCrossTenantIntegrationTests`, dev Postgres on :5435).
Two independent `PosService` instances (each with its own `AppDbContext`, mirroring two
separate HTTP request scopes) both sell the last unit of the same batch via `Task.WhenAll`.
The race is made *deterministic* (not timing-luck) via a two-way rendezvous
(`RendezvousStockRepository` wrapping `IStockRepository`): both cashiers are guaranteed to
read the same pre-decrement `Quantity` before either is allowed to proceed to its write.
Asserts exactly one sale succeeds, the other gets a clean `409`, and the final DB quantity
is exactly `0` — never both succeeding (oversell) and never a lost update. This test is
what caught the `GetByBarcodeAsync` bug above (it failed on the very first run for an
unrelated reason before the concurrency logic was even exercised).

Also added to `PosServiceTests.cs` (fake-based, deterministic, no DB needed):
- `CreateSale_successful_online_fiscalization_is_reflected_in_response` — pins the new
  inline/awaited fiscalization behavior (previously untestable by construction, since the
  old detached task could never be observed to complete within a synchronous unit test).
- `CreateSale_concurrency_conflict_on_commit_returns_409` — pins the service-layer
  `ConcurrencyConflictException → 409` translation in isolation from the real-DB test.

## Flagged, not fixed — needs a product decision

**Shift open is scoped per TENANT, not per store.** `PosRepository.GetOpenShiftAsync`
filters only on `TenantId` (no `StoreId`), so `PosService.OpenShiftAsync`'s "already open"
409 check blocks opening a shift at Store B while Store A (same tenant) still has one open
— even though `PosShift` has a DB-level unique index that *is* per-store
(`HasIndex(s => s.StoreId).IsUnique().HasFilter("ClosedAt IS NULL")`), suggesting per-store
shifts were the original intent. This wasn't changed: `IFiscalServiceFactory.
GetForTenantAsync` resolves the Checkbox license **per tenant**, not per store/register, so
Checkbox itself only supports one physical register (one open fiscal shift) per tenant
today — "fixing" the business-rule check to be per-store without also making fiscal
resolution per-store/register would just move the failure from a clean upfront 409 to a
confusing per-sale `open_failed`/permanently-pending-fiscalization state at the second
store. This is a real limitation for multi-store chains wanting POS running at more than
one location simultaneously (a segment the product explicitly targets per CLAUDE.md) — it
needs a scope decision (single-register-per-tenant is fine for now vs. invest in
per-store/register Checkbox resolution), not a unilateral code fix.

**Shift close has no cash-count reconciliation.** `PosShift.ClosingCash` exists in the
schema (migration `V3PosFoundation`) but is never read or written anywhere — no backend
endpoint parameter, no frontend UI field (`frontend/features/pos` has zero references).
`CloseShiftAsync` only produces the fiscal Z-report; v3-spec §3's "Закриття зміни:
Z-звіт → Інкасація → Звіт касира за зміну" describes cash reconciliation as part of the
close flow, but that half was never built. Flagging as a scope gap for a product decision
(needed for MVP launch vs. acceptable to defer), not something to add unilaterally here —
it needs UI/UX design (who counts cash, what happens on a mismatch, does it block the next
open) beyond what a backend-only audit pass should decide.

## Verification

- `dotnet build` — 0 errors, 0 warnings (repo-wide).
- `dotnet test` — 824/824 green (was 821; +3: the new concurrency integration test, the
  successful-fiscalization test, the concurrency-conflict-409 test).
- Migration `20260715054917_AddProductStockXminConcurrencyToken` applied to dev DB,
  confirmed in `__EFMigrationsHistory`.
- Concurrency fix verified end-to-end against real dev Postgres (not just unit fakes).

## Files changed

- `backend/ShelfGuard.Application/Features/Pos/PosService.cs` — inline bounded
  fiscalization (replaces detached `Task.Run`); concurrency-conflict catch on the main
  commit → 409.
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs` — `ProductStock` xmin
  concurrency token.
- `backend/ShelfGuard.Infrastructure/Data/Repositories/PosRepository.cs` —
  `DbUpdateConcurrencyException` → `ConcurrencyConflictException` translation.
- `backend/ShelfGuard.Infrastructure/Data/Repositories/ItemRepository.cs` —
  `GetByBarcodeAsync` fixed to use `EF.Functions.JsonContains`.
- `backend/ShelfGuard.Domain/Exceptions/ConcurrencyConflictException.cs` — new.
- `backend/ShelfGuard.Infrastructure/Migrations/20260715054917_AddProductStockXminConcurrencyToken.*` — new (no-op SQL, model-snapshot only).
- `backend/ShelfGuard.Tests/Pos/PosConcurrencySalesIntegrationTests.cs` — new.
- `backend/ShelfGuard.Tests/Pos/PosServiceTests.cs` — 2 new tests + `FakePosRepo`/fiscal
  fake additions.

## Next

- Follow-up task spawned for `DailySalesRepository.GetProductIdsByBarcodesAsync`
  (`task_1f85ac74`).
- Production not touched — all changes on dev only, per the overall audit plan (deploy
  deferred to a separate user decision).

---

# Addendum (2026-07-15, same day) — user-confirmed follow-ups on the two flagged gaps

User reviewed both items flagged above and gave two different directives: **plan only**
(no code) for per-store shifts; **implement now** for cash reconciliation.

## Per-store shift migration — plan (research only, NOT implemented)

### Where the "one open shift per tenant" restriction actually lives

Traced every call site. It is enforced in exactly two places, both service-layer, neither
DB-enforced at the tenant-wide grain:

1. **`IPosRepository.GetOpenShiftAsync(Guid tenantId, ct)`** (`PosRepository.cs`) —
   `_db.PosShifts.Where(s => s.TenantId == tenantId && s.ClosedAt == null).FirstOrDefaultAsync()`.
   No `StoreId` filter at all. Three call sites in `PosService.cs`:
   `OpenShiftAsync` (line 56, the 409 gate), `GetCurrentShiftAsync` (line 122),
   `CreateSaleAsync`'s shift-closed check indirectly via `GetShiftByIdAsync` (line 131,
   not affected — that one looks up by shift id, already unambiguous).
2. **`IFiscalServiceFactory.GetForTenantAsync(Guid tenantId, ct)`** (`FiscalServiceFactory.cs`)
   — resolves the tenant's Checkbox registration from `integration_configs` keyed by
   `UNIQUE (TenantId, Service)` (`AppDbContext.cs` line ~700) — **one row per tenant, full
   stop, no `StoreId` column exists in the schema at all**. Four call sites in
   `PosService.cs`: `OpenShiftAsync` (78), `CloseShiftAsync` (146), `CreateSaleAsync` (395),
   `FiscalizeTransactionAsync`/retry path (488).

**The DB itself does NOT enforce tenant-wide exclusivity** — `PosShift` already has a
**per-store** unique partial index (`AppDbContext.cs`: `e.HasIndex(s => s.StoreId).IsUnique().HasFilter("\"ClosedAt\" IS NULL")`),
which only blocks two open shifts at the *same* store. This strongly suggests per-store
shifts were the original intent and the service-layer check (tenant-wide) is what
regressed/simplified away from that — not a deliberate design choice recorded anywhere
(`decisions.md` has no ADR for this).

### Is the Checkbox license really one-per-company? (the thing worth checking, per the ask)

**No — checked, and it's not.** `.claude/docs/integrations.md`'s Checkbox section (verified
live 2026-06-12, `CheckboxFiscalClient.cs` header comment repeats it): *"Auth: X-License-Key
header identifies **the cash register**"* — singular register, not company/tenant. Checkbox's
own auth model is register-scoped: each physical/virtual касовий апарат gets its own
license key + fiscal registration with ДПС (this matches general Ukrainian ПРРО practice —
a business with 5 tills provisions 5 registrations, not 1 shared one). Nothing in the wire
protocol (`cashier/signin`, `shifts`, `receipts/sell` — all scoped by the bearer token +
`X-License-Key` header on the request) implies or enforces "only one register per company."
**The "one open shift per tenant" limitation in ShelfGuard today is a self-imposed
simplification from `integration_configs` having no `StoreId` column — not a Checkbox
platform restriction.** One more piece of supporting evidence: `CheckboxTokenStoreRegistry.GetOrAdd(tenantId, baseUrl, licenseKey)`
already keys its cached bearer-token cache by `licenseKey` (not just `tenantId`) — this
piece of the stack is *already* register-scoped and needs zero changes for multi-register
support.

**Verdict for the ask ("оцінка ризику/обсягу роботи" + "якщо тривіальний фікс — скажи"):
this is NOT a trivial fix.** It's a real, multi-layer, coordinated change — see below — but
every individual piece uses patterns already proven elsewhere in this codebase (partial
unique indexes for "one X per Y where condition", register-scoped token caching already
built). Low architectural risk, moderate size, roughly 2 files → cascades through
~6 backend files + 2 frontend surfaces + a migration + ~5 test files.

### What would actually need to change

**Database (1 migration, additive/low-risk):**
- `integration_configs`: add nullable `StoreId uuid NULL REFERENCES locations(id)`.
  `NULL` = tenant-wide fallback config (what every existing row becomes automatically —
  zero data migration needed, fully backward compatible for tenants who never configure a
  per-store override).
- Replace the single `UNIQUE (TenantId, Service)` index with two partial unique indexes:
  `UNIQUE (TenantId, StoreId, Service) WHERE StoreId IS NOT NULL` (one config per store)
  and `UNIQUE (TenantId, Service) WHERE StoreId IS NULL` (at most one tenant-wide
  fallback) — same idiom as the existing `UX_supplier_profiles_owner_tenant` partial
  index and the cooperation-agreements "one live agreement" partial index.
- No RLS changes needed — `integration_configs`' `tenant_isolation` policy is tenant-scoped
  only; store-level segregation is already handled at the application layer everywhere
  else in this codebase (e.g. `product_stock.StoreId` filtering), not via RLS.

**Backend (~half to 1 day):**
- `IIntegrationRepository`: new method (or overload) that looks up the store-specific row
  first, falls back to the `StoreId IS NULL` tenant-wide row. Keep the existing
  tenant-only method for non-POS integrations (Вчасно, Telegram) that have no store
  dimension — don't force every `IIntegrationRepository` consumer through the new shape.
- `IFiscalServiceFactory`: `GetForTenantAsync(Guid tenantId, ct)` → `GetForStoreAsync(Guid tenantId, Guid storeId, ct)`
  (or an overload). Update all 4 `PosService.cs` call sites — 3 already have a `storeId`
  in scope trivially (`OpenShiftAsync` has `request.StoreId`, `CloseShiftAsync`/
  `CreateSaleAsync`'s fiscal calls already resolve `shift.StoreId` earlier in the same
  method), the 4th (`FiscalizeTransactionAsync`, retry path) needs `tx.StoreId` — already
  a column on `PosTransaction`, just not currently read into that call.
- `IPosRepository.GetOpenShiftAsync(Guid tenantId, ct)` → add `Guid storeId` param. Two
  call sites in `PosService.cs` (`OpenShiftAsync`'s 409 gate, `GetCurrentShiftAsync`).
  `GetCurrentShiftAsync` and the `IPosService` interface both need a new `storeId`
  parameter threaded from `PosController`.
- `PosController`: `GetCurrentShift` (currently no params beyond auth) and `GetSales`
  need a `?storeId=` query param (or route segment) — ambiguous otherwise once more than
  one shift can be open at once. `OpenShift` needs no route change (`OpenShiftRequest`
  already carries `StoreId`).
- `PrroSettingsController`: `GET/PUT /api/settings/prro` + `POST .../test` are entirely
  tenant-scoped today (`ResolveTenantId()` only). Needs a store dimension in the route/
  query (e.g. `/api/settings/prro?storeId=` with omission meaning "manage the tenant-wide
  fallback") — this is the biggest single-file change since it's also new product surface
  (a settings screen for "which store uses which register"), not just a signature tweak.
- Test fallout: `PosServiceTests.cs`, `FiscalServiceFactoryTests.cs`,
  `PrroSettingsServiceTests.cs`, `FiscalizationRetryTests.cs` all construct fakes/mocks
  against the current signatures — every one needs updating for the new params, on top of
  new tests for the store-fallback resolution logic itself (store-specific config present
  → used; absent → tenant-wide fallback used; neither → env/Noop fallback, unchanged).

**Frontend (~1 day, separate agent):**
- Settings → Integrations → ПРРО: needs a store selector + genuinely new UI for "one
  config per store, or a shared default" — today it's a single flat form assuming one
  register for the whole company.
- POS "current shift" fetch needs to become store-scoped (the app already has a
  tenant-wide `StoreSelector`/`useStoreContext` used by Dashboard/`/stock` since TASK-281
  — this is the natural place to source the `storeId` from, so the wiring pattern already
  exists, just not applied to POS yet).

**Business/cost dimension worth surfacing to the user, not just engineering:** each
additional concurrently-open register is a **separate real-world Checkbox registration**
(their own pricing/admin overhead, separate ДПС fiscal registration per register) — so
this isn't purely a code decision, it also commits the business to provisioning +
paying for N Checkbox registrations for an N-store rollout, once a tenant actually wants
simultaneous multi-store POS.

**Not implemented.** No code changed for this — plan only, per the instruction. Tracked as
`known-issues.md` KI-015 and referenced from `api-contracts.md`'s new POS section.

## Cash reconciliation (ClosingCash) — implemented

Backend-only, per the second directive. `PosShift.ClosingCash` (already in the schema
since `V3PosFoundation`, previously never read/written by any code path) is now wired
end-to-end.

**`POST /api/pos/shifts/close`** — body is now optional `CloseShiftRequest { decimal? ActualClosingCash }`
(`Features/Pos/Dtos/PosDtos.cs`). Omitting it (or sending `actualClosingCash: null`) closes
exactly as before — fully backward compatible with the mobile app and any other caller
that currently sends no body. When provided:
- Validated `>= 0` before touching the fiscal provider at all — a bad cash count returns
  `400 { "error": "ActualClosingCash cannot be negative." }` and nothing is persisted (shift
  stays open).
- `ExpectedCashAmount` = `shift.OpeningCash` (0 if null) + this shift's **cash-only** sales
  total (`PaymentType == "cash"`) — new `IPosRepository.GetCashSalesTotalForShiftAsync`
  (`SUM("TotalAmount") WHERE ShiftId=... AND PaymentType='cash'`, single query, no N+1).
  Card sales are deliberately excluded — they never touch the physical drawer, so
  including them would flag every card-heavy shift as a false "shortage".
- `CashDiscrepancy` = `ActualClosingCash - ExpectedCashAmount`. Positive = surplus,
  negative = shortage, `0` = exact match, `null` when not reconciled.
- Reconciliation is computed **independently of the fiscal Z-report outcome** — a Checkbox
  failure (`close_failed`) doesn't block or skip the cash count, and vice versa.
- "Can't close an already-closed shift": unchanged existing behavior —
  `GetOpenShiftAsync` only ever returns shifts with `ClosedAt IS NULL`, so a second close
  attempt naturally 404s (`"No open shift found."`) before reconciliation logic runs at
  all; pinned with a new explicit test rather than relying on that being incidental.

`ShiftDto` extended with `OpeningCash`, `ClosingCash`, `ExpectedCashAmount`,
`CashDiscrepancy` (all `decimal?`, appended as trailing optional params — non-breaking for
any other construction site). `OpeningCash`/`ClosingCash` now also come back on
`shifts/open` and `shifts/current`, not just `shifts/close` (reads straight off the
entity, no extra query).

**Tests added** (`PosServiceTests.cs`, fake-based, no DB needed): exact match (discrepancy
0, card sales correctly excluded from the expected-cash calc), shortage (negative
discrepancy), surplus (positive discrepancy), negative-amount validation (400, shift stays
open — asserted via `existing.ClosedAt` still null), omitted-body backward compat (all
four reconciliation fields null), double-close (second attempt on an already-closed shift
→ 404). `dotnet build` 0 err/0 warn, `dotnet test` 830/830 green (was 824 after the main
audit; +6 for this addendum).

**Docs updated:**
- `.claude/docs/api-contracts.md` — new "POS" section (previously undocumented
  entirely): all `/api/pos/*` routes + full request/response contract for the reconciled
  close-shift endpoint, with a JSON example.
- `.claude/docs/known-issues.md` — new KI-015 for the per-store shift gap (cross-referenced
  from api-contracts.md).

### Exact signature for the frontend hand-off

```
POST /api/pos/shifts/close
Auth: [Authorize(Policy = CanAccessPos)]  (unchanged)
Body (optional): { "actualClosingCash"?: number }   // omit/null = no reconciliation, unchanged behavior
Response 200 (ShiftDto):
{
  "shiftId": "uuid", "storeId": "uuid",
  "status": "Closed",
  "openedAt": "ISO8601", "closedAt": "ISO8601",
  "providerShiftId": "string|null", "fiscalStatus": "closed|close_failed|local_only",
  "totalSales": number, "shiftNumber": number|null,
  "openingCash": number|null,
  "closingCash": number|null,        // = actualClosingCash if provided, else null
  "expectedCashAmount": number|null, // OpeningCash + cash-only sales; null unless reconciled
  "cashDiscrepancy": number|null     // closingCash - expectedCashAmount; +surplus / -shortage; null unless reconciled
}
Response 400: { "error": "ActualClosingCash cannot be negative." }
Response 404: { "error": "No open shift found." }
```
No route change, no auth change — purely an optional body + 4 new response fields.
Frontend needs: a cash-count input on the close-shift screen/dialog that posts
`actualClosingCash`, and a way to surface `cashDiscrepancy` (e.g. a warning banner when
non-zero, distinguishing surplus vs. shortage) — `frontend/features/pos` currently has no
UI for this step at all (close shift is presumably a single button today).
