# Glossary

**Owner:** documentation-writer
**Updated:** 2026-07-27

## Business Terms

**FEFO** — First Expired, First Out. Stock consumption rule: always sell/use the batch with the nearest expiry date first.

**Batch (Партія)** — A specific delivery of a product with a unique expiry date and optional batch number. One product can have multiple active batches in the same store with different expiry dates and quantities.

**Expiry status** — Computed hourly by `expiry-check.job`:
- `safe` — more than 14 days remaining
- `warning` — 7–14 days remaining
- `critical` — 1–6 days remaining
- `expired` — 0 or fewer days remaining
- `sold_out` — quantity = 0
- `archived` — sold_out for more than 30 days (cleanup job)
- `needs_verification` — last checked more than 90 days ago

**Safety buffer (ББ)** — Reserved minimum quantity for shelf presentation (facing). Not available for sale. If sold, counts as a lost sale.

**MOQ** — Minimum Order Quantity. Cannot order less than this from a supplier.

**USQ** — Unit Step Quantity. Order must be a multiple of this (after MOQ).

**ADU** — Average Daily Usage. Mean daily consumption over 30/60/90 days of valid sales.

**CDA** — Consumption Driven Algorithm. Buffer calculation method with Green/Yellow/Red zones for reorder point.

**MTS** — Make to Stock. Product always on shelf, regularly ordered automatically.
**MTO** — Make to Order. Special orders only, not stocked.
**NA** — Not Active. Removed from assortment.
**NM** — Not Managed. Tracked but not ordered automatically.

**RLS** — Row Level Security. PostgreSQL feature enforcing tenant isolation at DB level via policies on each table.

**Tenant** — A client company using the ShelfGuard platform (e.g. a retail chain).

**Provider** — The ShelfGuard platform owner. Role = `provider`. Has access to all tenants. TenantId = NULL in JWT.

**Impersonation** — Provider accessing a specific tenant's account for support purposes. Always logged in `activity_logs` with `is_impersonated = true`.

**TenantConnectionInterceptor** — EF Core `DbConnectionInterceptor` that sets `app.tenant_id` and `app.role` PostgreSQL session variables on every connection open. Activates RLS automatically for all queries.

## Technical Terms

**FEFO index** — `idx_stock_expiry_active` on `product_stock("TenantId", "StoreId", "ProductId", "ExpiryDate")` WHERE quantity > 0 AND status NOT IN ('sold_out', 'archived'). Critical for performant FEFO batch selection queries.

**POC Products** — Legacy `Products` table (EF entity `Product`) created for initial testing. Has no `TenantId`. Will be replaced by `catalog_products` in TASK-003b.

**catalog_products** — V1 tenant-aware product catalog table (EF entity `CatalogProduct`). Has `TenantId`, RLS, full ABM fields. This is the production product table.

**apiFetch** — Frontend HTTP wrapper in `lib/api.ts`. Handles Authorization header injection, 401 refresh retry, and session expiry redirect. All feature API modules must use `import { api } from "@/lib/api"` — never define a local apiFetch.

## Loyalty & Marketing Analytics (RFM)

**RFM** — Recency/Frequency/Monetary. Customer segmentation model: score each customer on how recently (R), how often (F), and how much (M) they buy, then bucket into named segments. Source: `docs/uployal/RFM_ANALYSIS.md` (competitor analysis); implemented in `Features/MarketingAnalytics/` (TASK-406) behind the `marketing_analytics` module key.

**R/F/M-score** — Per-dimension quintile score, 1–5 (5 = best), computed via Postgres `NTILE(5)`. Recomputed from scratch for every filter combination (period + stores) over the currently active customer population — never cached globally or reused across filters. The same customer can land in a different score/segment under a different period with zero change to their actual purchase history.

**LTV (Lifetime Value)** — Customer's all-time revenue. Unlike every other RFM metric on the same dashboard (receipt count, average ticket, revenue — all "windowed" to the active period filter), LTV is **always all-time**, regardless of period. `MarketingAnalyticsRepository.GetLtvAsync` takes no date-range parameter at all, enforcing this at the method signature rather than by convention alone.

**Lift / affinity (афінність)** — `P(companion product | buyers of the anchor product in this segment) / P(companion product | all buyers of this segment)`. How much more often a companion product is bought by buyers of one specific anchor product, versus the segment baseline — normalizes away plain popularity. Computed for exactly one anchor product at a time (`GET .../products/{productName}/affinity`), never a full pairwise scan.

