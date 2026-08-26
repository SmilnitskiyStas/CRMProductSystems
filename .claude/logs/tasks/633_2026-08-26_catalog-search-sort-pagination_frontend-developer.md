# TASK-633 — Catalog/Inventory: search, category filter, sortable columns, pagination (frontend)

**Status:** done (frontend) · **Agent:** frontend-developer · Parallel counterpart to TASK-632
(backend-developer, `GET /api/categories` + `sortBy`/`sortDescending` on `/api/items`).

## Bug fixed

`productsApi.getAll()` called `GET /api/items` and did `.then(r => r.items)`, discarding the
`PagedResult<Product>` envelope and never sending `page`/`pageSize` — the Catalog page was
silently capped at the backend's default `pageSize=50` with no indication more products existed.
Same bug class as this week's Receipts/Transfers/WriteOffs/Stock fix (TASK-629/630/631).

## What changed

- `features/inventory/api/products.ts` — `getAll` now takes `category_id`, `page`, `pageSize`,
  `sortBy`, `sortDescending` (search already existed) and returns the full
  `PagedResult<Product>` instead of unwrapping `.items`.
- `features/inventory/api/categories.ts` (new) — `categoriesApi.getAll()` → `GET /api/categories`.
- `features/inventory/hooks/useCategories.ts` (new) — `useQuery`, `staleTime: 5min` (matches
  `useDashboard.ts`'s `useStoreZones` precedent for slow-moving reference data).
- `features/inventory/hooks/useProducts.ts` — rewritten. `useProducts(params)` keeps returning a
  flat `Product[]` via `select: (r) => r.items` so its one other external caller
  (`app/(dashboard)/sales/page.tsx`) needed zero changes. New `useProductsPaged(params)` returns
  the raw `PagedResult<Product>` (no `select`) for the Inventory page's pagination footer — same
  `queryKey`/`queryFn` as `useProducts` (factored into a shared `productsListQuery` helper) so
  React Query dedupes the network request if both were ever used together. `useProductsByIds`/
  `useProductSearch` each got `select: (r) => r.items` added so their own external callers
  (`EventDetailPanel.tsx`, `EventProductPicker.tsx`) see no shape change.
- `features/inventory/types.ts` — added `CategoryDto` and `ProductSortBy` (`"name" | "barcode" |
  "category" | "purchaseprice" | "retailprice" | "minstock" | "maxstock"`), confirmed against
  the backend's `ItemSortKeys` allowlist (see below) — exact match, no drift.
- `features/inventory/components/ProductsTable.tsx` — added `sortBy`/`sortDescending`/`onSort`
  props; wired `SortableHeader` (shared component) onto barcode/name/category/purchase/retail/
  min/max columns; class/itemType/unit/status/actions stay plain.
- `app/(dashboard)/inventory/page.tsx` — added debounced (300ms) search, a category `<select>`
  (populated from `useCategories()`, "All categories" default option), sort state (default
  `name`/ascending, matching the backend's default-key convention; switching to a new column
  defaults to descending, matching Stock/Receipts/Transfers/WriteOffs' `handleSort` precedent),
  and the shared `Pagination` component. `page` resets to 1 on any search/category/sort change.
  Switched from `useProducts()` to `useProductsPaged()` to get `totalCount`/`totalPages`.
- `messages/{uk,en}.json` — added `Dashboard.inventory.page.searchPlaceholder` /
  `.allCategories`, mirroring Stock's `stockFilters.searchPlaceholder`/`.allStatuses` wording
  convention. `Common.prev/next/pageOf/totalLabel` already existed (used by `Pagination.tsx`),
  no changes needed there.

## Cross-check against backend (TASK-632, read directly from its source — no task log existed
yet at verification time)

Confirmed exact match, no adjustment needed:
- `ItemsController.GetAll`: `category_id`, `sortBy`, `sortDescending` query params present.
- `ItemSortKeys` allowlist: `name` (default), `barcode`, `category`, `purchaseprice`,
  `retailprice`, `minstock`, `maxstock` — identical set/spelling to this frontend's
  `ProductSortBy`.
- `CategoriesController` → `GET /api/categories` → `List<CategoryDto>`; `CategoryDto(Guid Id,
  string Name)` → camelCases to `{ id, name }`, matches this frontend's `CategoryDto` exactly.
- Categories are seeded in `DbSeeder.cs` (6 rows: dairy/veg/meat/dry/drinks/bread) — the dropdown
  will not be empty on the local dev DB.

## Verification

- `npx tsc --noEmit` — clean.
- `npm run lint` — clean, no new warnings.
- Full E2E **not possible yet**: backend (TASK-632) is still mid-edit — `dotnet build` currently
  fails with `CS0535` on two hand-written `IItemRepository` test fakes
  (`PosServiceTests.FakeCatalogRepo`, `FiscalizationRetryTests.RetryFakeCatalogRepo`) not yet
  updated for the new `GetPagedAsync` signature. This is the backend agent's in-progress work,
  not touched here (disjoint-files boundary). Structural correctness was instead confirmed by
  reading the backend source directly (contract match documented above). Live E2E (dropdown
  population, search narrowing, per-column sort requests, pagination, >50-item catalog no longer
  capped) is pending until TASK-632 lands and the backend builds cleanly — recommend a quick
  follow-up smoke pass once it does.

## Files touched

`frontend/app/(dashboard)/inventory/page.tsx`,
`frontend/features/inventory/api/products.ts`,
`frontend/features/inventory/api/categories.ts` (new),
`frontend/features/inventory/hooks/useProducts.ts`,
`frontend/features/inventory/hooks/useCategories.ts` (new),
`frontend/features/inventory/types.ts`,
`frontend/features/inventory/components/ProductsTable.tsx`,
`frontend/messages/uk.json`, `frontend/messages/en.json`.
