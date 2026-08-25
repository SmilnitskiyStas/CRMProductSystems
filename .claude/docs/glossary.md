# Glossary

**Owner:** documentation-writer
**Updated:** 2026-08-24

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

**Tier ladder (рангова драбина)** — `LoyaltyTierDefinition` (TASK-613/615): a per-tenant, admin-configured ordered list of loyalty rungs (name, minimum composite score, accrual multiplier, discount percent). Crossing into a rung is not just a badge — it changes real checkout math (`PosService`'s bonus-accrual multiplier and a per-item discount) starting with the next nightly recompute. Configured at `api/settings/loyalty/tiers`; a tenant with no ladder behaves exactly as it did before this feature shipped (every membership stays tierless, multiplier 1.0, discount 0).

**Composite score** — `LoyaltyMembership.CompositeScore` (TASK-613/619): equal-weight average of a membership's Recency/Frequency/Monetary quintile scores (`(R+F+M)/3`, the same `NTILE(5)` quintile machinery as the RFM dashboard above), computed **only** by the nightly `loyalty-tier-recompute.job.ts` worker job — never live, never at request time. Compared against each tier's `MinCompositeScore` (highest-`SortOrder` qualifying rung wins) to decide `LoyaltyMembership.CurrentTierId`. See `decisions.md` ADR-034 for why this must never be written outside that one nightly job.

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

## Audience Builder (Фаза 3)

Source: `docs/uployal/AUDIENCE_PREPARATION_ANALYSIS.md` (competitor analysis); implemented in
`Features/MarketingAnalytics/AudienceBuilder/` (TASK-428..431) — a third mode on the same
`marketing_analytics` module key as RFM (Фаза 1) and Price Segments (Фаза 2), not a new module.

**Audience builder (побудова аудиторії за товаром/категорією)** — turns purchase history into a
ready marketing audience: add one or more search terms (product-name substring, exact barcode, or
exact item id) and/or a category, optionally set a minimum quantity/amount threshold over a
period, and get every matching customer back, plus a receipt-level XLSX export for raffles/
campaigns. A different question from RFM ("who bought THIS", not "who is valuable"). Three result
tabs: Покупці товару (own-product buyers), Конкурентна аудиторія (competitor audience), Знайдені
товари (matched items — manual curation).

**Term (термін)** — one chip in the term-builder: `Text` (matches `Item.Name` by substring
`ILIKE`, OR an exact `Item.Barcodes` entry, OR an exact `Item.Id` — one field, mirroring the
competitor's "name, barcode, or external ID" box; this schema has no separate external-SKU-id
column, so `Item.Id` fills that role) or `Category` (picked from a typeahead, never free text). A
term missing its own kind's value is silently dropped server-side — same defensive posture as an
empty `Terms` list resolving to a zeroed result without touching the database.

**Any / All (Будь-який товар / Усі товари, OR / AND)** — how multiple terms combine at the
customer level; the toggle only appears once a 2nd term exists.
- **Any (OR)** — customer bought at least one item matching *any* term.
- **All (AND)** — customer bought at least one item matching *each* term — not "bought every
  matched SKU." See term coverage below for how this is evaluated without double-counting.

**Term coverage (покриття терміна)** — the AND-mode bookkeeping unit: which term_indexes a given
customer's purchases satisfied, tracked separately from that customer's total quantity/spend. One
item can match more than one term at once (e.g. a text term and a category term both matching the
same product) — that single purchase must cover both term indexes (satisfying AND) while still
counting its quantity/amount exactly **once** toward totals, never once per matching term.
`AudienceBuilderRepository` enforces this with two separate CTEs off the same underlying
line-items set (`customer_totals` vs `customer_term_coverage`) instead of one combined aggregate —
a real double-counting bug in the design doc's own SQL sketch, caught and fixed in TASK-429 with a
dedicated regression test.

**Manual SKU curation (ручна курація SKU)** — the "Знайдені товари" tab: every SKU any term/
category matched, with sold qty/receipts/buyers for the active period (zero-sales SKUs included,
never filtered out) and a checkbox. Unchecking a SKU removes it from the active selection and
instantly recalculates every KPI/table across all three tabs. This is the intended fix for text
search's main failure mode — a name substring can pull in bundles, multipacks, or discontinued
items the marketer doesn't actually want in the audience.

**Competitive audience (конкурентна аудиторія)** — `competitor_buyers_in_period MINUS
own_product_buyers` — customers who bought a competitor's term but not the tenant's own term (a
conquest/reactivation audience). Needs the shared own-product term state
(`ownTerms`/`ownExcludedItemIds` — same term-builder state the main tab uses) plus its own
`competitorTerms` chips; both sides must resolve to at least one valid term or the request
short-circuits to a zeroed result. `unitsPurchased`/`totalSpend` on this tab are always
period-scoped — the exclusion horizon below only changes who counts as "new," never the KPI window.

**Exclusion horizon (горизонт виключення) — "у періоді" vs "будь-коли"** — the two historical
windows for the competitive audience's exclusion side:
- **InPeriod (у періоді)** — excludes customers who bought the own product **within the same
  active period** as their competitor purchase. Old, out-of-window own-brand history doesn't
  disqualify them — the larger, "win back this period's basket" audience.
- **AllTime (будь-коли)** — excludes customers who **ever**, in all available history, bought the
  own product. Stricter and typically much smaller (competitor analysis' own tested example: ~23%
  the size of InPeriod) — the true never-bought-us conquest audience.

Same own-term state and manual SKU exclusions apply under both horizons; only the exclusion side's
historical window changes.

## Post-Campaign Analysis (Фаза 4)

Source: `docs/uployal/AUDIENCE_ANALYSIS.md` (competitor analysis); implemented in
`Features/MarketingAnalytics/PostCampaign/` (TASK-471..474/477) — a fourth mode on the same
`marketing_analytics` module key as RFM (Фаза 1), Price Segments (Фаза 2), and Audience Builder
(Фаза 3), not a new module.

**Post-campaign segment (пост-кампанійний сегмент)** — `PostCampaignSegment`: a marketer-uploaded
list of customer identifiers (`Customer.Id` GUIDs or phone numbers), sourced from *outside* this
system (an SMS blast, a raffle list, a Фаза 3 AudienceBuilder export), compared across equal
before/after date windows around a campaign. A different question again from RFM/Price Segments/
Audience Builder — "did THIS specific list of already-contacted people actually come back," not
"who bought THIS" or "who is valuable." Unlike Фаза 1-3 (fully stateless, computed live on every
request), this is the first **persisted** entity in the whole marketing-analytics initiative — see
`database-schema.md` TASK-471 and `decisions.md` ADR-023 addendum (Фаза 4) for why.

**Draft vs. analyzed segment (чернетка / застосований сегмент)** — a segment's lifecycle has
exactly two states, both derived from the same four nullable date columns
(`AfterStart`/`AfterEnd`/`BeforeStart`/`BeforeEnd`) — no separate boolean/enum column exists:
- **Draft** — just imported (`POST .../segments/import` succeeded); all four date columns and
  `AnalyzedAt` are null. The uploaded list and its validation results exist, but no report tab can
  be read yet (every report GET returns a 400 "not analyzed yet" on a draft segment).
- **Analyzed** — `POST .../segments/{id}/analyze` has run at least once: all four dates are frozen
  and `SegmentHash`/`AnalyzedAt` are set, and every report tab (summary/daily-turnover/rfm-activity/
  customers/migration) reads this same frozen snapshot. Re-running analyze on the same segment (new
  dates) re-freezes the window and bumps `SegmentHash`/`AnalyzedAt` in place — it never creates a
  new segment row or re-touches the uploaded member list.

**Before/after window (вікно до/після)** — `POST .../segments/{id}/analyze` takes only
`afterStart`/`afterEnd`; the before window is derived automatically as an equal-length window
immediately preceding it (`beforeEnd = afterStart − 1 day`, `beforeStart` sized to match the
after-window's exact day count) — the caller never picks the before dates directly.

**Reactivated / retained / dropped / not returned (Реактивовані / Утримані / Відпали / Не
повернулись)** — `PostCampaignBehaviorStatus`, the four mutually-exclusive behavioral states for
one matched customer, computed purely from whether they had any purchase in the before window and
any in the after window (an exhaustive 2×2 truth table):
- **Reactivated** — no purchase before, a purchase after (the campaign's target outcome).
- **Retained** — purchased both before and after.
- **Dropped** — purchased before, nothing after (churn).
- **Not returned** — no purchase before, none after either (never really active to begin with).

These four always partition the full matched segment exactly
(`reactivated + retained + dropped + notReturned == matchedCount`, enforced by construction, not by
convention — `PostCampaignBehaviorClassifier.Classify`'s truth table is exhaustive). Every rate
built on top of them (reactivation rate = reactivated / inactiveBefore; retention rate = retained /
activeBefore; churn rate = dropped / activeBefore) returns `null`, never `0%`, when its own
denominator is zero. `PostCampaignBehaviorStatus` itself is a backend-only classification — it is
never serialized on the wire; only its aggregate counts appear in `PostCampaignSummaryDto`.

**RFM migration matrix (матриця міграції RFM)** — `GET .../segments/{id}/migration`: classifies
every matched customer's RFM segment (Фаза 1's `RfmSegmentKey`) independently in the before window
and the after window, then cross-tabulates the two into a sparse 12×12 transition matrix (11 named
segments + the "Без покупок"/no-purchase null bucket; the frontend renders the full fixed grid with
dots for empty cells). Reuses Фаза 1's existing `IMarketingAnalyticsRepository.
GetScoredCustomersAsync` + `RfmSegmentClassifier` completely unchanged — no second RFM
implementation exists anywhere in this feature. See `decisions.md` ADR-023 addendum (Фаза 4) for
how a third, all-time call to the same repository method tells apart "never purchased" from "real
history, zero purchases in this specific window."

**Segment hash (in this feature's sense)** — `PostCampaignSegment.SegmentHash`: a versioning token
recomputed only when `POST .../analyze` runs (not on every request, unlike Фаза 1-3's per-request
`filtersHash`), since Фаза 4's report tabs read a frozen snapshot rather than live-filtered data.
Stored on the segment row itself rather than derived fresh per response; every report DTO echoes it
back so the frontend can detect a stale cached response after a re-analyze.
