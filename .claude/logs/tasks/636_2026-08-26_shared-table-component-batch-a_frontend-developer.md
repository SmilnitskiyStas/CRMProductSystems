# TASK-636 — Shared `Table` component (Batch A: foundation + pilot)

**Status:** done · **Agent:** frontend-developer

## What was built

`frontend/components/ui/Table.tsx` — new shared dark-theme data table, presentation-only
(no fetching/sort-comparator/pagination math). Visual language copied from
`ProductsTable.tsx`'s pre-migration baseline card (`#0D1117` bg, `#1F2937` border,
`#0A0F1A` header row). Sortable headers (chevron, click-to-toggle) render internally —
does not import `SortableHeader.tsx` or the price-segments `TableControls` fork, per the
brief, to avoid perpetuating their `align` divergence. Pagination footer reuses the
existing `Pagination.tsx`. `renderExpanded`/`expandedRowKey` exist structurally for later
batches but aren't wired into any of the 8 pilot files (none have expandable rows).

**Product rule implemented as a structural default:** `column.align ?? (index === 0 ?
"left" : "center")`, computed once per column and applied identically to `<th>` (including
the sort button's internal `justifyContent`) and every `<td>` in that column. `align` is a
per-column escape hatch, used only where a real reason exists (see deviations).

## Files migrated (8)

1. `frontend/features/inventory/components/ProductsTable.tsx` (+ `inventory/page.tsx` —
   no changes needed there, `ProductsTable`'s public prop interface is unchanged)
2. `frontend/features/shelf/components/StockTable.tsx` + `app/(dashboard)/stock/page.tsx`
3. `app/(dashboard)/write-offs/page.tsx`
4. `app/(dashboard)/transfers/page.tsx`
5. `app/(dashboard)/receipts/page.tsx`
6. `app/(dashboard)/locations/page.tsx` (still no pagination — `onPageChange` simply
   omitted, per brief)

## Cleanup

- `frontend/components/ui/table.tsx` (dead shadcn primitive, confirmed 0 imports) deleted,
  `Table.tsx` created. Windows case-insensitivity trap: `git mv` twice silently kept the
  OLD blob content staged under the new path (verified via `git cat-file`) — fixed with an
  explicit `git add` after the renames. Final `git status` shows a clean `A .../Table.tsx`
  / `D .../table.tsx` pair, not a folded rename.
- `SortableHeader.tsx` still imported by 11 non-pilot files (analytics, marketing-analytics
  audience-builder/price-segments) — left in place per brief, to be retired in a later batch.

## Deliberate deviations from a literal 1:1 port

- **ProductsTable column order**: reordered so `name` is column 0 (was `barcode`), so it
  gets left-align for free from the structural default instead of needing a manual
  override on the pilot/baseline file — this is the one visual change the brief called out
  for this file.
- **StockTable**: checkbox stays column 0 (`align: "center"` override), product name is
  column 1 (`align: "left"` override) — the "genuinely good reason" case the component's
  own docs anticipate. Bulk-select tint (`#1D3461`) and critical-row tint moved from
  inline `onMouseEnter` DOM mutation to the new `rowStyle` prop; hover (`#0F1825`) is now
  handled internally by `Table`.
- **Receipts** `id` column and **Transfers** `to` column: kept their existing position
  (no reorder) — the id/to columns are literally column 0/2, so they take the mechanical
  default (id: left, was center before; to: stays center, unchanged) rather than getting a
  manual override, per the brief's "derived automatically from index, not hand-set" intent.
- **StockPage / WriteOffs / Transfers / Receipts / Locations**: `isLoading`/empty text now
  render inside `Table`'s own card shell instead of each page's previous bespoke
  loading/empty markup (no card at all, or card-without-header). Minor, deliberate visual
  consistency improvement — exactly what this migration exists to produce — not a
  functional change.
- **StockPage**: removed the page's own outer card `<div>` wrapping `StockTable`, since
  `Table` now supplies its own card — kept only the "batches count" label above it.
  Avoids a double-bordered-box regression.
- **Locations** actions column: centered (`justify-content: center`) instead of the
  original's right-aligned flex row, matching every other pilot table's actions-column
  convention; no override set, so it takes the mechanical center default.

## Verification

- `npx tsc --noEmit` — clean. `npm run lint` — clean ("No ESLint warnings or errors").
- Logged into the running `frontend-dev`/`backend-dev` servers and exercised all 6 pages
  via computed-style checks (`getComputedStyle(...).textAlign`) plus live network-request
  inspection:
  - Column-0-left / rest-center confirmed on Inventory, Stock, Write-offs, Transfers,
    Receipts, Locations (including the checkbox+name exception on Stock).
  - Sorting: clicked a header on every page, confirmed `sortBy`/`sortDescending` changed
    in the outgoing `GET` request (server-paginated pages) and in the client-sorted row
    order (Locations).
  - Search: typed into Inventory's and Locations' search inputs, confirmed debounced
    request / client filter updated the row set.
  - Pagination: present on Inventory/Stock/Write-offs/Transfers/Receipts, absent on
    Locations, as required.
  - Row-level styling: Stock's checkbox-selected tint (`rgb(29,52,97)` = `#1D3461`) and
    Write-offs' pending-approval tint (`rgba(251,191,36,0.03)`) both verified present via
    `getComputedStyle`.
  - ActionMenu → "View" still opens the detail drawer on Inventory (confirmed via a fixed-
    position drawer element appearing with the clicked product's name/category).

## Pre-existing issue observed, not fixed (out of scope)

Locations page: one seed store has `locationType: "shop"`, which has no entry in
`Dashboard.locations.types` in `en.json`/`uk.json` (only `retail_store`, `warehouse`,
`auto_service`, `office`, `production`, `restaurant`). `next-intl` falls back to printing
the raw key path in the TYPE cell for that row. Confirmed pre-existing — the
`tTypes(loc.locationType)` call is unchanged, just relocated into the new column's
`render`. Not touched.
