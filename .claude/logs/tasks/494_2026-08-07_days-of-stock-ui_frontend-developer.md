# TASK-494: Days-of-stock-remaining UI

**Agent:** frontend-developer
**Date:** 2026-08-07
**Status:** done. No blockers.

## Scope

Consumed TASK-491's `daysOfStockRemaining: number | null` field on `CategoryProductRowDto`
(`GET /api/analytics/by-category/products`). Read `AnalyticsDtos.cs` fresh first and confirmed the
field's exact semantics myself rather than trusting the brief secondhand: `TotalQuantity /
ProductAdu.AduEffective`, rounded to 1 decimal, null in two cases the UI must not try to tell
apart — no `store_id` on the request, or the product has no `ProductAdu` row / `AduEffective` is
null-or-0 (a real "no usage history" state, not an error). Read `types.ts` fresh first and
confirmed TASK-493's `WorstProductsDto`/`WorstProductRowDto` were already there — appended after
them, untouched.

1. `frontend/features/analytics/types.ts` — added `daysOfStockRemaining: number | null` to the
   existing `CategoryProductRowDto` interface (field addition, not a new type), extended its doc
   comment with the two-null-case explanation. Appended a new `AduDto` interface after
   `WorstProductsDto`, field-for-field mirror of backend `AduDtos.cs`.
2. `frontend/features/analytics/components/CategoryDetailPanel.tsx` — new sortable "Днів запасу" /
   "Days of stock" column, **not** gated on `canViewMargin` (operational data, not margin/cost —
   always the last column in both the margin and non-margin grid-template variants, +100px in
   each). Renders "—" for null; otherwise `{days} дн.`/`{days}d` via a new `daysOfStockValue` i18n
   key. New local `daysOfStockColor()` helper (red `<7`, amber `<30`, green `>=30`, gray null) —
   reuses the exact hex triple (`#F87171`/`#FBBF24`/`#4ADE80`) this table's own
   safe/warning/critical status cells already use, so the same product reads with the same urgency
   tone as the expiry columns. `compareRows`/sort logic needed no changes — already generic over
   `number | null` fields (same pattern as marginAmount/marginPercent).
3. `frontend/features/inventory/components/ProductAnalyticsTab.tsx` — new optional
   `daysOfStockRemaining?: number | null` prop, purely presentational (component still never
   fetches ADU/stock itself, same posture as the existing `canViewMargin` prop). Renders one more
   `SummaryCard` (icon: `Clock`) in the existing summary-card row, right after the `currentStock`
   card, **only when the prop is not `undefined`** — `undefined` (omitted) → no card at all;
   explicit `null` → card renders showing "—"; a number → card renders color-coded. This
   "absent vs. empty" distinction mirrors `CategoryProductRowDto.daysOfStockRemaining`'s own
   null-semantics. Local `daysOfStockColor()` copy (not shared/imported) — same "no shared
   chart/table formatting module today" reason this file's existing `marginColor` is already a
   local duplicate of `CategoryDetailPanel.tsx`'s version, not an import.
4. `frontend/features/analytics/components/ProductTrendPanel.tsx` — now actually consumes the
   `storeId?` prop it already accepted (TASK-488 added the prop for shape parity but never wired
   it to anything). When `storeId` is a concrete value: fetches ADU via the new `useAdu` hook,
   fetches on-hand stock for that (product, store) via the existing `stockApi.getAll({ store_id,
   product_id })` (`frontend/features/shelf/api/stock.ts` — already supports both filters, no
   backend change needed), sums `quantity` across returned batches excluding `sold_out`/`archived`
   statuses (same on-hand definition `AnalyticsRepository.cs` uses in `GetByCategoryAsync`/
   `GetWorstProductsAsync`), divides by `aduEffective`, rounds to 1 decimal (matches the backend's
   own `Math.Round(x, 1)`), and passes the result to `ProductAnalyticsTab`. When `storeId` is
   `undefined` (today: always true on `/analytics`, which has no page-wide store filter;
   `/analytics/pos` does pass a concrete `storeId`) — no fetch happens at all, prop isn't passed,
   no card renders.
   - New `frontend/features/analytics/api/adu.ts` + `frontend/features/analytics/hooks/useAdu.ts`
     — no prior frontend consumption of `GET /api/adu/{storeId}/{productId}` existed anywhere in
     the app (grepped first; only the unrelated bulk `POST /api/adu/recalculate` action in
     `features/orders` was consumed). New minimal hook, same `{queryKey, queryFn, enabled}` shape
     as every other hook in this feature. `retry` skips a 404 (no `ProductAdu` row yet — an
     expected, common state) exactly like `features/pos/hooks/usePos.ts`'s `useCurrentShift`
     precedent for the same reason.
   - Used `stockApi` directly via a local `useQuery` in `ProductTrendPanel.tsx` rather than the
     shelf feature's existing `useStock` hook wrapper — that hook has no `enabled` param (every
     other call site always wants it enabled), and adding one would touch a shared hook outside
     this task's 4-file scope for a one-off need. `stockApi.getAll` itself (the actual fetcher) is
     reused as-is.
   - A settled ADU error (404 or otherwise) and a successful-but-zero/null `aduEffective` both
     collapse to the same `null` result — this component doesn't need to (and per TASK-491's own
     doc comment, shouldn't) tell them apart. While either query is still resolving, the computed
     value stays `undefined` (no card yet) rather than flashing "—" then a number.