**"Разом у чеку" (same-receipt / basket)** — `COUNT(receipts containing both A and B) / COUNT(receipts containing A)`. A **different** metric from lift/affinity: same-receipt co-occurrence (bundle/cross-merchandising), not cross-visit buying-pattern correlation. `GET .../products/{productName}/basket` — different endpoint, different numbers, do not conflate the two.

**Loyalty membership** — `LoyaltyMembership`: a `ConsumerAccount`'s enrollment in one tenant's bonus program (balance, TOTP-backed rotating QR/barcode, status active/blocked). Tenant-scoped, standard RLS. One `ConsumerAccount` can hold many memberships, one per tenant it has joined.

**Consumer account** — `ConsumerAccount`: the global, cross-tenant identity of an end customer (phone+password login) — completely separate from the tenant-scoped `Customer` (CRM record) and `User` (staff account). One `ConsumerAccount` JWT reads every `LoyaltyMembership` it holds across every tenant ("wallet of cards," no re-login per network). See `database-schema.md` for why this is the one table in the project with no RLS at all.

## Price Segments & Frequency/Reactivation (Фаза 2)

Source: `docs/uployal/PRICE_SEGMENTS_ANALYSIS.md` (competitor analysis); implemented in
`Features/MarketingAnalytics/PriceSegments/` (TASK-419/420) — a second mode on the same
`marketing_analytics` module key as RFM (Фаза 1), not a new module.

**Медіанний чек / типовий чек (typical check)** — `MEDIAN(receipt_amount)` per customer, over a
period or all-time. **Deliberately the median, not the mean** — this is the whole point: one
outlier receipt must not alone push a normally-200₴ customer into a top tier. Computed via
Postgres `PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY ...)`, always cast `::numeric` (Postgres
returns `double precision` otherwise — see `decisions.md` ADR-023 addendum).

**Ціновий сегмент / тір (price segment)** — `PriceSegmentKey.Tier1`..`Tier7`, a quantile tier
(cutoffs at **P20/P40/P60/P80/P90/P97** of the network's typical-check distribution — 7 tiers from
6 cutoffs, top tier open-ended). A customer's tier compares their own typical check against the
**network's own live boundaries**, never a fixed ₴ figure — the human-readable range label
(`"120–190 ₴"`) is computed dynamically per tenant (`PriceSegmentCatalog.RangeLabelUa`). Boundaries
themselves are computed **all-time**, not from whatever comparison window is active — see
`decisions.md` ADR-023 addendum for why.

**Індекс цін (price index)** — network-wide change in average unit price between the current and
previous comparison windows: `(avg_unit_price_current / avg_unit_price_previous - 1) * 100%`.
Separates genuine buying-appetite growth from a receipt that only grew because of inflation.

**"Ростуть по-справжньому" (RealGrowth) vs "Ростуть через ціни" (PriceGrowth)** — both are
"segment rose" customers (`PriceAudienceKey`), split by whether `items_per_receipt` also rose.
RealGrowth = segment up **and** more items per receipt (real appetite). PriceGrowth = segment up
but same/fewer items (bigger check from price alone — loyalty not confirmed). `Declining` = segment
fell. **`Stable`** (same segment both periods) is a full 4th `PriceAudienceKey` member with list/
sort/export/recommendation parity to the other three — unlike the competitor, which shows a
`Стабільні` KPI number but no card/list/export for it at all (analysis doc §7.4/§25.3).

**Sleeping / declining / growing / stable (частотні аудиторії)** — `FrequencyAudienceKey`, over the
**union** (not intersection) of current+previous period buyers:
- **Sleeping (Зовсім сплять)** — bought previous period, zero purchases this period.
- **Declining (Купують рідше)** — bought this period, but frequency fell by at least the tenant's
  configured decline threshold (`PriceSegmentSettings.DefaultFrequencyDeclineThresholdPercent`,
  default 30%).
- **Growing (Частота зросла)** — current frequency > previous (includes brand-new buyers with
  previous = 0 — shown as `—` percent change, never "∞").
- **Other / stable (Інші / стабільні)** — everything else in the union: unchanged frequency, or a
  decline below threshold. No dedicated card in the competitor; fully listable here.
