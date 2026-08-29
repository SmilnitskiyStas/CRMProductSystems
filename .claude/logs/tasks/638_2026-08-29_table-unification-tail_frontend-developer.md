# TASK-638 — Table unification: final 3 files + dead-code cleanup

**Agent:** frontend-developer
**Status:** done

## What

Migrated the 3 files left over from the table-unification migration (previous agents were
cut off mid-batch) to the shared `Table` component (`frontend/components/ui/Table.tsx`):

1. `frontend/features/analytics/components/PosCashierStatsTable.tsx` — plain table → `Table`,
   5 columns (cashier name left-aligned via column-0 default, revenue/receipts/avg-ticket/shifts
   center via default; kept monospace + accent colors via `cellStyle`).
2. `frontend/features/supplier-cabinet/components/CooperationRequestsTab.tsx` — plain table →
   `Table`, 5 columns (client/message/date/status/actions); filter-tab state, mutations, and
   `ReasonModal` dialogs untouched; loading/empty states now driven by `Table`'s
   `isLoading`/`emptyMessage` props (kept the existing "all" vs "filtered" empty copy split).
3. `frontend/features/provider/components/StatsTab.tsx` — CSS-grid pseudo-table → `Table`,
   6 columns (member info left-aligned, 5 numeric `StatCell` columns center via default;
   dropped the now-redundant `textAlign: "center"` from `StatCell` itself since the column wrapper
   already centers).

All three preserve behavior/visuals exactly — no sort, no pagination, no row-click on any of them
(matches source).

## Cleanup

Grepped the whole `frontend/` tree for imports of `SortableHeader` / `TableControls`
(`from ".../SortableHeader"` / `from ".../TableControls"`) — zero matches. Deleted both:
- `frontend/components/ui/SortableHeader.tsx`
- `frontend/features/marketing-analytics/price-segments/components/TableControls.tsx`

## Verification

- `npx tsc --noEmit` — clean, no errors.
- `npm run lint` — clean, no warnings/errors.
- Runtime check (log in + render 3 tables) not performed: Docker Desktop is down locally
  (no DB), so the full dev stack couldn't be brought up in this session. Typecheck + lint are
  clean and the JSX/prop mapping was reviewed line-by-line against each original file's markup
  and against 2 already-migrated sibling files (`WorstProductsTable.tsx`, `CabinetItemsTable.tsx`)
  for pattern consistency.

## Files touched

- `frontend/features/analytics/components/PosCashierStatsTable.tsx`
- `frontend/features/supplier-cabinet/components/CooperationRequestsTab.tsx`
- `frontend/features/provider/components/StatsTab.tsx`
- Deleted: `frontend/components/ui/SortableHeader.tsx`,
  `frontend/features/marketing-analytics/price-segments/components/TableControls.tsx`