5. i18n — `Dashboard.analytics.categoryDetailPanel.headers.daysOfStockRemaining` +
   `.daysOfStockValue` (uk/en), `Dashboard.inventory.analytics.daysOfStockRemaining` +
   `.daysOfStockValue` (uk/en, next to the existing `currentStock` key). Value-format strings
   follow this app's existing "{days} дн." / "{days}d" convention (same pattern already used
   elsewhere, e.g. `daysLeftValue`), duplicated per-namespace rather than shared cross-namespace —
   matches this file's own existing precedent (e.g. `closeButton`/`empty` are already duplicated
   verbatim across `categoryDetailPanel` and `lossesProductBreakdownPanel`).

## Known limitation (not a bug — flagged per the brief, for traceability)

`/analytics` (where `CategoryDetailPanel` lives) has no page-wide store filter at all (confirmed
by TASK-488, re-confirmed here) — every request from that page omits `store_id`, so
`daysOfStockRemaining` will render as "—" for every row in `CategoryDetailPanel` in practice today.
The column itself is correct and ready for whenever `/analytics` gets a store filter, or for a
future single-store tenant. Did **not** add a store filter to `/analytics` — explicitly out of
scope. `ProductTrendPanel`'s card is unaffected by this limitation on `/analytics/pos`, which
already has a store filter and passes it through — the card there resolves to a real number
whenever ADU data exists for that store/product.

## Files

Changed: `frontend/features/analytics/types.ts`,
`frontend/features/analytics/components/CategoryDetailPanel.tsx`,
`frontend/features/inventory/components/ProductAnalyticsTab.tsx`,
`frontend/features/analytics/components/ProductTrendPanel.tsx`, `frontend/messages/uk.json`,
`frontend/messages/en.json`.
New: `frontend/features/analytics/api/adu.ts`, `frontend/features/analytics/hooks/useAdu.ts`.
Not touched: `WorstProductsTable.tsx`, `LossesTrendChart.tsx`, `analytics/pos/page.tsx`,
`analytics/page.tsx`, `features/shelf/hooks/useStock.ts`, any backend file.

## Verification

- `npx tsc --noEmit` — 0 errors.
- `npm run lint` (`next lint`) — 0 warnings/errors.
- `npm run build` — exit 0, 57/57 static pages. `/analytics` 8.51 kB/270 kB, `/analytics/pos`
  5.39 kB/261 kB First Load JS. Both routes' First Load JS ticked up ~2 kB (both by the same
  amount, since both import the same extended `ProductTrendPanel`), consistent with the new
  code added; the per-route "Size" column moved in the opposite direction from prior batch
  figures for both routes, which is the same non-monotonic churn TASK-493's log already flagged
  and attributed to the shared, still-uncommitted working tree for this whole TASK-488..495 batch
  rather than a regression from this task's own diff. `ENVIRONMENT_FALLBACK` traces in the build
  log are the same pre-existing next-intl noise every prior log in this batch already confirmed
  unrelated.
- Live dev server (`frontend-dev` launch config, port 3000): same constraint every build agent in
  this batch has hit — confirmed directly that Docker's daemon isn't reachable this session
  (`docker ps` fails to connect), so no backend/session is available. Navigated to `/uk/analytics`
  live: redirected to `/login` cleanly (middleware edge-redirect, matches TASK-492's documented
  finding), zero hydration/module-resolution console errors from any of this task's new code
  (`useAdu`, `aduApi`, the cross-feature `stockApi` import, the `Clock` icon) — only the
  pre-existing `ENVIRONMENT_FALLBACK` noise and expected `ERR_CONNECTION_REFUSED` from the
  unreachable backend API. `npm run build`'s successful compile of the real bundles (route table
  above) remains the strongest signal available without a backend, same conclusion every prior
  task in this batch reached. Dev server stopped cleanly at end.

## Task log / current.md note

Wrote this log and prepended `current.md` after re-reading it fresh immediately before — TASK-493
was still the top entry, nothing else landed concurrently.
