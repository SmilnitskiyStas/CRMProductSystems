# TASK-515 — Header store selector: single → multi-select, remove 4 duplicate pickers

**Date:** 2026-08-13
**Agent:** frontend-developer
**Status:** done

## What changed

- `frontend/lib/useStoreContext.ts` — `selectedStoreId: string|null` → `selectedStoreIds: string[]`
  (`[]` = all stores) + `initialized` flag (tells "never touched" apart from "explicitly chose all
  stores", both empty arrays). Added `usePrimaryStoreId()` derived hook for single-store contexts.
- `frontend/components/layout/StoreSelector.tsx` — rewritten as checkbox multi-select popover
  (select-all row + per-store checkboxes + Done button), same pattern the 4 marketing-analytics
  filter bars already used. Mount effect resolves the one-time default (user.storeId → first
  store) only while `!initialized`; afterwards an explicit "all stores" choice survives a stores
  refetch instead of being clobbered back to one store.
- Rewired to `usePrimaryStoreId()`: `useDashboard.ts` (5 hooks), `QuickActions.tsx`, `stock/page.tsx`,
  `analytics/page.tsx`, `analytics/pos/page.tsx` — unchanged single-store behavior.
- `useAuth.ts` — login/logout reset to `{ selectedStoreIds: [], initialized: false }`.
- 4 marketing-analytics pages (RFM, price-segments, audience-builder, post-campaign) now read
  `storeIds` from `useStoreContext` instead of local `useState`.
- Deleted the duplicate store-picker UI from `PeriodStoreFilterBar.tsx`, `ComparisonFilterBar.tsx`,
  `AudiencePeriodBar.tsx`, `AfterPeriodBar.tsx` (state, handlers, JSX, unused imports, stale doc
  comments).
- i18n: added `allStores`/`storesCount`/`selectAllStores`/`doneButton` to `Dashboard.storeSelector`
  in `uk.json`/`en.json`.

## Deviation from brief (noted per CLAUDE.md judgment-call rule)

Brief's Step 5 examples said to drop only `onStoreIdsChange` from the 4 pages' filter-bar JSX
calls, keeping `storeIds={storeIds}`. But Step 6 explicitly requires removing `storeIds` from
those components' `Props` interfaces too (they no longer render anything with it). Keeping the
prop passed while removing it from `Props` fails `tsc` (JSX excess-property check). Resolved by
also dropping `storeIds` from the 4 JSX calls into the filter-bar components specifically —
`storeIds` is still passed to every other downstream consumer (tables, panels, memoized filter
objects) exactly as the brief specifies elsewhere.

## Build/verify

- `npx tsc --noEmit` — clean, 0 errors.
- `npx eslint` on all 17 touched files — clean, 0 errors/warnings.
- Browser sanity check skipped: port 3001 (this repo's only dev-server config) is held by another
  chat session; did not repurpose it or touch shared config to work around that.

## Files touched

`frontend/lib/useStoreContext.ts`, `frontend/components/layout/StoreSelector.tsx`,
`frontend/features/dashboard/hooks/useDashboard.ts`,
`frontend/features/dashboard/components/QuickActions.tsx`,
`frontend/app/(dashboard)/stock/page.tsx`, `frontend/app/(dashboard)/analytics/page.tsx`,
`frontend/app/(dashboard)/analytics/pos/page.tsx`, `frontend/features/auth/hooks/useAuth.ts`,
`frontend/app/(dashboard)/marketing-analytics/page.tsx`,
`frontend/app/(dashboard)/marketing-analytics/price-segments/page.tsx`,
`frontend/app/(dashboard)/marketing-analytics/audience-builder/page.tsx`,
`frontend/app/(dashboard)/marketing-analytics/post-campaign/page.tsx`,
`frontend/features/marketing-analytics/components/PeriodStoreFilterBar.tsx`,
`frontend/features/marketing-analytics/price-segments/components/ComparisonFilterBar.tsx`,
`frontend/features/marketing-analytics/audience-builder/components/AudiencePeriodBar.tsx`,
`frontend/features/marketing-analytics/post-campaign/components/AfterPeriodBar.tsx`,
`frontend/messages/uk.json`, `frontend/messages/en.json`.
