# TASK-640 — Category + numeric-range filters on 5 tables (frontend)

**Agent:** frontend-developer · **Date:** 2026-08-30 · **Status:** done

## What changed

**New shared component**
- `frontend/components/ui/RangeFilter.tsx` — controlled "від"/"до" number-input pair, 300ms internal debounce, empty string → `undefined` (never `0`/`NaN`). Placeholders ("від"/"до") come from `Common.rangeFrom`/`Common.rangeTo`; optional `placeholder` prop renders as a leading label.

**Stock** (`frontend/app/(dashboard)/stock/page.tsx`, `frontend/features/shelf/{api/stock.ts,hooks/useStock.ts,components/StockFilters.tsx}`)
- Added `category_id`, `min_quantity`, `max_quantity` through api → hook → page `Filters` → `StockFilters`.
- `StockFilters` gained a category `<select>` (via `useCategories`) and a `<RangeFilter>`; reset button and "has active filters" check now cover the new fields too.

**Inventory/Catalog** (`frontend/app/(dashboard)/inventory/page.tsx`, `frontend/features/inventory/{api/products.ts,hooks/useProducts.ts}`)
- Added `min_price`/`max_price` through `productsApi.getAll` and `ProductsListParams` (used by both `useProducts`/`useProductsPaged`).
- Page: `<RangeFilter>` next to the category select, wired to local `minPrice`/`maxPrice` state, added to page-reset effect deps.

**Receipts / Transfers** (`frontend/app/(dashboard)/{receipts,transfers}/page.tsx` + their `api`/`hooks`)
- Added `category_id`, `min_items`, `max_items`. Each page had no filters row before this — added one directly below the status-tabs+search row: category `<select>` + `<RangeFilter>`, using a new local `filterInputStyle` matching the page's existing search-input style.

**Write-offs** (`frontend/app/(dashboard)/write-offs/page.tsx` (`WriteOffsPageContent`), `frontend/features/write-offs/{api,hooks}`)
- Added `category_id`, `min_loss_amount`, `max_loss_amount`, same new-row pattern; labeled `<RangeFilter placeholder="Сума збитку, ₴">`. Existing client-side `reasonFilter` left untouched.

**i18n** (`frontend/messages/{uk,en}.json`)
- `Common`: added `rangeFrom`/`rangeTo` ("від"/"до", "from"/"to").
- Per-page: `Dashboard.shelf.stockFilters.{allCategories,quantityRangeLabel}`, `Dashboard.inventory.page.priceRangeLabel`, `Dashboard.receipts.page.{allCategories,itemsRangeLabel}`, `Dashboard.transfers.page.{allCategories,itemsRangeLabel}`, `Dashboard.writeOffs.page.{allCategories,lossAmountRangeLabel}` — added to both locales.

All new/changed query params use `!= null` checks (never truthy) so `0` is preserved as a valid bound, both in the api-layer query-string builders and in `RangeFilter`'s own parsing.

## Verification
- `npx tsc --noEmit` (frontend/) — 0 errors.
- `npm run lint` — 0 warnings/errors.
- Browser check against a live `frontend-dev` (port 3001) + `backend-dev` (port 5000, backend's parallel task already had the matching query params live):
  - Stock: category select + quantity range render; typing `5` → `min_quantity=5` sent, 200 OK; selecting a category → `category_id=<guid>` added, 200 OK.
  - Receipts: new category+item-count row renders below status tabs; typing `2` → `min_items=2`, then category select → `category_id=<guid>` added, both 200 OK.
  - Inventory: typed `0` into price-from → `min_price=0` sent (confirms the `!= null`, not-truthy check works for the zero-bound case).
  - Transfers, Write-offs: rendered correctly (category select + range filter visible); Write-offs typing `100` → `min_loss_amount=100`, 200 OK.
  - No console errors from the new components; only pre-existing connection-refused noise from before the backend finished starting.

## Files touched
- `frontend/components/ui/RangeFilter.tsx` (new)
- `frontend/app/(dashboard)/stock/page.tsx`
- `frontend/features/shelf/components/StockFilters.tsx`
- `frontend/features/shelf/api/stock.ts`
- `frontend/features/shelf/hooks/useStock.ts`
- `frontend/app/(dashboard)/inventory/page.tsx`
- `frontend/features/inventory/api/products.ts`
- `frontend/features/inventory/hooks/useProducts.ts`
- `frontend/app/(dashboard)/receipts/page.tsx`
- `frontend/features/receipts/api/receipts.ts`
- `frontend/features/receipts/hooks/useReceipts.ts`
- `frontend/app/(dashboard)/transfers/page.tsx`
- `frontend/features/transfers/api/transfers.ts`
- `frontend/features/transfers/hooks/useTransfers.ts`
- `frontend/app/(dashboard)/write-offs/page.tsx`
- `frontend/features/write-offs/api/writeOffs.ts`
- `frontend/features/write-offs/hooks/useWriteOffs.ts`
- `frontend/messages/uk.json`, `frontend/messages/en.json`

No commit/push made (main session handles git per instructions).
