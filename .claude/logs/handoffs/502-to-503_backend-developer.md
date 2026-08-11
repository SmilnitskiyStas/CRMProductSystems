# Handoff: TASK-502 (backend) → TASK-503 (frontend-developer)

Backend for the store-migration feature is done (`MarketingAnalyticsController.cs`). Full DTOs
live in `backend/ShelfGuard.Application/Features/MarketingAnalytics/Dtos/MarketingAnalyticsDtos.cs`
(search "Store migration"). Everything below reflects what was actually implemented, including
one deviation from the original plan — read the "3rd endpoint" note before wiring the customer
table.

## Endpoints

All under `[Authorize(Policy = AppPolicies.MarketingAnalyticsViewOrCapability)]` +
`[RequireModule("marketing_analytics")]`, same as every other endpoint on this controller — no
new auth wiring needed on the frontend beyond what the rest of the RFM dashboard already does.

### `GET /api/marketing-analytics/store-migration`

Query params (identical to every other GET on this controller): `period` (`"3m"|"6m"|"12m"|"all"`,
default 6m), `from`/`to` (explicit `DateOnly`, wins over `period` if both present), `storeIds`
(repeated, e.g. `?storeIds=guid1&storeIds=guid2`; omitted/empty = all stores).

Returns `StoreMigrationOverviewDto`:
```
ActiveCustomerCount: number       // customers with >=1 receipt in the period (store filter applied)
MigratedCustomerCount: number     // sum of Flows[].CustomerCount
MigratedSharePercent: number      // MigratedCustomerCount / ActiveCustomerCount * 100, 0 if ActiveCustomerCount=0
Flows: StoreMigrationFlowDto[]    // non-zero matrix cells only
NetFlowByStore: StoreNetFlowDto[]
PeriodFrom: string (yyyy-MM-dd)
PeriodTo: string (yyyy-MM-dd)
```
```
StoreMigrationFlowDto:  { FromStoreId, FromStoreName, ToStoreId, ToStoreName, CustomerCount, Revenue }
StoreNetFlowDto:        { StoreId, StoreName, Gained, Lost, Net }
```
No `FiltersHash`/`CalculatedAt` fields (unlike the RFM overview/segment DTOs) — the brief's exact
DTO shape omits them, so don't expect them on the wire.

### `GET /api/marketing-analytics/store-migration/customers` — **not in the original plan doc, added this round**

The plan/brief only specified 2 endpoints (this overview GET + the export POST below). But the
repository/service layer both explicitly build a customer-drill-down query meant for "on-screen
(small limit) and export (large limit)" use, and the feature's own goal includes a "drill-down
customer list" as a first-class deliverable — there was no way to serve that on-screen without a
3rd endpoint, so one was added, following this controller's existing pattern of separate
drill-down GETs (e.g. `segments/{key}/products/{productName}/affinity`).

Same query params as the overview GET, plus `limit` (int, clamped server-side: default 100,
max 500 — out-of-range values silently fall back to 100, never 400/error).

Returns `StoreMigrationCustomerRowDto[]`, ordered most-recent-migration-first:
```
CustomerId, Name, Phone, Email,
FromStoreId, FromStoreName, FromDate (yyyy-MM-dd),
ToStoreId, ToStoreName, ToDate (yyyy-MM-dd),
TransactionCountInPeriod, RevenueInPeriod
```
**Phone/Email are ALWAYS masked here** (e.g. `+380 67 *** ** 67`, `i***@test.com`) — there is no
unmask query param on this endpoint. Unmasking is only ever available via the export below (and
only for a caller `MarketingAnalyticsAuthorization.CanExportPii` clears). Do not build a client-side
"show unmasked" toggle against this endpoint — it will never return unmasked data no matter what
the caller passes.

### `POST /api/marketing-analytics/exports/store-migration`

Body: `ExportStoreMigrationRequest { StoreIds: Guid[] | null, From: date, To: date, UnmaskPii: bool }`
(no `Key` field — this export has no RFM-segment concept, unlike the other 3 exports on this
controller). Same response shape as every other export action: raw `.xlsx` bytes,
`Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, filename in
`Content-Disposition` (`store_migration_<timestamp>.xlsx`).

`UnmaskPii: true` in the request is honored **only** if the caller's role/capability passes
`MarketingAnalyticsAuthorization.CanExportPii` server-side (same as the other 3 exports) — the
existing client-side "am I allowed to unmask" role check already used elsewhere on this page
applies unchanged here; no new capability was introduced.

Excel columns (in order): Ім'я, Телефон, Email, Заклад (перша покупка), Дата першої покупки,
Заклад (остання покупка), Дата останньої покупки, К-сть чеків, Сума.

## Behavior notes relevant to UI design

- **Migration definition**: within `[from,to]`, a customer "migrated" if their earliest
  transaction's store differs from their latest transaction's store. Stores visited in between
  are NOT tracked as hops — a customer who visited store A→B→A in the period shows as "not
  migrated" (from=A, to=A), not as two flows.
- **Store filter semantics**: a flow/customer row matches the selected `storeIds` if EITHER the
  from-store OR the to-store is in the list (not AND). Selecting just one store surfaces both
  "customers who left this store" and "customers who arrived at this store."
- Both new GET endpoints return `200` with empty/zeroed data for a tenant/period with no
  migrations — never `404`, matching the rest of this dashboard's "empty state is still a valid
  DTO" convention.
- Single-store tenants will always get `Flows: [], NetFlowByStore: [], MigratedCustomerCount: 0`
  (there's no cross-store data to detect) — the plan's frontend section already calls for an
  empty-state guard keyed off `useStores().length <= 1`; the backend doesn't special-case this,
  it just naturally returns zeros.

## Files to reference

- `backend/ShelfGuard.Api/Controllers/MarketingAnalyticsController.cs` — the 3 new actions, look
  for the "Store migration (TASK-502)" section.
- `backend/ShelfGuard.Application/Features/MarketingAnalytics/Dtos/MarketingAnalyticsDtos.cs` —
  exact DTO shapes, "Store migration" section.
