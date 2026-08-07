# TASK-493: Worst-performing products / dead-stock table UI

**Agent:** frontend-developer
**Date:** 2026-08-07
**Status:** done. No blockers.

## Scope

Consumed TASK-490's `GET /api/analytics/pos/worst-products` endpoint (`store_id`/`from`/`to`/
`limit`, server clamped 1-100, shape verified fresh against `PosAnalyticsDtos.cs`'s
`WorstProductsDto`/`WorstProductRowDto` and `AnalyticsController.cs`'s `GetWorstProducts` action
before writing frontend types — confirmed no barcode field, sales fields always `0` not null,
`currentStock` always `> 0`, ascending by `salesRevenue`). Read `types.ts` fresh first and
confirmed TASK-492's `LossesTrendDto`/`LossesTrendPointDto` additions were already there —
appended after them without touching. Used the renamed `ProductTrendPanel.tsx` (TASK-488) — no
references anywhere to the old `PosProductTrendPanel` name.

1. `frontend/features/analytics/types.ts` — `WorstProductsDto`/`WorstProductRowDto` (camelCase),
   appended after `LossesTrendDto`.
2. `frontend/features/analytics/api/pos-analytics.ts` — `getWorstProducts(params)`, same
   `buildQs`/`rangeEntries` convention as `getTopProducts` right above it (store_id/from/to/limit).
3. `frontend/features/analytics/hooks/usePosAnalytics.ts` — `useWorstProducts(params, enabled)`,
   full filter object as query key (`["pos-analytics-worst-products", params]`), no
   `keepPreviousData`, placed directly after `usePosTopProducts`.
4. New `frontend/features/analytics/components/WorstProductsTable.tsx` — mirrors
   `PosTopProductsTable.tsx`'s structure/styling verbatim (same `thStyle`/`baseTd`/row hover/
   active-row `#111827` highlight mechanism, same `onRowClick?`/`selectedProductId?` prop shape).
   Two differences from the source table: no barcode column (`WorstProductRowDto` carries no
   barcode field, unlike `PosTopProductItem`), and one added column — `currentStock` — styled in
   amber (`#FBBF24`, bold) reusing the same color this feature already uses for "warning"-class
   counts (`CategoryDetailPanel.tsx`'s `p.warning` cell) rather than inventing a new color, since
   this is the "N units sitting unsold" evidence column that makes a zero-revenue row actionable.
5. Wired into `frontend/app/(dashboard)/analytics/pos/page.tsx` (read fresh first, confirmed
   `selectedProduct`/`handleProductClick` from TASK-484 and the `ProductTrendPanel` import from
   TASK-488 were both present and unmodified by anything else). Added `useWorstProducts({
   ...params, limit: "10" }, enabled)` alongside the existing `usePosTopProducts` call (same
   params shape, same `enabled` gate — no compare-mode variant exists for this endpoint). New
   `<section>` rendered directly below the existing "Top products + Cashiers" grid section and
   above the `ProductTrendPanel` conditional block, own independent loading gate (`worstLoading`).
   No new local state: `onRowClick={handleProductClick}` and
   `selectedProductId={selectedProduct?.id ?? null}` are the *exact* same values already passed to
   `PosTopProductsTable` a few lines above — clicking a row in either table drives one shared
   `selectedProduct` state and opens the same `ProductTrendPanel` instance at the bottom of the
   page. **Placement/heading decision:** did not add a page-level `<h2 style={sectionTitle}>`
   wrapper for this new section. Followed this page's own existing precedent instead —
   `PosTopProductsTable`/`PosCashierStatsTable` above it already render their own internal title
   bar (`t("title")` inside the card) and are *not* wrapped in an external `<h2>`, unlike the KPI-
   style sections (summary/revenue-trend/payment-pie) which have no internal title and *do* get an
   external `<h2>`. `WorstProductsTable` follows the same internal-title-bar pattern as its
   sibling table, so wrapping it in a second, redundant heading would have produced a visible
   double-title. The internal title itself ("Товари, що не продаються" / "Products not selling")
   already reads as clearly distinct from "Топ товари" / "Top products" per the brief's
   distinguishing-heading requirement.
6. i18n: new `Dashboard.analytics.pos.worstProducts` block (`title`/`empty`/
   `headers.{name,revenue,quantity,receipts,currentStock}`) in both `uk.json`/`en.json`, inserted
   directly after the sibling `topProducts` block, same key names reused where the concept matches
   (`name`/`revenue`/`quantity`/`receipts`) plus one new `currentStock` key.

## Files

Changed: `frontend/features/analytics/types.ts`, `frontend/features/analytics/api/pos-analytics.ts`,
`frontend/features/analytics/hooks/usePosAnalytics.ts`,
`frontend/app/(dashboard)/analytics/pos/page.tsx`, `frontend/messages/uk.json`,
`frontend/messages/en.json`.
New: `frontend/features/analytics/components/WorstProductsTable.tsx`.
Not touched: `LossesTrendChart.tsx`, write-offs section, `/analytics` (non-POS) page,
`CategoryDetailPanel.tsx`, `ProductTrendPanel.tsx`/`ProductAnalyticsTab.tsx` internals, any
backend file.

## Verification

- `npx tsc --noEmit` — 0 errors.
- `npm run lint` (`next lint`) — 0 warnings/errors.
- `npm run build` — exit 0 (confirmed explicitly, not just clean tail output), 57/57 static pages.
  `/analytics/pos` 6.93 kB / 259 kB First Load JS, reproduced identically across two consecutive
  build runs. Notably *smaller* route-specific size than TASK-488's logged 11.4 kB/259 kB figure
  for the same route — not a regression from this task's diff: the working tree is the shared,
  still-uncommitted stack for the whole TASK-488..495 batch, so intermediate churn from
  TASK-489/490/491/492's own edits (all landed in this same tree before this task started) is the
  more likely explanation for the shift than anything added here. Total First Load JS for the
  route (259 kB) is unchanged from TASK-488's own figure either way. Build log's repeated
  `ENVIRONMENT_FALLBACK` traces are the same pre-existing next-intl "no timeZone configured" noise
  already confirmed unrelated by 29+ prior task logs (per TASK-492's note).
- Live dev server (`frontend-dev` launch config, port 3000): same constraint every build agent in
  this batch has hit — Docker isn't running this session (`docker ps` fails to reach the daemon),
  so no backend/session is available. Confirmed directly (not assumed) that `/analytics/pos` is
  still edge-redirected by `middleware.ts`'s `PROTECTED` array (`["/dashboard", "/stock",
  "/products", "/analytics", "/provider"]`) before Next ever compiles the page module — navigated
  there live, landed on `/login` with zero hydration/module-resolution console errors (only the
  pre-existing `ENVIRONMENT_FALLBACK` noise and expected `ERR_CONNECTION_REFUSED` from the
  unreachable backend API), and confirmed via `preview_logs` search that no "analytics/pos" compile
  log line exists at all — the route genuinely never got compiled server-side this session, so this
  is not a disguised pass (same distinction TASK-492's log corrected TASK-488's precedent on).
  `npm run build`'s successful compile of the real `/analytics/pos` bundle (route table above) is
  the strongest signal available without a backend. Dev server stopped cleanly at end.

## Task log / current.md note

Wrote this log and prepended `current.md` after re-reading it fresh immediately before — TASK-492
was still the top entry, nothing else landed concurrently.
