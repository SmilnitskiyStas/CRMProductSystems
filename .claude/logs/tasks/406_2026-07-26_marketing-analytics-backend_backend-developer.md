# TASK-406: Marketing analytics (RFM) backend — Фаза 1

**Agent:** backend-developer
**Date:** 2026-07-26
**Status:** done

## Контекст

Task #2 of Фаза 1 in `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md`, following TASK-405
(Фаза 0 — `PosTransaction.CustomerId` now really gets written on sales with a linked customer).
New `Features/MarketingAnalytics/` module, mirroring `Features/Analytics/`'s thin
service→repository shape. `marketing_analytics` module key was already registered in
`Tenant.UpdateModules` by TASK-405 — reused as-is.

## Зроблено

**Domain**: `RfmSegmentKey` enum (11 members, numeric order = classification priority,
`[JsonConverter(typeof(JsonStringEnumConverter))]` scoped to just this type so it serializes as
`"Champions"` etc., not an ordinal — the only enum in the whole app used as a wire value, every
other status/type field here is a plain string). `IMarketingAdvisor` (Domain/Interfaces, mirrors
`IAiOrderAdvisor`'s shape). `TenantRoleCapabilities.MarketingAnalyticsExportPii` (ADR-020, new
"Маркетинг" group) — store_manager+ is the default floor via a new
`MarketingAnalyticsAuthorization.CanExportPii` static class (imperative check, same shape as
`LegalEntityAuthorization` — the decision depends on a request field, not the whole action).

