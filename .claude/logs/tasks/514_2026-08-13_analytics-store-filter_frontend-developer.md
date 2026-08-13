# TASK-514 — Analytics page ignores header store selector

**Agent:** frontend-developer
**Date:** 2026-08-13
**Status:** done

## Bug
`/analytics` never read the header's global store selector (`useStoreContext`), so every
analytics query was always network-wide regardless of which store the user picked at the top of
the app. `/analytics/pos` had its own local store `<select>`, fully decoupled from the header too.

## Changes

- `frontend/app/(dashboard)/analytics/page.tsx` — reads `selectedStoreId` from `useStoreContext`
  and threads it as `store_id`/positional param into every analytics hook call
  (`useExpirySummary`, `useWriteOffAnalytics(Compare)`, `useZoneAnalytics`, `useCategoryAnalytics`,
  `useLosses(Compare)`, `useLossesTrend`), and passes `storeId` to `CategoryDetailPanel`,
  `ProductTrendPanel`, and the two `LossesProductBreakdownPanel` call sites triggered by the
  reason/day drill-downs (the store-row drill-down was already correct — it passes the clicked
  row's concrete store id). Replaced the stale comment claiming "this page has no page-wide store
  filter of its own".
- `frontend/features/analytics/components/CategoryDetailPanel.tsx` — added optional `storeId?`
  prop, forwarded to `useCategoryProductBreakdown` as `store_id`.
- `frontend/features/analytics/components/ProductTrendPanel.tsx` — doc-comment-only update; logic
  (ADU fetch gated on `hasStore`, days-of-stock computation) was already generic and correct.
- `frontend/features/analytics/components/LossesProductBreakdownPanel.tsx` — updated the Props
  doc comment: storeId and reason can now both be set (backend already supports it — see
  `PosAnalyticsServiceTests.GetLossesByProductAsync_store_and_reason_filters_are_forwarded_unchanged`),
  not "exactly one" as previously documented.
- `frontend/app/(dashboard)/analytics/pos/page.tsx` — added `effectiveStoreId = storeId ||
  selectedStoreId || undefined` (same fallback pattern as `stock/page.tsx`), used everywhere the
  page previously did `storeId || undefined` (params memo, `usePosSummary(Compare)`,
  `usePosCashiers`, `PosDayDetailPanel`, `ProductTrendPanel`). Local `<select>` (`""` = all stores)
  is unchanged and still overrides the header once the user touches it.

## Out of scope (untouched, per bug brief)
`useAnalytics.ts`, `usePosAnalytics.ts`, `analytics.ts`, `pos-analytics.ts`, all backend files,
`useStoreContext.ts`, `StoreSelector.tsx` — these were already correctly wired; this was a
page-level wiring bug only.

## Verification
- `npx tsc --noEmit` in `/frontend` — no errors (including the new optional `storeId` prop on
  `CategoryDetailPanel`).
- Dev server: this project's shared dev server (port 3001, `.claude/launch.json`) was already
  running under another session; did not spin up a second instance to avoid a port/config
  conflict. Verified by static review of the diff against the working reference pattern
  (`stock/page.tsx`'s `effectiveStoreId`) and by confirming every touched hook/component prop
  already accepted `store_id`/`storeId` before this change (per the bug brief's prior
  investigation) — no runtime browser check performed.
