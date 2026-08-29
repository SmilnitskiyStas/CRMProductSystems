# TASK-637 — Table unification Batch C (price-segments)

**Agent:** frontend-developer
**Status:** done

## Scope

Migrated 3 CSS-grid "fake tables" in `frontend/features/marketing-analytics/price-segments/` to the
shared `components/ui/Table.tsx` (Batch C of the 9-batch table-unification migration; Batch A —
the `Table` component itself — already done, commit referenced in task log 636):

- `components/PriceAudienceTable.tsx`
- `components/FrequencyView/FrequencyAudienceTable.tsx`
- `components/AllTimeView/AllTimeCustomerTable.tsx`

All three keep their existing state/hooks/data-fetching untouched — only the grid markup +
`TableControls.tsx`'s `SortableHeader`/`TablePaginationFooter` were swapped for `<Table>`,
wired straight through to the same `sortBy`/`sortDescending`/`onSort`/`page`/`totalPages`/
`onPageChange` props.

## Deviations (per Table's non-negotiable alignment rule, same as Batch A's ProductsTable)

- Numeric columns (items/check/ltv/purchases/spend/delta/etc.) were previously explicitly
  right-aligned via CSS grid. Table's rule is "column 0 left, everything else center, no
  per-column override without a genuinely good reason" — none of these columns had one, so they
  now render center-aligned, matching ProductsTable's precedent and the brief's own verification
  step ("first column left-aligned / rest centered").
- `FrequencyAudienceTable`'s "CHANGE, %" column stays unsortable (no `sortKey`) — preserved
  exactly, verified no sort button renders on it.
- Pagination footer text changes from the feature-specific `"{count} клієнтів/покупців"` label to
  `Table`'s generic `Common.totalLabel` ("Усього: {count}") — `Table`/`Pagination` has no prop
  for a custom label. The specific noun ("clients"/"buyers") is still shown in each table's header
  area above the grid (untouched, outside migration scope), so the information isn't lost, just no
  longer duplicated in the footer. Passed `totalCount={data.totalCount}` explicitly so the number
  itself stays correct.
- Row-dependent styling (phone color when null, delta sign color, null-check dash color) moved
  from `column.cellStyle` (static, can't vary per row) into inline `<span>` styles inside each
  column's `render()`, same pattern as `ProductsTable`'s status badge.

## Verification

- `npx tsc --noEmit` in `frontend/`: clean for all 3 migrated files. Two pre-existing errors
  remain in unrelated files (`audience-builder/.../MatchedItemsTable.tsx`,
  `users/components/UsersList.tsx`) — confirmed via `git status` these are dirty from other
  concurrently-running agents (audience-builder is one of the two other batches still importing
  `TableControls.tsx`), not caused by this change.
- `npx eslint` on the 3 files: clean.
- Manually verified in-browser (dev server + backend already running, logged in as network admin
  with marketing-analytics access):
  - Purchase-amounts tab → `PriceAudienceTable` (Stable audience): renders, real request
    `GET .../price-segments/audiences/Stable?...&sortBy=check...`, clicking CUSTOMER header
    re-fetches with `sortBy=name`.
  - Frequency tab → `FrequencyAudienceTable` (Growing, 1258 rows / 126 pages): renders, header
    alignment confirmed via `getComputedStyle` (`CUSTOMER` → left, all others → center),
    `CHANGE, %` header has no button (unsortable, as before), clicking "Next" re-fetches
    `page=2` with the rest of the params unchanged.
  - All-time tab → `AllTimeCustomerTable` (1272 rows / 128 pages): renders, real request
    `GET .../all-time/customers?page=1&pageSize=10&sortBy=check...`, no recommendation block
    (correct — that DTO has none).
  - Screenshot compositing unavailable in this environment (known limitation) — alignment
    verified via `getComputedStyle` instead, per the brief.

## TableControls.tsx

Confirmed NOT imported by any of the 3 migrated files anymore (only self-referential mentions in
my own doc comments matched a grep for the name). **Did not delete the file** — confirmed it is
still imported by `post-campaign/components/CustomerTable.tsx` and
`audience-builder/components/BuyersTab/BuyersTable.tsx`, both owned by other in-flight batches.
