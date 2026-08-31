# TASK-651 (T4) — coverage-aware region filter + `GET /api/marketplace/suppliers/{id}/coverage`

**Status:** done (committed to main) · **Agent:** backend-developer
Plan: `eventual-whistling-rabbit.md`, T4 of T1–T16. Depends on T3 (TASK-650, on main — same files).

## What changed

### 1. Region filter now matches delivery coverage
- `IMarketplaceRepository` / `MarketplaceRepository`: `region` → `regionCode` on
  `GetPublicSuppliersAsync`, `CountPublicSuppliersAsync`, `SearchSuppliersAsync`; `BuildPublicQuery`
  param renamed too.
- New private `MarketplaceRepository.ApplyRegionCoverageFilter(IQueryable<SupplierProfile>, string)`:
  a profile matches when `DeliveryCoverage.served` contains the code **and** it is **not** in
  `notServed`; a profile whose `DeliveryCoverage IS NULL` (legacy, pre-backfill) falls back to a
  free-text `Region ILIKE` against **either the raw code or the region's Ukrainian name**
  (`UkraineRegions.Find(code)?.NameUa`, resolved inside the repo so the interface stays a plain
  rename). Used by both `BuildPublicQuery` and `SearchSuppliersAsync`.
- **jsonb approach:** clean `EF.Functions.JsonContains` → Postgres server-side `@>` (same mechanism
  as the existing `Categories` filter). Verified via `ToQueryString()` — nothing client-evaluated,
  no `GetDbConnection()` / raw SQL / session `SET`, everything still inside the existing
  `IProviderRlsOverride.ExecuteAsync` block. KI-036 / ADR-035 standing rule holds; the standing
  review-comment block at the top of `MarketplaceRepository.cs` is unchanged and still accurate.
- `MarketplaceService.NormalizeRegionCode` runs the incoming filter string through
  `UkraineRegions.TryMatchFreeText` before it reaches the repo → a bare code passes through, a
  legacy free-text name (`"Київська область"` → `UA-32`) resolves, anything unrecognized becomes
  `null` (no filter). This also keeps an unvalidated value out of the interpolated jsonb fragment.

Generated SQL (region clause):
```
WHERE s."IsPublic" AND (
  (s."DeliveryCoverage" IS NOT NULL
   AND s."DeliveryCoverage" @> @__servedJson_1          -- '{"served":[{"regionCode":"UA-32"}]}'
   AND NOT (s."DeliveryCoverage" @> @__notServedJson_2))  -- '{"notServed":["UA-32"]}'
  OR
  (s."DeliveryCoverage" IS NULL AND s."Region" IS NOT NULL
   AND (s."Region" ILIKE @__Format_3 ESCAPE '' OR s."Region" ILIKE @__Format_4 ESCAPE ''))  -- '%UA-32%', '%Київська%'
)
```

### 2. `GET /api/marketplace/suppliers/{id}/coverage` — new endpoint
- `MarketplaceController.GetSupplierCoverage` — `[HttpGet("suppliers/{id:guid}/coverage")]`,
  `[Authorize]` + `[RequireModule("marketplace")]` (matches the other two authenticated actions in
  this controller — not anonymous; needs the caller's tenant to derive their region). Optional
  `?buyerRegionCode=`. 404 when the supplier is missing/unpublished.
- `MarketplaceService.GetSupplierCoverageForBuyerAsync(supplierId, buyerRegionCodeOverride,
  callerTenantId, ct)`:
  - supplier profile via the provider-bypass `GetSupplierByIdAsync` (metrics come joined — no extra
    round trip); unpublished → `null` (BUG-010 parity).
  - `DeliveryCoverage` parsed via `DeliveryCoverageJson.Parse`; null → empty `DeliveryCoverageDto`.
  - **buyer region:** valid `buyerRegionCodeOverride` wins; else the caller tenant's **primary
    location** = first active `Location` by `CreatedAt` that has a `RegionCode`
    (`ILocationRepository.GetAllAsync`, already caller-tenant + store-scope RLS'd); else `null`.
  - `buyerRegionStatus` = `served` / `not_served` / `unknown`; `buyerRegionTerms` = the served
    entry's terms.
  - measured days: `SupplierMetrics.DeliveryByRegion` entry whose `regionCode` == buyer region;
    else null.
- `MarketplaceService` now also takes `ILocationRepository` (already registered; same dependency
  `MarketplaceOrderService` uses).

### 3. `region` → `regionCode` rename
- `GET /api/marketplace/suppliers` query param.
- `SupplierSearchDto.Region` → `RegionCode` (`POST /api/marketplace/search` body) — coverage-match
  semantics apply there too.
- `IMarketplaceService.GetPublicSuppliersAsync` param.

## "Primary location" decision
No first-class primary/default-location flag exists in the schema (`Location` has no `IsPrimary`;
the frontend `usePrimaryStoreId` is a localStorage store-selector value, not server state). Picked
**oldest active `Location` with a non-null `RegionCode`** — deterministic, and `GetAllAsync` is
already scoped to the caller tenant (and to a store-scoped user's assigned locations) by RLS.
Documented on the method.

## `GET suppliers/{id}/coverage` response shape (camelCase, ASP.NET default)
```json
{
  "coverage": {
    "served": [{ "regionCode": "UA-32", "terms": "2-3 дні, від 5000 грн" }],
    "notServed": ["UA-43"],
    "note": "Доставка Новою Поштою за домовленістю"
  },
  "buyerRegionCode": "UA-32",
  "buyerRegionStatus": "served",
  "buyerRegionTerms": "2-3 дні, від 5000 грн",
  "measuredAvgDeliveryDaysToBuyerRegion": 2.4,
  "measuredSampleSize": 17
}
```
`buyerRegionStatus` ∈ `served | not_served | unknown`. When the buyer region can't be resolved:
`buyerRegionCode: null`, `buyerRegionStatus: "unknown"`, terms/measured all null. Coverage is never
premium-gated.

## Tests
- `MarketplaceServiceTests` (+15): region-code normalization (code passthrough / legacy-name →
  code / unrecognized → null) for search + list; `GetSupplierCoverageForBuyerAsync` —
  missing/unpublished → null, override served/not_served, invalid override → primary-location
  fallback, oldest-active-with-region pick, unresolved → unknown, served-region-outside-both-lists
  → unknown, null coverage → empty DTO, measured-days lookup hit/miss.
- `MarketplaceRepositoryCoverageFilterIntegrationTests` (new, live Postgres): served-yes,
  not-served, in-both-lists (notServed guard wins → excluded), legacy fallback by name, legacy
  fallback by raw code, legacy no-match, non-null coverage for another region (fallback skipped);
  count == list; `SearchSuppliersAsync` applies the same match.
- `MarketplaceProviderBypassScopeRlsIntegrationTests` — 3 `new MarketplaceService(...)` sites
  updated for the new ctor arg.

## Build / tests
`dotnet build ShelfGuard.sln` — 0 errors (1 pre-existing warning, `MarketplaceServiceTests.cs`
line 875, untouched test). `dotnet test --filter "FullyQualifiedName~Marketplace"` — 283/283.

## Notes / follow-ups
- `AiRecommend` passes its free-text `dto.Region` into the (now-normalizing) search/list path —
  recognizable names still resolve to codes, unrecognized ("Вся Україна") just stop pre-filtering;
  the region still reaches the AI prompt. No behavior regression worth fixing here.
- Frontend `regionCode` wiring, openapi.json regen → T8 / T15.
