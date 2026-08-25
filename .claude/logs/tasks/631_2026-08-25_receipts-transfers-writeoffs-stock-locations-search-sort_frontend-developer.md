# TASK-631 — Search + sortable headers for Receipts/Transfers/WriteOffs/Stock/Locations (frontend)

**Status:** done · **Agent:** frontend-developer · pairs with TASK-630 (backend contract)

## What changed

- New shared `frontend/components/ui/SortableHeader.tsx` — lifted out of
  `features/marketing-analytics/price-segments/components/TableControls.tsx` (left untouched,
  out of scope, same precedent as `Pagination.tsx`'s extraction).
- Server-side search+sort (Receipts, Transfers, Write-offs, Stock): added `search?`/`sortBy?`/
  `sortDescending?` to each feature's `api/*.ts` `getAll` params + `hooks/use*.ts`, sent only when
  defined/non-empty, included in the React Query `queryKey`. Sort-key string-literal unions added
  to each feature's `types.ts`, matching TASK-630's backend allowlists exactly (verified byte-for-
  byte against `{Receipt,Transfer,WriteOff,Stock}SortKeys.cs`).
- Each of the 4 pages: debounced (300ms) search `<input>` next to the status tabs, `sortBy`/
  `sortDescending` state with click-to-sort/click-again-to-flip on the relevant `<th>` headers via
  `SortableHeader`, `page` reset to 1 on search/sort change (same effect that already resets page
  on filter changes).
- Stock specifically: removed the old post-fetch `.filter()` in `stock/page.tsx` (the known
  bug — only matched within the loaded page, missed hits elsewhere) and `StockFilters`' search
  input now feeds the debounced value into `useStock(...)` instead. Stock's default sort is
  `expirydate` **ascending** (`sortDescending: false`), not the descending-default convention used
  elsewhere — matches `StockRepository`'s pre-existing FEFO-first order (confirmed against
  TASK-630's log before implementing, not just guessed).
- Locations (client-side only, no pagination on this page): added `searchText` state (filters
  `name`/`address`, case-insensitive) and `SortableHeader` on Name/Type, chained into the existing
  `filteredLocations` useMemo after the store-id filter. Copies the array (`[...result].sort(...)`)
  before sorting since `result` can still be the exact React-Query-cached `locations` reference.
- Added `searchPlaceholder` i18n keys to `en.json`/`uk.json` for receipts/transfers/write-offs/
  locations `page` namespaces (Stock reuses its pre-existing `Dashboard.shelf.stockFilters.
  searchPlaceholder`, unchanged).

## Verification

- `npx tsc --noEmit` — clean.
- `npm run lint` — clean.
- Backend (TASK-630) was already complete — ran full end-to-end against a live local
  `dotnet run` API + seeded Postgres, logged in as `ea@demo.local`:
  - Receipts: typed "Чумак" → narrowed 3→1 rows, request carried `search=`; clicked "Supplier"
    header → `sortBy=supplier&sortDescending=true`, row order changed; clicked again →
    `sortDescending=false`, order flipped back.
  - Stock: confirmed default request is `sortBy=expirydate&sortDescending=false`; searched
    "Молоко" → narrowed correctly, still expiry-ascending; clicked "Qty" → `sortBy=quantity&
    sortDescending=true`, rows re-sorted descending by quantity.
  - Transfers: clicked "To" → `sortBy=to&sortDescending=true` confirmed in the request.
  - Write-offs: clicked "Loss amount" → `sortBy=netloss&sortDescending=true` confirmed.
  - Locations: search narrowed/emptied the client-side list correctly (name + address match);
    sort click handler fires without error (single seeded location, so reordering itself
    wasn't visually distinguishable, but wiring/logic was code-reviewed).

## Issues found (out of scope, flagged separately)

- Locations page: the seeded location's `locationType` is `"shop"`, which isn't in the
  `LocationType` union or the `Dashboard.locations.types` translations — renders the raw
  untranslated key + logs a next-intl `MISSING_MESSAGE` console error. Pre-existing data/i18n
  mismatch, unrelated to this task. Flagged via a spawned task
  ("Fix unmapped \"shop\" locationType seed data").

## Files touched

`frontend/components/ui/SortableHeader.tsx` (new); `frontend/features/{receipts,transfers,
write-offs,shelf,locations}/types.ts`; `frontend/features/{receipts,transfers,write-offs,shelf}/
api/*.ts` + `hooks/use*.ts`; `frontend/features/shelf/components/StockTable.tsx`;
`frontend/app/(dashboard)/{receipts,transfers,write-offs,stock,locations}/page.tsx`;
`frontend/messages/{en,uk}.json`.
