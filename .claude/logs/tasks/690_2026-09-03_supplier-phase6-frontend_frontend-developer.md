# TASK-690 — Supplier portal expansion Phase 6 (frontend: 6a–6e)

**Status:** review (NOT committed) · **Agent:** frontend-developer · Plan `1-partitioned-book.md` Phase 6
**Base:** HEAD `877576c4` (all Phase 6 backend committed). Final agent of the program.

## 6a — "new order arrived" badge (#3)

- `api/supplier-cabinet-api.ts` — `getUnseenOrderCount()` → `{ count }`, `markOrdersSeen()` → 204.
- `hooks/useCabinetCooperation.ts` — `useUnseenOrderCount(enabled)` (key `["supplier","orders","unseen-count"]`,
  `refetchInterval: 60_000`), `useMarkOrdersSeen()` (invalidates that key).
- `components/layout/Sidebar.tsx` — mirrors the chat-badge wiring: `useUnseenOrderCount(isSupplierAdmin)`
  → `supplierUnseenOrders` → attached as `NavItem.badge` on `/supplier/orders` in the same `.map`.
- `app/(dashboard)/supplier/orders/page.tsx` — `useMarkOrdersSeen().mutate()` in a mount `useEffect`.

## 6b — supplier demand analytics (#7)

- `types.ts` — `SupplierAnalytics` + `SupplierAnalyticsItem` / `SupplierAnalyticsBuyer` /
  `SupplierAnalyticsTrendPoint` / `SupplierPeriodMetric`.
- `api/supplier-cabinet-api.ts` `getAnalytics(from?, to?)`; new `hooks/useSupplierAnalytics.ts`
  (`useSupplierAnalytics(from, to)`, key `["supplier","analytics",from,to]`).
- New `components/SupplierAnalyticsDashboard.tsx` — from/to date inputs + 30/90/365 presets;
  3 KPI cards (revenue / orders / units) each with a period-over-period delta via `TrendIndicator`
  + "проти попереднього періоду"; `components/SupplierRevenueTrendChart.tsx` (thin local Recharts
  AreaChart, own i18n namespace — retail `PosRevenueTrendChart` is too coupled to reuse); top-items
  / slow-items / by-buyer `Table`s.
- New `app/(dashboard)/supplier/analytics/page.tsx` — `SUPPLIER_ONLY` + `analytics_view` permission
  gate (mirrors `warehouses/page.tsx`; no `ModuleGate` — `marketplace_supplier` isn't a `ModuleKey`
  and is always-on for suppliers, backend action-gates the permission).
- `Sidebar.tsx` — `/supplier/analytics` nav item (`BarChart3`, `permission: "analytics_view"`, no moduleKey).

## 6c + 6d — "Моя ефективність" + composite quality score

- `features/marketplace/types.ts` — `compositeScore` + `onTimeDeliveryRate` on `SupplierMetricsDto`;
  `compositeScore` on `SupplierListItemDto`; `compositeScore` + `onTimeDeliveryRate` on
  `SupplierMetricsHistoryPoint`.
- `features/supplier-cabinet/types.ts` — `CabinetMetrics` += the two optional fields;
  `SupplierMetricsHistoryResponse { points, deltas }` + `SupplierMetricsHistoryDeltas`.
- `api` `getMetricsHistory(days)`; `hooks/useSupplierCabinet.ts` `useSupplierMetricsHistory(days)`
  (key `["supplier","metrics-history",days]`).
- New `components/SupplierPerformanceView.tsx` — 30/90/365 day selector; composite score header
  card **rendered on a 0–100 scale** (`Math.round(score*100)` + "зі 100"); one `MetricRow` per
  metric (composite, rating, on-time %, picking accuracy %, avg delivery days, response hours) with
  a direction-aware local `DeltaBadge` (green = improving, respects "lower is better" for delivery
  days / response time) + a trend chart reusing `features/marketplace/SupplierMetricTrendChart`.
  `< 2` history points → single "накопичується" note (KI-043 pattern), composite header stays.
