# TASK-591 — Events calendar: product linking + sales comparison (Wave 2)

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-21

Wave 2 on top of TASK-589's day-detail drawer shell. `EventDetailPanel.tsx` gains two new
`DrawerSection`s: **Linked Products** (product-scoped `DemandEventCoefficient` rows — search,
add, inline-edit coefficient, remove) and **Sales Comparison** (per linked product, a compact
revenue trend card comparing the event's date window vs. the server's auto baseline period).
Frontend-only — both backend endpoints (`DELETE .../coefficients/{coefId}` from TASK-588,
`?compare=true` on the product trend endpoint from TASK-590) were already merged and verified.

**Files created:**
- `frontend/features/events/components/EventProductPicker.tsx` — debounced (~300ms) search
  box over `/api/items` via `useProductSearch`, results excluding `excludeIds`, click → `onPick`.
  Sourced from the inventory catalog, not `consumer-app`/`catalog` (different bounded context).
- `frontend/features/events/components/LinkedProductSalesCard.tsx` — one card per linked
  product: `resolveEventWindowForYear(event, referenceDateIso)` → `useProductSalesTrendCompare`
  (compareFrom/compareTo omitted, server auto-baseline) → `TrendIndicator` (revenue,
  current vs. comparison) + a small Recharts area/line chart (height 90), points aligned by
  day-offset from `data.from`/`data.compareFrom` (mirrors `PosRevenueTrendChart.tsx`'s
  `daysBetween`/`byOffset`, adapted to the new DTO's field names). Own component instance per
  product so each can call the hook independently without breaking rules of hooks. Loading/
  error/all-zero-sales states handled explicitly; unique SVG gradient id per `productId`.

**Files changed:**
- `frontend/features/events/api/events.ts` — `removeCoefficient(eventId, coefId)` →
  `DELETE /api/events/{id}/coefficients/{coefId}`.
- `frontend/features/events/hooks/useEvents.ts` — `useRemoveCoefficient()`, same
  mutate+invalidate pattern as the existing coefficient hooks.
- `frontend/features/inventory/api/products.ts` — `getAll` takes an optional
  `{ search?, ids?, pageSize? }` (unchanged no-arg call still works).
- `frontend/features/inventory/hooks/useProducts.ts` — added `useProductsByIds(ids)`,
  `useProductSearch(search, enabled)`. **Had to fix `useProducts()`**: it previously passed
  `productsApi.getAll` directly as `queryFn` (relying on the old zero-arg signature); once
  `getAll` gained an optional-params signature, React Query's own call context doesn't
  structurally match, and `tsc` correctly flagged it (surfaced as `unknown`-typed `products` two
  call sites away, in `inventory/page.tsx` and `sales/page.tsx`) — changed to
  `queryFn: () => productsApi.getAll()`. No behavior change, just restores type inference.
- `frontend/features/analytics/types.ts` — `ProductSalesTrendCompareDto`; updated the stale
  "no compare-mode variant" comment above `ProductSalesTrendDto`.
- `frontend/features/analytics/api/pos-analytics.ts` — `getProductSalesTrend` is now an
  overloaded function (compare `false`/omitted → `ProductSalesTrendDto`, `true` →
  `ProductSalesTrendCompareDto`), same split as the existing `getRevenueTrend`; moved above
  `posAnalyticsApi` to match that function's placement convention.
- `frontend/features/analytics/hooks/usePosAnalytics.ts` — `useProductSalesTrendCompare`.
- `frontend/features/events/components/EventDetailPanel.tsx` — new `referenceDateIso: string`
  prop; product-scoped coefficients resolved once via `useProductsByIds`, shared by both new
  sections. Inline coefficient edit mirrors `EventForm.tsx`'s `CoefficientEditor` (number input,
  `onBlur` → `useUpdateCoefficient`); remove button → `useRemoveCoefficient`; picker add →
  `useAddCoefficient` with a default coefficient of 1.5. Toasts (success/error) on all three
  mutations via `sonner`, consistent with `CoefficientEditor`'s own toast usage.
- `frontend/features/events/components/EventDayDetailDrawer.tsx` — passes
  `referenceDateIso={dateIso}` to `EventDetailPanel` (was already in scope, one-line addition).
- `frontend/messages/en.json`, `frontend/messages/uk.json` — 19 new keys under
  `Dashboard.events.dayDetail` (section titles, empty states, picker copy, toasts, chart/trend
  labels).

**Verification:** `npx tsc --noEmit` clean (after the `useProducts()` fix above), `npx eslint`
clean on all 11 touched/created files, both `messages/{en,uk}.json` parse as valid JSON. No dev
server running and no authenticated browser session existed — live click-through was skipped
per the task boundary (did not attempt login), as expected in this environment.

**Not done:** none — both Wave 2 sections are complete; nothing deferred.

## Note on task numbering
Originally logged as TASK-590 by the agent (picked before checking that TASK-590 had just been
claimed minutes earlier by the merged worktree backend task — product-sales-trend-comparison).
Renumbered to TASK-591 when reconciling; only this log file and its own `current.md` section
header were self-referential and needed correction. The four in-code comments this task added
that say "TASK-590" (in `analytics/types.ts`, `pos-analytics.ts`, `usePosAnalytics.ts`) correctly
refer to the *backend* TASK-590 compare-mode endpoint and were left unchanged.
