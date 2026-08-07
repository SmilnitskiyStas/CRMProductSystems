# TASK-488: Category/losses product drill-down → shared ProductTrendPanel

**Agent:** frontend-developer
**Date:** 2026-08-07
**Status:** done. No blockers.

## Scope

User-flagged gap after live review of TASK-479..487: on `/analytics`, product rows inside
`CategoryDetailPanel` and `LossesProductBreakdownPanel` (TASK-483) had no click handler — a dead
end — while `PosTopProductsTable` on `/analytics/pos` (TASK-484) already opens a full per-product
trend panel on row click. Closed the gap by reusing that exact panel from both new locations.

1. Read `PosProductTrendPanel.tsx` (now `ProductTrendPanel.tsx`) fully first — confirmed genuinely
   generic (`productId`/`productName`/`storeId?`/`onClose`, no POS-specific coupling; it just
   resolves `canViewMargin` and renders `ProductAnalyticsTab`). Reused unmodified, only renamed —
   no logic changes needed.
2. `frontend/features/analytics/components/CategoryDetailPanel.tsx` — new
   `onProductClick?: (productId: string, productName: string) => void` prop. Product-name cell
   becomes a `<button>` when the prop is provided (plain `<div>` otherwise, unchanged). New
   `productNameButton` style: background-chip hover (`#111827`, the same accent
   `PosTopProductsTable`'s row hover/active-row highlight already uses in this feature), not styled
   like `SortableHeader`'s uppercase/gray sort buttons in the header row above it, and scoped to
   just the name cell (not the whole grid row — the row's other cells are status/margin figures,
   not separate nav targets).
3. `frontend/features/analytics/components/LossesProductBreakdownPanel.tsx` — identical treatment
   (same `onProductClick?` prop, same `productNameButton` style, shared by both its callers —
   losses-by-store and losses-by-reason).
4. `frontend/app/(dashboard)/analytics/page.tsx` (read fresh) — new
   `selectedProduct: {id, name} | null` state + `handleProductClick`, exact toggle-on-reselect
   pattern as `analytics/pos/page.tsx`'s own `selectedProduct`/`handleProductClick` (TASK-484).
   Wired `onProductClick={handleProductClick}` into all three existing call sites (by-category,
   losses-by-reason, losses-by-store). `ProductTrendPanel` rendered once, in a single shared spot
   at the bottom of the page — deliberately not nested under each of the three trigger panels: they
   share one piece of state, so nesting under any single trigger would make the trend panel vanish
   when that specific panel closes (even though it has its own close button) and could re-surface a
   stale product if that trigger is reopened later with different data selected. No `storeId`
   threaded through: verified `/analytics` has no page-wide store filter at all (every hook on the
   page — `useExpirySummary`, `useWriteOffAnalytics`, `useZoneAnalytics`, `useCategoryAnalytics`,
   `useLosses` — takes no `store_id`; the per-row store links in the expiry/zone tables navigate to
   `/stock`, they aren't a page filter), unlike `/analytics/pos` which has a real store `<select>`.
5. **Renamed** `PosProductTrendPanel.tsx` → `ProductTrendPanel.tsx` (`git mv`, function renamed to
   match) since it's no longer POS-page-only after this task. Updated its one existing import/JSX
   site (`analytics/pos/page.tsx`) and the new one (`analytics/page.tsx`). Also fixed the two now-
   stale name references this rename left in comments (not logic) in
   `ProductAnalyticsTab.tsx` (its `showRevenueSeries`/`canViewMargin` prop docs, which name the
   caller) and `PosTopProductsTable.tsx` (its `onRowClick` prop doc) — both explicitly permitted by
   the brief as part of "the rename," nothing else in either file touched. Translation namespace
   (`Dashboard.analytics.pos.productTrendPanel`) deliberately left as-is — out of scope, not part of
   a pure rename, and the panel's own internals are otherwise untouched per the brief.

## Files

Changed: `frontend/features/analytics/components/CategoryDetailPanel.tsx`,
`frontend/features/analytics/components/LossesProductBreakdownPanel.tsx`,
`frontend/features/analytics/components/PosTopProductsTable.tsx` (comment only),
`frontend/features/inventory/components/ProductAnalyticsTab.tsx` (comments only),
`frontend/app/(dashboard)/analytics/page.tsx`, `frontend/app/(dashboard)/analytics/pos/page.tsx`
(import/JSX name update only).
Renamed (`git mv`, content updated): `frontend/features/analytics/components/
PosProductTrendPanel.tsx` → `ProductTrendPanel.tsx`.
No backend files, no `CategoryStatusChart.tsx`/`LossesByReasonChart.tsx`/`LossesByStoreChart.tsx`/
`ExpiryDonut.tsx`/`PosRevenueTrendChart.tsx`, no i18n files touched (no new visible text — the
click affordance is purely a hover-style change on existing text).

## Verification

- `npx tsc --noEmit` — 0 errors.
- `npm run lint` (`next lint`) — 0 warnings/errors.
- `npm run build` — exit 0, 57/57 static pages. `/analytics` 9.27 kB / 263 kB First Load JS (up
  from TASK-483's 8.87 kB / 247 kB — expected, now pulls in `ProductAnalyticsTab`/recharts via
  `ProductTrendPanel`, previously only loaded from `/analytics/pos`). `/analytics/pos` 11.4 kB /
  259 kB (was 11.3 kB / 259 kB — negligible, just the import path rename). Build output has the
  same repeated `ENVIRONMENT_FALLBACK` stack traces during static-page generation TASK-483/484
  both already logged as pre-existing `next-intl` "no timeZone configured" noise, confirmed again
  here by inspecting the dev-server logs directly (`IntlError: ENVIRONMENT_FALLBACK: There is no
  timeZone configured…`) — unrelated to any file touched in this task, not a new regression.
- Live dev server (`preview_start` on the project's own `frontend-dev` launch config): loaded
  `/uk/analytics` and `/uk/analytics/pos`. No backend process in this session (`ERR_CONNECTION_
  REFUSED` to `localhost:5000`, same constraint TASK-483/484/485 all hit and documented) — both
  pages correctly redirected to `/login` client-side, confirming no compile/hydration/module-
  resolution errors from either the renamed import or the new code (webpack compiled both route
  chunks cleanly; only console errors were the expected connection-refused network failures).
  Full authenticated click-through (product row → panel opens → toggle closes) not possible without
  a backend — same limitation the whole initiative's build tasks hit; genuinely low-risk here since
  `ProductTrendPanel` itself was already live E2E-verified end-to-end by TASK-486 and is reused
  completely unmodified, and the only new interactive surface (the two product-name buttons) is a
  straightforward conditional render + onClick, not new data-fetching or business logic.

## Naming decision (per orchestrator's request, relevant to future TASK-493)

**Renamed** `PosProductTrendPanel` → `ProductTrendPanel`. Any future import (e.g. TASK-493) should
use `frontend/features/analytics/components/ProductTrendPanel.tsx`, named export `ProductTrendPanel`.
