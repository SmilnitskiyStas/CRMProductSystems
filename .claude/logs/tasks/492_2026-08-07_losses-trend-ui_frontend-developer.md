# TASK-492: Losses/write-offs trend chart UI

**Agent:** frontend-developer
**Date:** 2026-08-07
**Status:** done. No blockers.

## Scope

Consumed TASK-489's `GET /api/analytics/losses/trend` endpoint (`store_id`/`from`/`to`/`group_by`,
no compare-mode, shape verified fresh against `AnalyticsDtos.cs`/`AnalyticsController.cs` before
writing types). Built on top of TASK-488's already-merged `analytics/page.tsx` edits
(`selectedProduct`, product-click wiring) without reverting anything.

1. `frontend/features/analytics/types.ts` — `LossesTrendDto`/`LossesTrendPointDto` (camelCase),
   appended after the existing TASK-483 losses section.
2. `frontend/features/analytics/api/analytics.ts` — `getLossesTrend(params)`, manual
   `URLSearchParams` shape matching this file's own `getCategoryProductBreakdown`/
   `getLossesByProduct` convention (not `pos-analytics.ts`'s `buildQs`/entries-array helper —
   different file, different existing convention).
3. `frontend/features/analytics/hooks/useAnalytics.ts` — `useLossesTrend(params, enabled)`, full
   `[store_id, from, to, group_by]` object as query key, no `keepPreviousData`.
4. New `frontend/features/analytics/components/LossesTrendChart.tsx` — mirrors
   `PosRevenueTrendChart.tsx`'s `AreaChart` structure and click mechanism verbatim (recharts 3.8.1
   `activeTooltipIndex` resolved against the chart's own data array, not `activePayload`). Single
   series (`totalLoss`), red (`#F87171`) instead of blue, no `Line`/`Legend` (no compare mode).
   Tooltip wording/count-suffix convention matches the sibling `LossesByReasonChart`/
   `LossesByStoreChart` (same section, same domain) rather than copying `PosRevenueTrendChart`'s
   own revenue-period tooltip text.
5. Wired into `analytics/page.tsx`: new `useLossesTrend({ from, to }, enabled)` call — **not**
   gated by `!compareEnabled` (unlike `useWriteOffAnalytics`/`useLosses`), since this endpoint has
   no compare variant at all; matches `useExpirySummary`'s same-shaped ungated call on this page,
   not the flat/compare toggle pattern. New `selectedLossDay` state + `handleLossDayClick`
   (toggle-on-reselect, same convention as every other handler on this page). Rendered inside the
   existing "Write-offs" `<section>`, between the summary-card grid and `LossesByReasonChart`
   (chart height 220 to match its neighbors in that section, not `PosRevenueTrendChart`'s 260).
   Own independent loading gate (`lossesTrendLoading`) since it's a separate query from
   `writeoffsLoadingEffective`. On day click, reuses `LossesProductBreakdownPanel` unmodified —
   confirmed its prop shape is still exactly `{title, totalLoss, storeId?, reason?, from, to,
   onClose, onProductClick?}` — called with no `storeId`/`reason`, `from = to = selectedLossDay`,
   `title` built from the existing `lossesProductPanelTitle` i18n key (reused, not duplicated) with
   a long-form formatted date as `{value}` (same `toLocaleDateString` options `PosDayDetailPanel`
   uses). `totalLoss` sourced from the already-fetched `lossesTrend.points` (optimistic value,
   overridden once the panel's own fetch resolves) — same pattern the reason/store panels already
   use against their own parent data.
6. i18n: new `Dashboard.analytics.lossesTrendChart` block (`title`/`empty`/`tooltipLabel`/
   `tooltipDocsSuffix`/`hint`) in both `uk.json`/`en.json`, inserted next to
   `lossesByReasonChart`/`lossesByStoreChart`. No new panel-title key — reused the existing
   `lossesProductPanelTitle` key (see above).

No `group_by` day/week toggle added — out of scope, endpoint defaults to `"day"` server-side and
the brief only asked for a single-series chart, not a new UI control.

## Files

Changed: `frontend/features/analytics/types.ts`, `frontend/features/analytics/api/analytics.ts`,
`frontend/features/analytics/hooks/useAnalytics.ts`, `frontend/app/(dashboard)/analytics/page.tsx`,
`frontend/messages/uk.json`, `frontend/messages/en.json`.
New: `frontend/features/analytics/components/LossesTrendChart.tsx`.
Not touched: `CategoryDetailPanel.tsx`, `ProductTrendPanel.tsx`, backend files, anything under
`/analytics/pos`.

## Verification

- `npx tsc --noEmit` — 0 errors.
- `npm run lint` (`next lint`) — 0 warnings/errors.
- `npm run build` — exit 0, 57/57 static pages. `/analytics` 9.96 kB / 268 kB First Load JS (up
  from TASK-488's 9.27 kB / 263 kB — expected, new chart component + hook + query). Build log has
  the same repeated `ENVIRONMENT_FALLBACK` stack traces prior tasks already logged as pre-existing
  `next-intl` "no timeZone configured" noise, confirmed unrelated by grepping 29 other task logs
  that show the identical trace.
- Live dev server (`frontend-dev` launch config): the real `/analytics` route is in
  `middleware.ts`'s `PROTECTED` array and gets edge-redirected to `/login` with no session cookie
  **before Next ever compiles the page module** — confirmed by reading `middleware.ts` directly,
  not guessed. Docker itself isn't running this session (not just the containers — `docker ps`
  fails to reach the daemon), so there's no way to establish a real session. **Correction to a
  prior sibling log's precedent:** TASK-488's log describes testing `/uk/analytics` as a working
  substitute ("both routes compiled and redirected to /login cleanly"). Tried the same URL here and
  checked what actually rendered: it resolves to Next's `[...not-found]` catch-all (page text
  literally "Сторінка в розробці"/"page under development"), not `analytics/page.tsx` — it only
  *looks* like a pass because that not-found page shares the `(dashboard)/layout.tsx` wrapper,
  whose own auth check is what triggers the subsequent `/login` compile. It never exercised the new
  code. No live route to the real page was reachable this session; `npm run build`'s successful
  compile of the actual `/analytics` bundle (route table above) is the strongest signal available
  without a backend.

## Task log / current.md note

Wrote this log and prepended `current.md` after re-reading it fresh — TASK-490 (backend, worst-
products endpoint) had landed concurrently and is above this entry; no TASK-491 entry exists yet as
of this write. Neither touches any file in this task's scope.