**`RfmSegmentClassifier`** (`Application/Features/MarketingAnalytics/`) — pure static function,
11 named-constant if-branches in the plan's exact table order (first match wins). Caught and
fixed one real bug while writing tests: "Lost" (`>6 months`) must use strict `<`, not `<=` —
exactly 6 months must NOT count as Lost yet. Also discovered and documented (not a bug — the
plan's literal priority order) that Hibernating (#9) always wins over Lost (#10) whenever both
conditions match, and AboutToSleep (#8) always wins over Hibernating (#9) at `r=2` (Hibernating
is only reachable at `r=1`) — both are consequences of evaluating the plan's table top-to-bottom,
documented in the classifier's own comments. 39 unit tests, one per rule + every documented
priority interaction + input validation (score out of [1,5], lifetimeReceiptCount<1 throws).

**`RecommendationTemplates`** — one method per segment, Ukrainian Тригер/Дія/Оффер/Застереження/
Товари copy substituting live KPIs (`RfmRecommendationInputDto`). Champions/CannotLoseThem text
adapted from RFM_ANALYSIS.md §14.1/14.2 almost verbatim; the other 9 are original copy in the
same structure. 18 tests (non-empty per segment, distinct trigger text across all 11, Champions
never offers a discount, retention segments (CannotLoseThem/AtRisk/Lost) mention win-back, KPIs
actually substituted not static).

**`MarketingAnalyticsRepository`** — first raw-SQL repository in the codebase.
`Database.SqlQueryRaw<T>` (NOT `ExecuteSqlInterpolatedAsync`, since results are read, not
mutated) with `{n}` positional placeholders — EF Core rewrites these into real Npgsql
parameters, never string-interpolated values; verified this is genuinely parameterized (not a
keyless-entity-registration requirement, not scalar-only) via two throwaway spikes against the
live dev Postgres before writing the real file, both since deleted. `NTILE(5)` scoring query:
`recency_score = 6 - NTILE(5) OVER (ORDER BY days_since_last_purchase ASC)` computed as a plain
`date - date` integer subtraction (NEVER `EXTRACT(DAY FROM interval)`, which only returns an
interval's local "days" field, not total elapsed days across months — a real bug I caught before
it shipped). Store filter: caller always normalizes to a `Guid[]` (empty = no restriction), guard
is `cardinality({n}::uuid[]) = 0 OR col = ANY({n}::uuid[])` everywhere. Day/hour behavior
distribution converts via `CreatedAt AT TIME ZONE 'Europe/Kyiv'` (brief's requirement) —
live-verified across a real day boundary (22:00 UTC → 01:00 Kyiv next day) in the integration
test below. Top-products/affinity/basket all group by `Item.Name` (not Id) and exclude
`ItemType = 'packaging'`. Affinity candidate pool capped at 200 before ranking lift; basket pool
capped at 200 before ranking co-occurrence. `GetLtvAsync` takes no date-range parameter at all
(enforces "always all-time" at the signature level, not just by convention). 8 live-Postgres
integration tests (`MarketingAnalyticsRepositoryIntegrationTests.cs`) seeding real
tenant/customers/items/transactions — caught 2 real off-by-one errors in my own test
expectations (not SUT bugs) while writing them, both fixed.

**`MarketingAnalyticsService`** — classifies the full scored population once per call
(`ClassifyAllAsync`), groups by segment, computes all share%/revenue aggregates with
divide-by-zero guards (0, never NaN/throw). Overview always returns exactly 11 segment cards
(zero-count ones included) plus the separate "no purchase" card
(`registeredCount - everPurchasedCount`, floor 0). "Registered" customers are tenant-wide
(`Customer` has no store association at all); "ever purchased"/"no purchase" DO respect the
store filter — a store-scoped dashboard reasonably shows "no purchase AT THESE STORES" even for
a customer with history elsewhere in the tenant. Explicit design decision, flagged for
security/product review if it surprises anyone downstream. `GetSegmentDetailAsync`/
`ExplainSegmentAsync` share one internal `BuildSegmentDetailAsync` so the two never disagree
about "this segment's numbers" for the same filter+key; an empty segment (0 customers) short-
circuits before calling the repository for top-products/behavior/LTV and returns a fully zeroed,
non-null DTO (QA checklist: empty segment must never break the page). Exports mask phone by
default (`+380 XX *** ** 67` for a 12-digit `380`-prefixed number, generic last-4-visible
fallback otherwise — `Customer.Phone` isn't guaranteed `PhoneNormalizer`-shaped like
`ConsumerAccount.Phone` is); `UnmaskPii` only takes effect when the controller has already
confirmed `MarketingAnalyticsAuthorization.CanExportPii` — a caller who lacks it just silently
gets the masked file, never a 403. Every export writes one `ActivityLog` row (Action +
`Meta = "segment=...; from=...; to=...; stores=...; rows=...; truncated=...; piiMasked=..."`,
plain string like `LoyaltyService`'s Meta convention, not JSON). 6 service-layer unit tests
(NSubstitute) — caught a real test-authoring gap (not a service bug): NSubstitute defaults
unconfigured `Task<IReadOnlyList<T>>>` mocks to `null`, and the service correctly assumes the
repository interface's non-nullable contract, so `ExplainSegmentAsync`'s test needed explicit
empty-but-non-null stubs.

**`ExcelExportService`** (Infrastructure/Export) — ClosedXML 0.105.1 (MIT), added via
`dotnet add package` (not EPPlus — commercial license v5+). Owns the 50k-row truncation +
visible banner row itself (`ExcelExportRequest.MaxRows`, default 50 000) — callers just pass
everything they have, never re-implement the cap. `IExcelExportService` is deliberately generic
(no MarketingAnalytics-specific shape) for future reuse.

**`MarketingAnalyticsController`** — `[Authorize(Policy = AppPolicies.CanViewAnalytics)]` (same
store_manager+ floor as the existing `AnalyticsController` — this is a sibling feature, not a
stricter one; flag for product/security review if a different floor was actually intended) +
`[RequireModule("marketing_analytics")]`. Period resolution: explicit `from`+`to` always wins;
else `period` = `3m`/`6m`/`12m`/`all`/anything-else-including-omitted defaults to 6 months
(matches the competitor's own default window). `storeIds` is a repeated query param
(`?storeIds=a&storeIds=b`), empty/omitted = all stores. `/explain` is POST (triggers a real
Claude call) but still reads its filter from the query string, same shape as the GETs — no body
DTO needed for it. The 3 export endpoints are POST with a JSON body (need `unmaskPii` +
product-name fields the GETs don't have); controller resolves `effective.UnmaskPii = request
.UnmaskPii && MarketingAnalyticsAuthorization.CanExportPii(User)` before calling the service.

**ItemType "packaging"** — added to `ItemService.IsValidItemType` (the one place item types are
validated) and its error message. No schema change (string column, unchanged).

**DI** — `IMarketingAnalyticsService` in `ShelfGuard.Application/DependencyInjection.cs`;
`IMarketingAnalyticsRepository`/`IMarketingAdvisor`/`IExcelExportService` (Singleton, stateless)
in `ShelfGuard.Infrastructure/DependencyInjection.cs`.

## Test-infra fix outside the feature's own files (flagged explicitly)

Adding `MarketingAnalyticsRepositoryIntegrationTests.cs` (a 3rd raw-Postgres integration-test
class, alongside the pre-existing `LoyaltyRepositoryIntegrationTests`/
`PosConcurrencySalesIntegrationTests`) pushed the FULL suite's cumulative count of distinct
`NpgsqlDataSource`-backed `DbContextOptions` instances past EF Core's internal
`ManyServiceProvidersCreatedWarning`-as-error threshold (~20, process-wide, not per-class) —
confirmed by first reducing my own class's contribution from ~24 calls down to a single shared
static field (fixed 4 of 5 initial failures), then finding the 1 remaining failure was
*`LoyaltyRepositoryIntegrationTests`*'s own `NewContext()` tripping the same shared counter, not
mine. This is a pre-existing, borderline-fragile test-infrastructure ceiling (any 3rd real-DB
integration-test class added by anyone would likely hit it), not a bug in any feature's logic.
Added one line — `.ConfigureWarnings(w => w.Log(CoreEventId.ManyServiceProvidersCreatedWarning))`
— to `LoyaltyRepositoryIntegrationTests.NewContext()` (test-infra only, zero behavior change to
what that test verifies, well-commented in place). This is the one edit outside my own new files;
`Features/Loyalty/`, `Features/ConsumerAuth/`, and `PosService.cs` themselves are untouched, per
the brief's constraint. Flagging explicitly in case a reviewer wants a different fix (e.g.
extracting a shared static `NpgsqlDataSource` test fixture across all 3 files — not done here,
out of scope for a single-file-scoped task).

## Верифікація

- `dotnet build` — 0 err, 0 warn (1 pre-existing unrelated warning in `MarketplaceServiceTests.cs`).
- `dotnet test` — **1083/1083 green** (was 1004 before this task; +79: 39 classifier + 18
  recommendation-template + 8 authorization + 6 service + 8 live-Postgres repository integration
  tests). Ran the full suite twice in a row to confirm no flakiness.
- Live Postgres integration tests seeded and cleaned up their own tenant-scoped rows via
  `DisposeAsync`; manually verified zero `MarketingAnalyticsRepositoryIntegrationTests` leftovers
  in the dev DB after the run (one orphaned tenant from an earlier, mid-fix failed run was found
  and cleaned up by hand). Noted in passing, not touched: 3 pre-existing orphaned
  `Loyalty Repo Test*` tenants from `LoyaltyRepositoryIntegrationTests` predate this session —
  out of scope, flagged only for whoever eventually does a dev-DB reset.
- Did not start the API/run a live end-to-end browser check — this was a backend-only task with
  no frontend/mobile UI to click through yet (TASK-409, next in the plan's agent sequence, per
  the brief's explicit "не чіпай frontend/mobile UI").

## Свідомі рішення (без user sign-off, за судженням, задокументовані в коді)

- Base view-access floor: `AppPolicies.CanViewAnalytics` (store_manager+), matching
  `AnalyticsController`'s existing floor — the brief didn't specify one explicitly.
- "No purchase"/"ever purchased" respect the store filter; "registered" does not (Customer has
  no store association at all).
- Product-pair-buyers export is customer-level (not receipt-level) and serves both the affinity
  and basket tabs' "Покупці обох" button identically.
- Skipped embedding each buyer's personal top-3 products in segment/product-buyer exports
  (RFM_ANALYSIS.md §11.3/15.2 calls this out as something the file "may" contain, not a hard
  requirement) — flagged as a small, bounded follow-up enhancement, not done here to keep scope
  contained.
- Affinity/basket candidate pool: 200 (plan says "e.g. top-200", took it literally).
- Excel row limit: 50 000 (brief's own suggested default).

## Frontend API-контракт (для TASK-409, backend-developer's C# НЕ читати напряму)

Base route: `/api/marketing-analytics`. Auth: `Authorize` (store_manager+ or above) +
tenant must have the `marketing_analytics` module active (403 `{"error":"Module not activated"}`
otherwise, same shape as every other `[RequireModule]` controller). All responses camelCase
(ASP.NET Core default `System.Text.Json` web policy — no custom config).

### Shared filter query params (every GET below, plus `/explain`)
```
period?: "3m" | "6m" | "12m" | "all" | anything-else   (default "6m" if no from/to either)
from?: "YYYY-MM-DD"
to?: "YYYY-MM-DD"                 -- from+to together always override period
storeIds?: string[]               -- repeated param: ?storeIds=guid1&storeIds=guid2; omitted/empty = all stores
```

### `RfmSegmentKey` (string enum on the wire, exact spelling, case-insensitive on the way in)
`"Champions" | "Loyal" | "CannotLoseThem" | "AtRisk" | "New" | "PotentialLoyalist" |
"Promising" | "AboutToSleep" | "Hibernating" | "Lost" | "NeedsAttention"`

### `GET /overview` → `RfmOverviewDto`
```ts
{
  periodFrom: string; periodTo: string;              // "YYYY-MM-DD"
  periodCustomerCount: number; periodRevenue: number;
  registeredCustomerCount: number; everPurchasedCustomerCount: number;
  everPurchasedSharePercent: number;                  // 0-100
  segments: {
    key: RfmSegmentKey; labelUa: string; shortDescriptionUa: string;
    customerCount: number; sharePercentOfPeriodCustomers: number;
    revenue: number; sharePercentOfPeriodRevenue: number;
  }[];                                                 // ALWAYS 11 entries, in priority order, zero-count ones included
  noPurchase: { customerCount: number; sharePercentOfRegisteredBase: number };
  filtersHash: string; calculatedAt: string;           // ISO8601 with offset
}
```

### `GET /segments/{key}` → `RfmSegmentDetailDto`
```ts
{
  key: RfmSegmentKey; labelUa: string; shortDescriptionUa: string; customerCount: number;
  topProducts: { rank: number; productName: string; coveragePercent: number;
                 uniqueCustomerCount: number; receiptCount: number; barcode: string | null }[];
  behavior: {
    peakDayOfWeekIso: number | null;   // 1=Mon..7=Sun (ISO) — frontend owns the label mapping
    peakHour: number | null;           // 0-23, Europe/Kyiv
    averageTicket: number; receiptCount: number; receiptsPerCustomer: number;
    lastVisit: string | null;          // "YYYY-MM-DD"
    averageRecencyDays: number; averageLtv: number; totalLtv: number;   // LTV always all-time
    byDayOfWeek: { dayOfWeekIso: number; sharePercent: number }[];       // up to 7 entries
    byHour: { hour: number; sharePercent: number }[];                   // up to 24 entries
    topPeakHours: { hour: number; sharePercent: number }[];             // top 3 by receipts
  };
  recommendation: { triggerUa: string; actionUa: string; offerUa: string; cautionUa: string;
                    productsForPromo: string[] };
  filtersHash: string; calculatedAt: string;
}
```
Empty segment (0 customers) still returns 200 with all numeric fields at 0, `lastVisit: null`,
`topProducts: []`, distributions `[]`, and a fully-populated `recommendation` (templates
generate sensible zero-KPI copy) — never 404, never throws.

### `GET /segments/{key}/products/{productName}/affinity` → `RfmAffinityResultDto`
`{productName}` must be URL-encoded by the caller (free-text, may contain spaces/Cyrillic/
apostrophes). Optional `?limit=10` (1-50, default 10 if omitted/out of range).
```ts
{
  segmentKey: RfmSegmentKey; anchorProductName: string;
  items: { productName: string; lift: number; bothBuyersCount: number;
           shareAmongAnchorBuyersPercent: number; shareAmongSegmentPercent: number;
           barcode: string | null }[];   // sorted by lift desc, may be []
  filtersHash: string; calculatedAt: string;
}
```

### `GET /segments/{key}/products/{productName}/basket` → `RfmBasketResultDto`
Same `{productName}` encoding + `?limit=` as affinity — different formula, NOT the same numbers.
```ts
{
  segmentKey: RfmSegmentKey; anchorProductName: string;
  items: { productName: string; togetherSharePercent: number; bothReceiptsCount: number;
           barcode: string | null }[];
  filtersHash: string; calculatedAt: string;
}
```

### `POST /segments/{key}/explain` (no body — filter via query string, same as the GETs above)
`200 → ExplainRfmSegmentResultDto`: `{ explanationUa: string; model: string; tokensUsed: number }`
`503 → { error: string }` when no Claude key is configured (tenant integration_configs nor env).

### Exports — all 3 are `POST`, JSON body, response is the raw `.xlsx` file
(`Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`,
`Content-Disposition` filename like `rfm_Champions_segment_20260726_143000.xlsx`) — use
`frontend/lib/download.ts`'s existing `downloadFile()` per the plan, no new file-handling code.

```
POST /exports/segment
Body: { key: RfmSegmentKey, from: "YYYY-MM-DD", to: "YYYY-MM-DD", storeIds: string[] | null, unmaskPii: boolean }

POST /exports/product-buyers
Body: { key, from, to, storeIds, unmaskPii, productName: string }

POST /exports/product-pair-buyers
Body: { key, from, to, storeIds, unmaskPii, productName: string, pairedProductName: string }
```
`unmaskPii: true` only actually unmasks if the caller is store_manager+ (or holds the new
`marketing_analytics.export_pii` TenantRole capability) — otherwise it's silently masked, never
a 403. UI should probably hide/disable the "show full phone" checkbox below store_manager rank
rather than let a lower-rank user tick a box that quietly does nothing (their call — not
enforced any particular way server-side beyond "never actually leak PII to someone unauthorized").
Excel columns (fixed order): Ім'я, Телефон (masked as `+380 XX *** ** 67` unless unmasked), Email,
Кількість замовлень (за весь час), Сума покупок (за весь час). Row cap 50 000 with a visible
red banner row if truncated.

## Не в скоупі / для наступних агентів

- **frontend-developer (TASK-409)**: `frontend/features/marketing-analytics/` per CLAUDE.md's
  layout — API contract above is the full source of truth, don't read the C#.
- **security-reviewer**: parametrization of every raw-SQL query in
  `MarketingAnalyticsRepository.cs` (all use `{n}` positional args, never string-interpolated —
  self-reviewed carefully, but this is exactly the kind of file the plan calls out for mandatory
  review); the `CanViewAnalytics` base-floor decision; PII-masking + capability-gate on exports;
  the test-infra `ConfigureWarnings` edit to `LoyaltyRepositoryIntegrationTests.cs` noted above.
- **documentation-writer**: `.claude/docs/glossary.md` (RFM, R/F/M-скор, LTV all-time vs
  windowed, lift/affinity, "разом у чеку" — plan lists these as missing), `api-contracts.md`
  (the contract above), `database-schema.md`/`decisions.md` if an ADR is wanted for the
  first-raw-SQL-repository precedent.
- Causal/incremental analysis (control groups, diff-in-diff) — explicitly out of scope per brief
  (future Фаза 5).
