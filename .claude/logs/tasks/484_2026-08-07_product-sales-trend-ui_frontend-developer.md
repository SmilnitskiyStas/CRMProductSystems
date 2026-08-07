# TASK-484: POS product sales-trend UI (interactive analytics + margin plan)

**Agent:** frontend-developer
**Date:** 2026-08-07
**Status:** done. No blockers.

## Scope

Per `iterative-purring-sifakis.md` plan (TASK-484, after TASK-482's backend endpoint). Row-click
drill-down from `PosTopProductsTable` on `/analytics/pos`, rendered inline via an extended
`ProductAnalyticsTab.tsx` — not a route navigation, `/inventory/{id}?tab=analytics` untouched.

1. `frontend/features/analytics/types.ts` — `ProductSalesTrendDto`/`ProductSalesTrendPointDto`,
   verified against the actual shipped `PosAnalyticsDtos.cs`/`AnalyticsController.cs` (not the
   brief's sketch) before typing.
2. `frontend/features/analytics/api/pos-analytics.ts` — `getProductSalesTrend(productId, params)`,
   no compare-mode variant (matches the endpoint).
3. `frontend/features/analytics/hooks/usePosAnalytics.ts` — `useProductSalesTrend`, full
   `[productId, params]` in the query key, no `keepPreviousData`.
4. `frontend/features/inventory/components/ProductAnalyticsTab.tsx` — new optional
   `showRevenueSeries?`/`canViewMargin?` props (default `false`/`undefined`, both existing call
   sites — `ProductsTable.tsx`, `inventory/[id]/page.tsx` — pass neither, unaffected). When set,
   fetches the trend at a fixed `group_by: "day"` (independent of the tab's own `rangeDays`) and
   merges points into the existing movement-derived `chartData` by date string: revenue/quantity
   sold zero-fill on no-sales days, margin stays `null` on a real sales day with unknown cost
   (server ADR-027 ambiguity) — only zero-fills on no-sales days. Second right-hand `YAxis`
   (`yAxisId="revenue"`) added; gave the pre-existing `YAxis` + 5 `Line`s + 4 `ReferenceArea`s + 3
   `ReferenceLine`s an explicit `yAxisId="quantity"` (all previously relied on recharts' implicit
   default axis, which breaks silently — not with an error — the moment a second axis exists).
   Legend/line-rendering both derive `yAxisId` from `buildLines()`'s per-entry field rather than a
   key-name branch, so quantity- and currency-scale series can't cross-wire. Margin legend/line/
   tooltip row entirely absent (not grayed) when `canViewMargin` is false. Tooltip gained
   dedicated revenue (₴, gold) and margin (₴, green/red/gray by sign, em-dash on null) rows.
   Optional revenue-total `SummaryCard` (Wallet icon) added alongside the existing 4, per the
   brief's "reasonable if it fits" allowance — no margin-total card added (brief only suggested
   revenue, kept to that).
5. New `frontend/features/analytics/components/PosProductTrendPanel.tsx` — thin wrapper. Resolves
   `canViewMargin` via `useMe()` + `canViewAnalyticsMargin`, the exact mechanism
   `CategoryDetailPanel.tsx` (TASK-483) already uses. Header chrome matches `PosDayDetailPanel.tsx`
   (TASK-485), extended to a 2-line title+disclaimer block (CategoryDetailPanel's own layout) since
   this panel — unlike PosDayDetailPanel — needs to show the "оцінна маржа" disclaimer when margin
   is visible.
6. `PosTopProductsTable.tsx` — `onRowClick?`/`selectedProductId?`. Active-row highlight reuses the
   table's own existing hover color (`#111827`) as a persistent "selected" background rather than
   introducing a new color.
7. `analytics/pos/page.tsx` (read fresh — built alongside TASK-485's already-merged `selectedDay`/
   `PosDayDetailPanel`, nothing reverted) — `selectedProduct` state, toggle-on-reselect handler
   (same convention as `handleDayClick`), `PosProductTrendPanel` rendered below the top-products/
   cashiers section.
8. i18n: `Dashboard.inventory.analytics.series.revenue`/`.margin`, new
   `Dashboard.analytics.pos.productTrendPanel.*` (`title`, `closeButton`, `marginDisclaimer`) in
   both `uk.json`/`en.json`.

## Noted decision

`PosProductTrendPanel` accepts `storeId?` (prop-shape parity with `PosDayDetailPanel`, and the
page passes its live `storeId` down) but deliberately does **not** thread it into
`ProductAnalyticsTab`'s trend fetch: `useProductMovements` (the tab's existing stock/movement
series) has no `store_id` filter at all — it's whole-tenant by design, same scope as the existing
`/inventory/{id}?tab=analytics` view. A store-scoped revenue line next to a store-agnostic stock
line would misrepresent the chart, so product-level trend here stays all-stores regardless of the
page's store filter. Matches the brief's literal example render call
(`<ProductAnalyticsTab productId={productId} showRevenueSeries canViewMargin={canViewMargin} />`,
no `storeId`).

## Verification

- `npx tsc --noEmit` — 0 errors.
- `npm run lint` (`next lint`) — 0 warnings/errors.
- `npm run build` — exit 0, 57/57 static pages, `/analytics/pos` 11.3 kB / 259 kB First Load JS.
  (Build output has repeated `ENVIRONMENT_FALLBACK` stack traces during static-page generation —
  pre-existing Next.js/next-intl static-export noise unrelated to any file touched here; same page
  count as before, no route failed, exit code 0.)
- Live browser: started the dev server, loaded `/analytics/pos` and `/analytics`. No backend
  process available in this session (all API calls `ERR_CONNECTION_REFUSED` to `localhost:5000`,
  same constraint TASK-483/485 both hit and documented) — full authenticated click-through
  (row click → panel opens, second click → toggles closed, margin present/absent by role) not
  possible here. Confirmed no React/hydration/chunk-load errors from the new code — console only
  showed the expected connection-refused network errors and routine Fast Refresh log lines. Full
  E2E deferred to TASK-486 (qa-tester), same precedent as TASK-483/485.

## Files

Changed: `frontend/features/analytics/types.ts`, `frontend/features/analytics/api/pos-analytics.ts`,
`frontend/features/analytics/hooks/usePosAnalytics.ts`,
`frontend/features/inventory/components/ProductAnalyticsTab.tsx`,
`frontend/features/analytics/components/PosTopProductsTable.tsx`,
`frontend/app/(dashboard)/analytics/pos/page.tsx`, `frontend/messages/uk.json`,
`frontend/messages/en.json`.
New: `frontend/features/analytics/components/PosProductTrendPanel.tsx`.

Untouched (explicitly out of scope): `CategoryDetailPanel.tsx`,
`LossesProductBreakdownPanel.tsx`, `CategoryStatusChart.tsx`, `LossesByReasonChart.tsx`,
`LossesByStoreChart.tsx`, `analytics/page.tsx` (TASK-483); `PosDayDetailPanel.tsx`,
`PosRevenueTrendChart.tsx`, `ExpiryDonut.tsx` (TASK-485); all backend files; both existing
`/inventory/{id}?tab=analytics` call sites (`ProductsTable.tsx`, `inventory/[id]/page.tsx` —
neither passes the new props, behavior unchanged).
