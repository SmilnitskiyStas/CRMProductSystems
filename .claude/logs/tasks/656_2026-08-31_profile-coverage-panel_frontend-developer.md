# TASK-656 (T9) — Supplier profile coverage panel + per-region delivery drill-down

**Status:** done · **Agent:** frontend-developer · **Model:** sonnet
**Branch:** main (main working tree) · **Depends on:** T3 (TASK-650, on main), T7 (TASK-654, on main)

## Scope

Frontend only. Surface the TASK-650 read-side data on the marketplace supplier
profile: always-visible delivery-coverage panel (NOT premium-gated) + a per-region
delivery-time drill-down under the metrics grid + sample-size sublabels.

## Files created

| File | Contents |
|---|---|
| `frontend/features/marketplace/components/SupplierCoveragePanel.tsx` | `{ coverage: DeliveryCoverage \| null \| undefined }`. "Регіони доставки" heading; served regions with `terms` (or "за домовленістю" when null); muted "Не доставляє: …"; note. Empty/null → muted "Постачальник не вказав регіони доставки". `useTranslations("Dashboard.marketplace.coverage")`, `useRegionLabel()`. |
| `frontend/features/marketplace/components/DeliveryByRegionPanel.tsx` | `{ stats: RegionDeliveryStat[] \| null \| undefined }`. Compact list `regionLabel · {n} дн. · faint n={sampleSize}`, sorted by `avgDeliveryDays` asc. Empty/null → muted "Ще недостатньо даних по регіонах". `useTranslations("Dashboard.marketplace.deliveryByRegion")`. |

## Files changed

- `frontend/features/marketplace/types.ts` — import + re-export geo `DeliveryCoverage` /
  `DeliveryCoverageEntry` (shape matches backend exactly, reused not duplicated); new
  `RegionDeliveryStat`; `SupplierMetricsDto` += `deliveryByRegion?` / `deliverySampleSize?` /
  `responseSampleSize?` / `aggregatesComputedAt?`; `SupplierProfileDto` += `deliveryCoverage?`.
  Additions appended at interface ends (minimal diff — TASK-655 concurrently edits filter/update
  types here; no overlap).
- `frontend/features/marketplace/components/SupplierMetrics.tsx` — `MetricItem` gains
  `sublabel` / `footer` slots. `avgDeliveryDays` tile: "на основі N замовлень" sublabel when
  `deliverySampleSize != null`; "детальніше по регіонах" / "приховати регіони" toggle
  (`useState`) → expands `<DeliveryByRegionPanel>` inline below the grid. Toggle shown only when
  `deliverySampleSize != null` or a region breakdown exists. `responseTimeHours` tile:
  "на основі N звернень" sublabel when `responseSampleSize != null`; value → "недостатньо даних"
  when `responseTimeHours == null`. Other 4 tiles unchanged.
- `frontend/app/(dashboard)/marketplace/[id]/page.tsx` — removed the premium-only
  `deliveryRegions` chip block; `<SupplierCoveragePanel coverage={supplier.deliveryCoverage} />`
  rendered outside the premium gate, just above the metrics section. website/workingHours/
  paymentTerms stay premium-gated. `t("deliveryLabel")` now unused (key left for T659 sweep).
- `frontend/messages/uk.json` + `frontend/messages/en.json` — new keys, both locales, full parity
  (verified by script — 0 keys drift).

## New i18n keys (both `uk.json` and `en.json`)

`Dashboard.marketplace.metrics`:
- `basedOnOrders` — "на основі {n} замовлень" / "based on {n} orders"
- `basedOnInquiries` — "на основі {n} звернень" / "based on {n} inquiries"
- `responseTimeInsufficient` — "недостатньо даних" / "not enough data"
- `regionsToggleShow` — "детальніше по регіонах" / "details by region"
- `regionsToggleHide` — "приховати регіони" / "hide regions"

`Dashboard.marketplace.coverage` (new namespace):
- `title` — "Регіони доставки" / "Delivery regions"
- `empty` — "Постачальник не вказав регіони доставки" / "The supplier has not specified delivery regions"
- `termsByAgreement` — "за домовленістю" / "by agreement"
- `notServed` — "Не доставляє: {regions}" / "Does not deliver to: {regions}"

`Dashboard.marketplace.deliveryByRegion` (new namespace):
- `empty` — "Ще недостатньо даних по регіонах" / "Not enough regional data yet"
- `days` — "{days} дн." / "{days} d."
- `sample` — "n={n}" / "n={n}"

## Verification

- `cd frontend && npx tsc --noEmit` — clean.
- `npm run lint` — "No ESLint warnings or errors".
- `npx vitest run` — 7 files / 50 tests pass (no marketplace component tests exist).
- i18n parity script — uk.json / en.json 0 keys drift.
- **Browser (frontend-dev 3001 + backend-dev 5000 + dev DB):** logged in as `ea@demo.local`,
  seeded `DeliveryCoverage` + metrics on a test supplier (reverted after).
  - `GET /api/marketplace/suppliers/{id}` returns `deliveryCoverage` + metrics
    `deliveryByRegion` / `deliverySampleSize` / `responseSampleSize` — camelCase, shapes match
    the TS types exactly.
  - Coverage panel renders for a FREE-plan supplier (not premium-gated): served regions with
    terms, "за домовленістю" fallback, "Не доставляє: Автономна Республіка Крим, Севастополь"
    (codes resolved to names), note.
  - Metrics: "на основі 14 замовлень" / "на основі 12 звернень" sublabels; toggle expands the
    drill-down sorted asc (м. Київ 1.8 дн. n=9 · Київська 3.1 дн. n=4 · Житомирська 4 дн. n=1).
  - Empty states verified: supplier with no coverage → "Постачальник не вказав…"; drill-down
    with no region data → "Ще недостатньо даних по регіонах".
  - Both locales (`sg_locale` cookie uk/en) render all keys; no next-intl missing-key throws,
    no React console errors.

## Notes / decisions

- Reused geo's `DeliveryCoverage` / `DeliveryCoverageEntry` (they already mirror the backend
  `DeliveryCoverageDto`); only `RegionDeliveryStat` is new and marketplace-local.
- "на основі 0 замовлень" shows when `deliverySampleSize === 0` (spec: sublabel when `!= null`).
  Kept — it makes the "—" value honestly attributable to "no data" rather than a bug.
- Concurrent T4 (TASK-651) backend work was uncommitted in the shared working tree during this
  task; committed only the 7 frontend files (`git add` by name, diffed first).
