# TASK-485: Quick-win interactivity — ExpiryDonut click + POS revenue trend day drill-down

**Agent:** frontend-developer
**Date:** 2026-08-07
**Status:** done. No backend dependency, no blockers.

## Scope

Per `iterative-purring-sifakis.md` plan (TASK-485, zero backend deps, runs parallel to the
479-484 backend chain):

1. `ExpiryDonut.tsx` — new optional `onSliceClick?: (status: string) => void`. Stays
   presentational; the SLICES array now also carries a `status` value per slice (snake_case
   `needs_verification` for that one slice, matching the URL param the page already uses).
2. `PosRevenueTrendChart.tsx` — new optional `onDayClick?: (date: string) => void` and
   `selectedDay?: string | null`. Click resolves the nearest chart point to a `date` and fires
   `onDayClick`; cursor:pointer when active; hint line below the chart (mirrors
   `SegmentGrid.tsx`'s hint pattern) shown only while `onDayClick` is set and nothing is selected
   yet.
3. New `PosDayDetailPanel.tsx` — pure composition of the existing `PosSummaryCards` +
   `PosTopProductsTable` + `PosCashierStatsTable`, each fed by the existing
   `usePosSummary`/`usePosTopProducts`/`usePosCashiers` hooks called with `from = to = date`. Also
   threads the page's live `storeId` filter through (not in the brief's literal prop list, but
   needed — see Deviations below). No new hook, no new endpoint.
4. Wired both pages: `/analytics` passes `onSliceClick={(status) => router.push(\`/stock?status=${status}\`)}`
   — the exact navigation the MetricCards/table rows already do. `/analytics/pos` adds
   `selectedDay` state + toggle-on-reselect handler (mirrors `marketing-analytics/page.tsx`'s
   `handleSelectSegment`) and renders `PosDayDetailPanel` below the revenue trend section.
5. i18n: `Dashboard.analytics.pos.revenueTrend.hint` and new `Dashboard.analytics.pos.dayDetail.*`
   (`title`, `closeButton`, `noData`) in both `uk.json`/`en.json`.

## Deviation from the plan brief (verified against installed code, not assumed)

The brief's exact recharts click snippet (`state?.activePayload?.[0]?.payload`) is
**recharts@2 API**. This repo has **recharts 3.8.1** installed, where `activePayload` no longer
exists on the click callback's param at all — confirmed by reading
`node_modules/recharts/types/synchronisation/types.d.ts` (`MouseHandlerDataParam` has no
`activePayload` field) and `node_modules/recharts/es6/state/externalEventsMiddleware.js` (the
actual object built for every click handler: `activeCoordinate/activeDataKey/activeIndex/
activeLabel/activeTooltipIndex/isTooltipActive` — no payload at all). Using the brief's literal
snippet would have been dead code — always `undefined`, `onDayClick` never firing, `tsc` may or
may not even catch it depending on inference.

Used the real 3.x mechanism instead: `state.activeTooltipIndex` is a string index (confirmed via
`combineActiveTooltipIndex.js`) into the chart's own `data` array, resolved locally as
`chartData[Number(idx)]`. Same guard (`if (point?.date)`) and same net behavior/UX as specified.

Same investigation for `ExpiryDonut`: recharts 3.x dispatches per-sector Pie clicks through
`<Pie onClick>` (receiving the sector's flattened data entry, `status` field included), not
through `<Cell onClick>` — confirmed by reading `recharts/es6/polar/Pie.js` and
`context/tooltipContext.js`'s `useMouseClickItemDispatch`. Cell still carries the `cursor: pointer`
style (pure CSS passthrough, unaffected by the click-dispatch change); the actual `onClick` sits on
`<Pie>`.

Also added `storeId` to `PosDayDetailPanel` (brief didn't list it as a prop): without it, a
store-filtered trend chart's day-click would silently mix in every store's revenue in the detail
panel below — a real inconsistency, not a style choice. Threaded from `pos/page.tsx`'s existing
live `storeId` state, not snapshotted.

## Verification

- `npx tsc --noEmit` — 0 errors.
- `npm run lint` (`next lint`) — 0 warnings/errors.
- Live browser E2E **not done**: no ShelfGuard dev stack was running locally (Postgres/worker
  containers up, but no .NET API, no frontend dev server — port 3000 locally is an unrelated
  app). Standing up backend+DB+seed data+login was out of this task's scope (tsc/lint were the
  brief's stated bar) and risked colliding with the user's own running process on that port, so
  skipped rather than faked. Full click-through (`/analytics` slice → `/stock?status=`,
  `/analytics/pos` day → panel open/toggle-close) is exactly what TASK-486 (qa-tester) already
  covers per the plan.

## Files

Changed: `frontend/features/analytics/components/ExpiryDonut.tsx`,
`frontend/features/analytics/components/PosRevenueTrendChart.tsx`,
`frontend/app/(dashboard)/analytics/page.tsx`, `frontend/app/(dashboard)/analytics/pos/page.tsx`,
`frontend/messages/uk.json`, `frontend/messages/en.json`.
New: `frontend/features/analytics/components/PosDayDetailPanel.tsx`.

Untouched (explicitly out of scope, reserved for TASK-483/484):
`CategoryStatusChart.tsx`, `LossesByReasonChart.tsx`, `LossesByStoreChart.tsx`,
`PosTopProductsTable.tsx` row click, `ProductAnalyticsTab.tsx`, `types.ts`, `api/analytics.ts`,
`api/pos-analytics.ts`, `hooks/useAnalytics.ts`, `roles.ts`.