- New `app/(dashboard)/supplier/performance/page.tsx` — `SUPPLIER_ONLY` + `client_reviews` gate.
- `Sidebar.tsx` — `/supplier/performance` nav item (`TrendingUp`, `permission: "client_reviews"`).
- Buyer side: `SupplierCard.tsx` — "Якість: 87" pill next to the star rating when `compositeScore != null`.
  `SupplierMetrics.tsx` — new "Композитний бал якості" (`87 / 100`) + "Доставки вчасно" tiles.
  `app/(dashboard)/marketplace/[id]/metrics/page.tsx` — `#composite` and `#ontime` `MetricSection`s
  (value + plain-language explanation + trend chart).

## 6e — category typeahead (#8)

- **Home decision:** `features/catalog/components/CategoryTypeahead.tsx` (not `components/ui/` — no
  `components/ui` file imports a feature hook / react-query, and `features/catalog` is the neutral
  shared catalog feature). Portal dropdown (createPortal + getBoundingClientRect + outside-click /
  scroll-resize dismissal, like `ActionMenu`), debounced 250ms, `value`/`onChange` as
  `{ id, name } | null`, clearable.
- `features/catalog/api/catalog.ts` `searchCategories(q, limit)`; `hooks/useCatalog.ts`
  `useCategorySearch(q, limit)` (`enabled: q.trim().length >= 2`, `placeholderData: keepPreviousData`).
  `features/catalog/types.ts` `CategorySearchResult`.
- `types.ts` — `CabinetItem` += `platformCategoryId` / `platformCategoryName`;
  `CabinetAddItemRequest` / `CabinetUpdateItemRequest` += `platformCategoryId?`.
- `components/CabinetItemModal.tsx` — "Категорія в каталозі" field; prefilled on edit; patch
  semantics on submit (unchanged → omit, cleared → all-zero guid sentinel, changed → new id).
- `components/CabinetItemsTable.tsx` — `platformCategoryName` column.
- **DEFERRED:** category filter on `WarehouseStockTable` (Phase-2 filters) — plan flagged it optional
  ("SKIP if it complicates"). Not done.

## i18n

`messages/{uk,en}.json` — **+63 keys each, parity 5775 == 5775** (structural parity verified). New
namespaces `Dashboard.supplierCabinet.analytics` / `.performance`, `Dashboard.ui.categoryTypeahead`;
additions to `sidebar.groups.supplierCabinet`, `supplierCabinet.pages` / `.itemModal` / `.itemsTable`,
`marketplace.card` / `.metrics` / `.metricsPage`. No new notification event types (none in the 688/689
backend logs).

## Verification

- `npx tsc --noEmit` — clean.
- `npx next lint` (touched dirs) — no warnings/errors.
- `npx next build` — success; `/supplier/analytics` + `/supplier/performance` compile (227 kB first-load,
  recharts, same tier as the retail analytics pages).
- i18n parity 5775 == 5775, structural parity OK.
- HEAD `877576c4` unchanged before + after.

## Blocking backend follow-up (not fixable here — "don't touch backend")

`GET /api/categories/search` (CategoriesController) is class-gated `[Authorize(Policy =
AppPolicies.CanViewStock)]`, and `supplier_admin` is **not** in `CanViewStockRoles` (AppPolicies.cs:122
— "supplier_admin is deliberately absent from every tenant-staff policy"). So the 6e typeahead in the
supplier cabinet item modal will 403 for `supplier_admin` until the endpoint is opened to that role
(add `AppRoles.SupplierAdmin` to `CanViewStockRoles`, or give `/search` its own policy incl.
supplier_admin). Frontend is fully wired and will work the moment that lands. (Dev `platform_categories`
is also still being built by the provider per memory, so there's no data to search yet either.)

## Not done / notes

- openapi.json regen — shared deferred debt (TASK-670..).
- No `mobile/` / backend / worker / retail `features/analytics`|`features/schedules` internals touched.
- NOT committed.
