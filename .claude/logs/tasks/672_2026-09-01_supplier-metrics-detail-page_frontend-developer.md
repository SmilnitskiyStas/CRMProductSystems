# TASK-672: buyer-facing supplier-metrics detail page + trend charts + tiles as deep-links

**Agent:** frontend-developer · **Date:** 2026-09-01 · **Status:** done (committed to main, not pushed)
Builds on TASK-671 (`7f43a496` — `GET /api/marketplace/suppliers/{id}/metrics-history`). Frontend only.

## Done

### Data layer
- `features/marketplace/types.ts` — `SupplierMetricsHistoryPoint` (`date`, `rating`, `avgDeliveryDays`,
  `orderAccuracy`, `qualityScore`, `cancellationRate`, `responseTimeHours`, `deliverySampleSize`,
  `responseSampleSize`; all metrics `number | null`).
- `features/marketplace/api/marketplace-api.ts` — `getSupplierMetricsHistory(supplierId, days = 90)`
  → `GET /api/marketplace/suppliers/{id}/metrics-history?days=`.
- `features/marketplace/hooks/useMarketplace.ts` — `useSupplierMetricsHistory(supplierId | null, days = 90)`,
  key `["marketplace","metrics-history", supplierId, days]` (added to `MARKETPLACE_KEYS.metricsHistory`),
  `enabled: !!supplierId`, `staleTime 60s`.

### New route `app/(dashboard)/marketplace/[id]/metrics/page.tsx`
- Header: back link `‹ до профілю` → `/marketplace/{id}`, title `Показники постачальника`, supplier name
  (`useSupplier(id)`), `оновлено {aggregatesComputedAt}` / `ще не розраховано`.
- One `<section id>` per metric — anchors: `rating`, `delivery`, `accuracy`, `quality`, `response`,
  `cancellation`, `coverage`. Each: current big value from `supplier.metrics` + "на основі N" (delivery,
  response) + keyed plain-language explanation + a trend chart from `useSupplierMetricsHistory`.
  `orderAccuracy` / `cancellationRate` charts plot `value * 100` as %.
- Delivery section extra: `<DeliveryRegionComparison>` (new — declared-vs-actual two-column per measured
  region) when `deliveryCoverage.served` is non-empty, else falls back to the existing
  `<DeliveryByRegionPanel>`.
- Coverage section: reuses `<SupplierCoveragePanel coverage={supplier.deliveryCoverage} />`.
- Loading skeleton / error+not-found (reuses `supplierPage.errorLoad` + back-to-marketplace), consistent
  with the profile page. A `useEffect` re-runs `scrollIntoView` for the URL hash once `supplier` resolves
  (the target only exists post-fetch, so the browser's own on-load hash scroll misses it).

### New components
- `components/SupplierMetricTrendChart.tsx` — `{ points: {date,value|null}[]; unit: "day"|"hour"|"percent"|"star"|"score"; label; color? }`.
  Recharts `AreaChart`/`ResponsiveContainer` height 200, dark theme copied from
  `analytics/components/LossesTrendChart.tsx` (axis/tooltip/grid styling, short-date X). `connectNulls`
  OFF → null days render as gaps. `< 2` real points → muted empty state (`metricsPage.chartEmpty`).
  `star` locks Y to `[0,5]`; everything else (incl. percent) auto-fits.
- `components/DeliveryRegionComparison.tsx` — rows from measured `deliveryByRegion` (fastest first);
  each shows declared terms (`formatDeliveryTerms`, `Dashboard.geo.deliveryTerms`) + measured
  `{days} дн. n={N}` (reuses `deliveryByRegion.days`/`.sample`).

### Profile-page summary `components/SupplierMetrics.tsx`
- New `supplierId: string` prop (page passes `supplierId={id}`). Each tile is now a `next/link`
  `<Link href={`/marketplace/${supplierId}/metrics#${anchor}`}>` (rating→#rating, avgDeliveryDays→#delivery,
  orderAccuracy→#accuracy, qualityScore→#quality, responseTimeHours→#response,
  cancellationRate→#cancellation) with a hover border-highlight (`SupplierCard` pattern).
- Removed the inline `regionsOpen` `useState` + "детальніше по регіонах" toggle + inline
  `<DeliveryByRegionPanel>` — that content is now on the detail page. "на основі N" sublabels kept.
- Added a "Детальніше про показники →" link under the grid → `/marketplace/{supplierId}/metrics`.

### i18n (both `messages/uk.json` + `en.json`) — new `Dashboard.marketplace.metricsPage.*`, 16 keys ea.
`title, backToProfile, updatedAt, notComputed, byRegionTitle, declared, actual, chartEmpty, detailsLink,
explainRating, explainDelivery, explainAccuracy, explainQuality, explainResponse, explainCancellation,
explainCoverage`.
The 6 metric **section titles** reuse the existing `Dashboard.marketplace.metrics.*` (identical strings —
"reuse where they fit"); the coverage section uses `<SupplierCoveragePanel>`'s own heading. No parallel
title keys duplicated into the new namespace.

## Verification

- `npx tsc --noEmit` — clean · `npx next lint` — clean · `npx vitest run` — 59 passed / 8 files
- uk/en deep-key parity (node): **4652 == 4652**, key sets identical, 0 drift
- `npx next build` — exit 0; route compiled: `ƒ /marketplace/[id]/metrics  8.94 kB  233 kB`
- **Live** (frontend dev :3001 → backend :5080 → dev DB :5435, buyer `ea@demo.local` / «Свіжий Кут»,
  supplier `b4e21658` seeded with 60 daily snapshot rows + fuller `supplier_metrics` incl.
  `DeliveryByRegion` — seed reverted afterward):
  - profile tiles clickable, hover highlight; tile click → `/marketplace/{id}/metrics#delivery` and the
    delivery section scrolls into view; "Детальніше про показники →" → `/metrics`; "‹ до профілю" back link works
  - all 7 sections render: current value + explanation + chart; rating chart 0–5, accuracy/cancellation %
    auto-domain, delivery/response day/hour
  - quality section → empty state (history all-null)
  - delivery "За регіонами": Житомирська `Заявлено 1–3 d. · from 5000 UAH` / `Фактично 1.8 d. n=25`,
    Київська/Львівська `Заявлено —` / measured
  - coverage section → `<SupplierCoveragePanel>`
  - screenshot captured

## Commit
`feat(marketplace): supplier metrics detail page with trend charts + tiles as deep-links (TASK-672)` — not pushed.
