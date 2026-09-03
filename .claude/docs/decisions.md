# Architecture Decisions (ADR Log)

**Owner:** project-architect
**Updated:** 2026-09-02 · reorganised 2026-09-02 (ADR-026 and older moved to `decisions-archive.md`)

## Index

Full text for **ADR-037 … ADR-027** is in this file. **ADR-026 and older** →
[`decisions-archive.md`](decisions-archive.md). `grep` either file by ADR-ID.

| ADR | Decision |
|---|---|
| ADR-037 | Provider-controlled `mobile_app` + `analytics` module keys — whole "Застосунок" section and "Аналітика" reports section gated; per-action `[RequireModule]` on the shared `AnalyticsController`; no backfill, default-off |
| ADR-036 | Supplier delivery coverage + performance metrics — Ukraine region registry, `MarketplaceOrder.DestinationRegionCode` snapshot, coverage not premium-gated, `supplier-metrics-recompute` worker write-boundary |
| ADR-035 | `IProviderRlsOverride` — marketplace provider bypass scoped to one repository method, replacing session-level `SET app.role` |
| ADR-034 | CRM loyalty tier ladder, consumer self-service, support tickets, reviews — phone-change verification, composite-score formula, per-item tier discount, worker write boundary |
| ADR-033 | Marketplace order receiving — client-confirmed receipt (scan/qty/expiry) replaces supplier one-click Deliver; split client-write / supplier-read RLS · **amended 2026-09-03**: `marketplace_order_item_batches` with the INVERSE split (supplier-write / client-read), receipt items now 1→N per order line |
| ADR-032 | Catalog curation — `productIds` block-prop kind + catalog-by-ids read path |
| ADR-031 | App Builder live preview — web-native mirror components (not RN-web reuse), client-side, resizable size props on the Block Registry |
| ADR-030 | SubscriptionPlan → Features — `Tenant.Plan` gates consumer features via the TASK-543 flag hook; no billing/enforcement yet |
| ADR-029 | Consumer-platform "Tenant" = existing `tenants` table; no shipped generic `UserTenant` equivalent |
| ADR-028 | KI-033 fix — `IAnalyticsRlsOverride` + `marketing_analytics_bypass` role, narrowing `pos_transactions.store_scope` for one read path |
| ADR-027 | Analytics margin — `Item.PricePurchase` as retroactive cost source, network_manager+ authorization floor |
| ADR-026 | Forgot-password redesign — temporary password replaces link/token, third RLS exception retired, auth-locale default → English *(archive)* |
| ADR-025 | Mobile offline boundary — durable drafts + limited cached reads, online-only mutations *(archive)* |
| ADR-024 | Forgot/reset-password flow — outbox reuse, third fail-open RLS exception *(archive; superseded by ADR-026)* |
| ADR-023 | Loyalty program & RFM marketing analytics — cross-tenant ConsumerAccount identity, TOTP-based live QR, independent module keys *(archive)* |
| ADR-022 | Store-scoped user assignment & data visibility (`user_locations` + RLS) *(archive)* |
| ADR-021 | TenantRole — per-role sidebar tab visibility (`AllowedTabs`) *(archive)* |
| ADR-020 | TenantRole — named custom-role templates with backend capability enforcement *(archive)* |
| ADR-019 | Temporary/permanent access grants beyond role — additive layer over `User.Permissions` *(archive)* |
| ADR-018 | Notification categories expansion + filter drawer — Postgres outbox instead of C# BullMQ producer *(archive)* |
| ADR-017 | Provider nav split (Клієнти/Постачальники) + per-item categories with JSONB attributes *(archive)* |
| ADR-016 | Supplier self-service — supplier as a separate tenant (`business_type = "supplier"`) *(archive)* |
| ADR-015 | Module-based tenant activation pattern *(archive)* |
| ADR-014 | Platform transformation — Universal Location/Item model *(archive)* |
| ADR-013 | Per-tenant fiscal provider config in DB, env fallback, per-tenant `IFiscalService` resolution *(archive)* |
| ADR-012 | Checkbox as fiscal provider behind `IFiscalService` *(archive)* |
| ADR-011 | PRRO fiscal integration — isolated client, pluggable signer, offline-first *(archive)* |
| ADR-010 | MQTT ingestion lives in the Node worker *(archive)* |
| ADR-009 | `IAnalyticsRepository` in Application layer *(archive)* |
| ADR-008 | RLS column names must be double-quoted *(archive)* |
| ADR-007 | Dashboard data from POC Products (temporary proxy) *(archive)* |
| ADR-006 | Separate `catalog_products` table (not replacing Products) *(archive)* |
| ADR-005 | Worker scaffold in TASK-000 *(archive)* |
| ADR-004 | Port mapping (avoid local conflicts) *(archive)* |
| ADR-003 | Expo SDK 56 for mobile *(archive)* |
| ADR-002 | Modular monolith over Turborepo *(archive)* |
| ADR-001 | BullMQ with ASP.NET Core *(archive)* |

## ADR-037: Provider-controlled `mobile_app` + `analytics` module keys
Date: 2026-09-02
Status: accepted — implemented (TASK-674, plan `peaceful-chasing-piglet.md`).

Context: two sidebar sections — "Застосунок" (`consumer_app` NavGroup: bonus program, loyalty
tiers, banners, promotions, catalog, App Builder, versions) and "Аналітика" (`analytics`
NavGroup: `/analytics`, `/analytics/pos`) — rendered for every tenant regardless of what the
Provider had enabled. The module-activation mechanism (ADR-015) simply was never wired to them:
no `NavGroup.moduleKey`, and the backing controllers carried no `[RequireModule]`. The `loyalty`
key (ADR-023) existed but only gated the POS QR accrual API (`LoyaltyController`). No `analytics`
key existed at all (`marketing_analytics` is the separate "Маркетинг" section).

Decision:

### 1 — two new keys in `Tenant.UpdateModules` allow-list: `mobile_app` and `analytics`
`mobile_app` gates the whole "Застосунок" section; `loyalty` is left untouched, still scoped to
the POS accrual API only — a tenant running the bonus program end-to-end needs both. `analytics`
gates the "Аналітика" reports section. Neither key is added to
`Tenant.DefaultModulesForBusinessType` — provider-granted only.

### 2 — no backfill
Existing tenants get neither key on deploy: both sections disappear until the Provider enables
the module per tenant. This is the intended outcome (remove the sections from tenants that never
paid for them), and it means **no data migration at all** — only the allow-list and the gates.
Deliberate deviation from the KI-012 `V4ModulesBackfill` precedent, which backfilled to preserve
access. Breaking-change note is in the plan / task log; the Provider re-grants post-deploy.

### 3 — `[RequireModule("analytics")]` is per-action on `AnalyticsController`, not class-level
`AnalyticsController` is shared: `expiry-summary/compare` and `dashboard/weekly-kpi` back the
main dashboard home (`features/dashboard`), and `pos/products/{productId}/trend` backs the Events
calendar's linked-product-sales card (module `pos`). Those three actions stay ungated; the other
14 (the dedicated reports) carry the attribute. `mobile_app` controllers are gated class-level
(self-contained web-admin surface) except `MobileConfigController` (`[AllowAnonymous]`, serves
the published config to the shopper app — must never be module-gated) and the 4
`customer-messages` actions on `NotificationsController` (per-action, rest of that controller is
core notifications).

### 4 — page-level gate via nested `layout.tsx` + a reusable `ModuleGate`
`frontend/features/modules/components/ModuleGate.tsx` (extracted from the inline pattern in
`marketing-analytics/page.tsx`) wraps `app/(dashboard)/consumer-app/layout.tsx` and
`app/(dashboard)/analytics/layout.tsx` — one gate per route subtree for the direct-URL case.
`provider` role bypasses; loading state renders children (no lock-screen flash).

**Consequence / out of scope:** the published shopper app (`api/consumer/*`, consumer JWT with no
`tenant_id`) keeps serving even when `mobile_app` is revoked — `[RequireModule]` can't gate it and
service-level `HasModule("loyalty")` checks for discovery already exist. Fully disabling a
tenant's shopper app on module removal is a separate task. Also: `DiscountsController`
(`AtLeastStoreManager`) is gated under `mobile_app` because today its only consumer is the
consumer-app "Акційні товари" screen — revisit if discounts become a general pricing feature.

## ADR-036: Supplier delivery coverage + performance metrics — app-side Ukraine region registry, point-in-time `MarketplaceOrder.DestinationRegionCode` snapshot, coverage deliberately not premium-gated, and the `supplier-metrics-recompute` worker write-boundary
Date: 2026-08-31
Status: accepted — implemented (TASK-648..661, plan `eventual-whistling-rabbit.md`, all merged to
`main`). Consolidated by documentation-writer (TASK-662) from the individual task logs rather than
authored up front — an already-approved, already-executed plan, not a from-scratch design session.

Context: a marketplace buyer sees almost no data about how a supplier actually performs.
`supplier_metrics` (`AvgDeliveryDays`/`ResponseTimeHours`/`OrderAccuracy`/`QualityScore`/
`CancellationRate`/`Rating`) has existed end-to-end since v4 — entity → DTO → web/mobile UI — **but
only `Rating` was ever written** (synchronously, at review time); the code comment "Updated by
background job" described a job that never existed. Geography was unmodeled: `SupplierProfile.Region`
is a free string, `SupplierProfile.DeliveryRegions` is an unused free-text jsonb array shown only as
premium chips, `Location` had only `Address` + coordinates. The feature adds: supplier-declared
per-region delivery coverage (with terms), a nightly worker job that measures actual
`DeliveredAt − ShippedAt` per destination region and chat first-reply latency, a buyer-facing "does
this supplier deliver to my region" endpoint, and a delivery-coverage section in the
cooperation-contract PDF.

Decision:

### Decision 1 — the region taxonomy is an app-side static registry, not a DB reference table
`UkraineRegions` (`ShelfGuard.Domain/Constants/UkraineRegions.cs`), mirroring `SupplierItemCategories`
— 27 ISO 3166-2:UA oblast-level units + 24 major cities, served via `GET /api/geo/regions`
(`[AllowAnonymous]`, precedent = the marketplace item-categories endpoint). Frontend and mobile
render every region picker from the endpoint and never hardcode the list; `DeliveryCoverageJson`
and `LocationService`/profile validation all check codes against `UkraineRegions.IsValid`.

Not a DB table because region *types* are static compile-time metadata (no tenant ever mints one),
the codes are read alongside the profile rather than joined, and a new table would be an RLS-triad +
audit-test surface for zero benefit — the same reasoning `domain-model.md`'s Block Registry
(TASK-538) already applied to block types.

- **`UA-30` (м. Київ — the city) ≠ `UA-32` (Київська область).** Classic confusion point; kept as
  two distinct rows, called out in the class doc and the endpoint doc; `UA-30` has no separate
  `city` child row (the code already *is* the city).
- Occupied territories — `UA-40` (Севастополь), `UA-43` (Автономна Республіка Крим) — are included
  with neutral administrative labels **specifically so a supplier can explicitly mark them "not
  served"** in `DeliveryCoverage.notServed`. The registry encodes no political status.

### Decision 2 — `MarketplaceOrder.DestinationRegionCode` is a point-in-time snapshot, not a live join through `DestinationStoreId`
`MarketplaceOrderService.CreateOrderAsync` copies the destination `Location.RegionCode` onto the
order row at creation time. Same rationale as ADR-033 (the `MarketplaceOrderReceipt`
denormalized-tenant columns) and the FEFO `expiry_date`/`batch_number` copy-on-transfer rule: a
location's `RegionCode` can be corrected later (it starts NULL on every existing location and is set
by hand through the location form), and delivery-time *history* must reflect where the goods actually
went, not where that store is filed today. The worker job then never joins `locations` at all — it
reads the frozen code off `marketplace_orders`.

**Consequence:** every order placed before migration `20260831090731` has
`DestinationRegionCode = NULL`, and those are not backfilled (region-from-`Address` is unreliable and
would feed the real statistics wrong data). Such orders still feed the *overall* `AvgDeliveryDays`
(they have `ShippedAt`/`DeliveredAt`), but no per-region row — `DeliveryByRegion` starts at n=0 and
fills only as new orders accrue. `known-issues.md` KI-038.

### Decision 3 — delivery coverage is NOT premium-gated (deliberate deviation from the `SupplierProfileDto` premium pattern)
`SupplierProfileDto.DeliveryCoverage` is populated for every caller — anonymous, free-plan and
premium alike — and `GET /api/marketplace/suppliers/{id}/coverage` carries no plan check. This is a
conscious departure from the established pattern where delivery-adjacent profile fields (`Website`,
`WorkingHours`, `PaymentTerms`, and the legacy `DeliveryRegions` chips) are premium-only.

"Does this supplier deliver to my region, and on what terms" is decision-critical for the buyer —
hiding it behind premium forces a cooperation request just to find out, which defeats the point of a
browsable marketplace. `Website`/`WorkingHours`/`PaymentTerms` stay premium. Recorded here so a
future reader does not "fix" the inconsistency by gating coverage.

### Decision 4 — the `supplier-metrics-recompute` worker job writes a fixed, disjoint column set and must never touch `Rating`/`QualityScore`
Mirrors ADR-034 Decision 4's framing for `loyalty-tier-recompute`.
`worker/src/jobs/supplier-metrics-recompute.job.ts` (cron `0 2 * * *`) writes exactly
`AvgDeliveryDays`, `DeliverySampleSize`, `DeliveryByRegion`, `ResponseTimeHours`,
`ResponseSampleSize`, `CancellationRate`, `OrderAccuracy`, `AggregatesComputedAt` — plus
`SupplierId`/`TenantId` on the INSERT branch only.

- **Never `Rating`** — owned by the synchronous `MarketplaceRepository.UpsertMetricsRatingAsync`
  (ADR-035 W1), which also owns `UpdatedAt`.
- **Never `QualityScore`** — there is no data source for it; it stays NULL end-to-end and its UI tile
  renders "—".
- **`supplier_metrics` has no `xmin` token.** Safety today rests entirely on the two writers touching
  **disjoint columns via separate UPDATE statements** — Postgres row-locking serializes them, and no
  lost update is possible because no column is written by both. This is load-bearing and fragile: any
  future "upsert all supplier metrics in one statement" path reintroduces a clobber risk against the
  synchronous `Rating` writer and **must add an explicit concurrency token first**. The rule is
  restated in the job-file header.
- The job populates **all** suppliers with a profile (no `IsPublic` filter) so the numbers are ready
  the moment a supplier publishes.

Consequences:
+ The region taxonomy has exactly one home; web, mobile and the contract-PDF generator all resolve
  codes → Ukrainian names from `UkraineRegions` (the PDF generator receives already-resolved names
  and stays IO-free).
+ `supplier_metrics`' long-dead columns finally have a writer — the stale "Updated by background job"
  comment is now true.
+ The `regionCode` filter on `GET /api/marketplace/suppliers` / `POST /api/marketplace/search` uses a
  server-side jsonb `@>` predicate (`EF.Functions.JsonContains`) inside the existing
  `IProviderRlsOverride.ExecuteAsync` block — verified via `ToQueryString()`, no `GetDbConnection()`,
  KI-036 / ADR-035 standing rule intact.
+ No new tables, no RLS policy change — the 7 new columns inherit `tenant_isolation` /
  `provider_bypass` / `worker_bypass` from their existing tables; the
  `AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass` audit is
  unaffected.
- Per-region delivery stats stay sparse for months (Decision 2) — the UI **must** show "на основі N"
  / "недостатньо даних" or a legitimately-empty drill-down reads as broken. `known-issues.md` KI-038.
- `AvgDeliveryDays` depends on the client finalizing a `MarketplaceOrderReceipt` (ADR-033) for
  `DeliveredAt` to exist → the average is biased toward diligent receiving clients; there is no
  `ConfirmedAt`, so supplier *order*-acknowledgement speed is unmeasurable — only chat first-reply
  latency. The response median counts only sessions where the supplier eventually replied (no
  "response rate" metric). All in KI-038.
- `DeliveryRegions` → `DeliveryCoverage` backfill match rate is low — free text like «Вся Україна» /
  «по домовленості» lands in `DeliveryCoverage.note`, not in structured `served` codes; affected
  suppliers may need to re-declare coverage to appear under a region filter. A
  `DeliveryCoverage IS NULL` profile still matches the region filter via a `Region ILIKE` fallback,
  so nobody vanishes from search mid-transition. `known-issues.md` KI-039.
- The cooperation-contract PDF gained «5. РЕГІОНИ ТА УМОВИ ДОСТАВКИ» (rendered only when the supplier
  has served regions); the signatures block renumbered `5.` → `6. ПІДПИСИ СТОРІН`.
  `ContractPdfGeneratorTests` assert byte-length deltas, not section text (no PDF text-extractor in
  the test project).
- `SupplierProfile.DeliveryRegions` is now `[Obsolete]` — column and mapping kept (fed to the
  deprecated `SupplierProfileDto.DeliveryRegions` for legacy rows), to be dropped by a later
  migration once the TASK-661 backfill tool has run in prod and the two
  `#pragma warning disable CS0618` reads in `MarketplaceService`/`SupplierCabinetService` are removed.
- `backend/openapi.json` was not regenerated for the new endpoints/DTOs — pending chore,
  `known-issues.md` KI-040.

Supersedes: nothing. Does not reopen ADR-033 (the `DestinationStoreId` / receiving-flow decisions
stand) or ADR-035 (the provider-bypass containment is unchanged — the new jsonb predicate lives
inside it).

Task breakdown: TASK-648 (region registry + `GeoService` + `GET /api/geo/regions`, backend/sonnet) ∥
TASK-649 (migration `AddSupplierPerformanceData` + entities, database-engineer/sonnet) → TASK-650
(coverage DTOs + `DeliveryCoverageJson` + profile read/write + order region snapshot,
backend/sonnet) → TASK-651 (coverage region filter + `GET suppliers/{id}/coverage`, backend/sonnet)
→ TASK-652 (contract-PDF §5, backend/sonnet) ∥ TASK-653 (worker
`supplier-metrics-recompute.job.ts`, backend/opus) → TASK-654..658 (frontend `features/geo` +
editors + panels + location form, frontend-developer) → TASK-660 (mobile read-only parity, also
fixes KI-037, mobile-developer) → TASK-661 (`DeliveryRegions` → codes one-shot tool, backend/sonnet)
→ TASK-662 (this documentation pass, documentation-writer).

### 2026-09-01 amendment: structured per-region delivery fields + primary supplier category (TASK-665..668)

Further refinement of the delivery-coverage shape and supplier profile structure, *not yet shipped to production*. Two changes:

**1. Structured per-region delivery entry fields, replacing the single `terms` string.**
`DeliveryCoverageEntry` now carries `deliveryDaysMin`, `deliveryDaysMax`, `minOrderAmount` (all
nullable int/decimal), and a per-region `note`, instead of a single `terms: string`. The global
`DeliveryCoverage.note` field remains. Rationale: finer-grained filtering and display at the buyer
side (the buyer can see "1–3 days, from 5000 hrn" as separate dimensions, not a free-text blob);
easier mobile/web form building (dedicated number inputs per region). JSON shape in
`supplier_profiles.DeliveryCoverage` is camelCase; no DB migration (app-level JSON reshape). Legacy
rows in the old `terms` shape self-heal on read: `DeliveryCoverageJson.Parse` moves `terms` → `note`
when `note` is empty, and `terms` is never written back (old `terms` key quietly disappears once
updated). `SupplierAgreementService.FormatDeliveryTerms` flattens the structured fields back into the
contract PDF's single `Terms` line so the PDF generator stays IO-free.

**2. One primary supplier category, set at tenant creation, read-only afterward.**
`SupplierProfile.Categories` (jsonb array) now holds 0 or 1 entry, chosen via
`CreateTenantRequest.supplierCategory` (provider-create and admin-create paths); validated
server-side only when `businessType == "supplier"`. Profile-update endpoints stop writing this field
(it remains on the DTO wire for back-compat but is ignored). Rationale: a supplier's primary
category is an immutable identity fact (a food distributor shouldn't flip to medical), so it belongs
at onboarding, not in self-serve profile edit. Cleanup step added to `DeliveryCoverageBackfill` for
dev: any multi-category rows reduced to their first. New provider endpoint
`PUT /api/provider/tenants/{id}/supplier-category` allows correcting a supplier's category
after the fact if needed.

Implemented: TASK-665 (backend), TASK-666 (frontend), TASK-667 (frontend forms + tenant
creation), TASK-668 (mobile display). No migration; no production deploy yet.

### 2026-09-01 amendment: supplier metric history + buyer-facing detail page (TASK-670..672)

Builds on the main ADR-036 design (delivery coverage + performance metrics recomputed nightly). The metric-history design extends the `supplier-metrics-recompute` job to also write a separate append-only snapshot table for trend-chart visualization.

**Decision: separate append-only `supplier_metrics_snapshots` table, not mutations to the live `supplier_metrics` row itself.** TASK-670 adds a new table `supplier_metrics_snapshots` — one row per (supplier, calendar-date) pair, keyed UNIQUE `(SupplierId, SnapshotDate)`. The nightly `supplier-metrics-recompute` job (TASK-671) runs its normal `supplier_metrics` upsert (Decision 4's write-boundary applies: `AvgDeliveryDays`, `ResponseTimeHours`, `CancellationRate`, `OrderAccuracy`, `DeliverySampleSize`, `ResponseSampleSize`, `AggregatesComputedAt` — never `Rating` or `QualityScore`), and then **also upserts one snapshot row per supplier, copying the full metric set including `Rating`/`QualityScore`**.

Why this doesn't violate the `supplier_metrics` write-boundary rule: the snapshot table is a distinct, append-only entity with its own UNIQUE key — nothing else writes to it, no clobber risk, no concurrency-token conflict with the synchronous `Rating` writer. The copied `Rating`/`QualityScore` are read from the just-updated live row (so they're always in-sync) rather than independently computed. `supplier_metrics` remains a single point of truth for current metrics; `supplier_metrics_snapshots` is a pure append-only audit trail for history.

TASK-671 adds a buyer-facing endpoint `GET /api/marketplace/suppliers/{id}/metrics-history?days=[7-365]` that reads from this snapshot table and returns `SupplierMetricsHistoryPointDto[]` (oldest→newest) — every metric field is optional (nullable) so missing data renders as gaps in the trend chart. TASK-672 implements a new `/marketplace/{id}/metrics` page with 7 metric sections (rating, delivery, accuracy, quality, response, cancellation, coverage), each showing the current value, a plain-language explanation, and a Recharts trend chart populated from the history endpoint.

Consequences:
- The buyer can now see how a supplier's metrics have changed over time (trend charts).
- The snapshot table inherits RLS from creation (`tenant_isolation` / `provider_bypass` / `worker_bypass` triad, FORCE RLS).
- `QualityScore` stays permanently null (no data source, as per Decision 4 above) — its detail-page chart and section title remain in the UI but show "—" / empty-state, not an error.
- Metric-history trend charts stay empty until `supplier_metrics_snapshots` accrues ≥2 daily rows (i.e., ~2 days after the nightly job has run twice) — documented in `known-issues.md` KI-042.

Implemented: TASK-670 (database-engineer, new table + RLS + indexes), TASK-671 (backend-developer, worker snapshot write + endpoint), TASK-672 (frontend-developer, detail page + trend charts).

## ADR-035: `IProviderRlsOverride` — scoping the marketplace provider bypass to one repository method, replacing session-level `SET app.role`
Date: 2026-08-30
Status: accepted — implemented (TASK-643 + 643b remediation), independently reviewed pre-impl
(TASK-641: SHIP-WITH-CHANGES, R1–R7 additive) and post-impl (TASK-645: SHIP-WITH-CHANGES → C1/C2
remediation confirmed → final verdict **SHIP**), real-Postgres RLS regression coverage added
(TASK-644, leak proven to fail pre-fix). **Committed `f14ea7f6`, auto-deployed to production
2026-08-30** (CI green incl. "Deploy → production"). See KI-036 in `known-issues.md` for the
closed-out bug and the full verification chain.

Context: a user hit a functional bug at marketplace checkout — the "Знайдено збіги штрихкодів"
(barcode-collision) dialog claimed the client's order lines already existed in their catalog "under
another name" even though that client's `Item` catalog was completely empty; the shown "matches"
were other tenants' `Item` rows. Root-cause investigation (main session + 3 Explore agents + a Plan
agent, then TASK-641's threat model) found a cross-tenant RLS leak:

`MarketplaceRepository.SetProviderRoleAsync` (`MarketplaceRepository.cs:410-419`, pre-fix) issued a
**session-level** `SET app.role = 'provider';` (not `SET LOCAL`, no enclosing transaction) directly
on the request-scoped `AppDbContext`'s `DbConnection`, and never reset it. It also called
`conn.OpenAsync()` manually, which makes EF treat the connection as externally-owned and stop
closing it after each query, so `TenantConnectionInterceptor.ConnectionOpenedAsync` never re-fired
to restore the caller's real role. Every subsequent statement in that HTTP request ran as
`app.role='provider'`. `items.provider_bypass` is a PERMISSIVE `FOR ALL` policy whose `WITH CHECK`
is `NULL` (Postgres defaults it to the `USING` expression) ⇒ cross-tenant **read AND write**; being
PERMISSIVE it ORs with `tenant_isolation`, so a fail-closed `tenant_isolation` could never contain
it (confirmed on live prod — TASK-642). `ItemRepository.GetByAnyBarcodeAsync`/`GetByIdAsync` carry
no app-level `TenantId` filter (documented project convention — `CLAUDE.md` "Tenant isolation via
RLS"), so under the leaked role they returned every tenant's rows.

**This is KI-028's hypothesised risk class — "a code path that runs SET ROLE / SET app.role" —
realized in production code, and the first confirmed live instance.** Blast radius: read disclosure
of foreign `Item` id/name/imageUrl/barcodes; a self-contained cross-tenant **write** vector (F2 —
`catalogAction:"link"` replays the disclosed foreign `Item.Id` into `_items.Update`, and
`DbSet.Update` rewrites the whole `.Include`d graph, so `categories`/`product_segments`/`suppliers`
foreign rows too); and cross-tenant Claude-API-key consumption on `POST /api/marketplace/ai-recommend`
(F5). Full detail in KI-036.

Precedent (mirrored): `IAnalyticsRlsOverride`/`AnalyticsRlsOverride` + ADR-028 (KI-033) — `SET LOCAL`
in a short explicit transaction, auto-revert, wrapped inside the repository, security contract as an
interface XML doc. `ITenantSessionOverride` (TASK-417) is the same shape for `app.tenant_id`.

Decision:

### Decision 1 — new `IProviderRlsOverride` primitive, not an inline helper
`ShelfGuard.Application/Services/IProviderRlsOverride.cs` (impl
`ShelfGuard.Infrastructure/Services/ProviderRlsOverride.cs`, DI `AddScoped` immediately after
`IAnalyticsRlsOverride`), signature identical to `IAnalyticsRlsOverride`
(`Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct = default)`). Implementation
one-for-one with `AnalyticsRlsOverride`: `BeginTransactionAsync` → `ExecuteSqlRawAsync("SET LOCAL
app.role = 'provider'")` (fixed literal ⇒ no `#pragma warning disable EF1002`) → `action()` →
`CommitAsync`; reverts on commit, rollback or unhandled exception. It deliberately does **not** join
an ambient transaction — if anyone ever nests it, EF throws `InvalidOperationException` loudly
rather than silently widening an outer transaction's RLS context.

Chosen over an inline `SET LOCAL` helper because: the ADR-028 precedent is binding here; the
security contract needs a documented home (the interface XML doc); a substitutable primitive lets
`ProviderRlsOverrideContainmentTests` assert both that every bypass method goes through it and that
no other type acquires it; and repositories in this codebase do not otherwise manage transactions.

`SetProviderRoleAsync` — including its `GetDbConnection()` and `conn.OpenAsync()` — was **deleted
entirely** (root-cause removal; "no `GetDbConnection()` left in `MarketplaceRepository`" is a
standing review criterion). 12 provider-bypass reads each wrap their existing body in one
`ExecuteAsync` block (`SearchSuppliersAsync`'s two dependent queries share one block;
`GetSupplierItemImagesByIdsAsync`'s `Count == 0` early return stays outside). The 13th,
`GetReviewByIdAsync`, was deleted as dead code once W2 moved (Decision 3) rather than left as a
repurposable bypass method.

### Decision 2 — keep the `'provider'` role value; no dedicated sentinel (deferred hardening)
ADR-028 minted `marketing_analytics_bypass` because it was **widening** a policy — adding a new
value to `pos_transactions.store_scope`'s IN-list, which needed a migration. This change is
different in kind: `provider_bypass` already exists and `MarketplaceRepository` already set
`app.role='provider'`; wrapping it in `SET LOCAL` inside a short transaction only **narrows the
duration** of an already-existing bypass (from "rest of the HTTP request" to "one transaction"). No
policy changes, no new row becomes reachable, and `app.role` is never read as an authorization input
outside RLS `USING` clauses (checked — `[Authorize(Policy = ProviderOnly)]` reads the JWT claim, not
the DB GUC). A migration would buy nothing for correctness.

**Blast radius, stated as a number:** `provider_bypass` was on **107 tables measured 2026-08-30**
(`SELECT count(*) FROM pg_policies WHERE policyname='provider_bypass'`), and **109 a day later**
after an unrelated concurrent migration (`20260830143000_AddCustomerMessageCampaignSnapshots`) — it
grows with every new RLS table. So `'provider'` is a whole-schema cross-tenant read+write bypass; it
is narrow **only in duration**, never by table. Phrase it that way, never as a fixed number.

A dedicated sentinel (e.g. `marketplace_provider_bypass`, mirroring ADR-028) is recorded here as
**deferred hardening**. Concrete revisit trigger: any new `IProviderRlsOverride` call site outside
`MarketplaceRepository`, **or** any `ExecuteAsync` block body that touches a non-marketplace table
or calls outward to another service/repository/override. The moment Decision 1's or Decision 3's
containment invariant is relaxed, the sentinel stops being bikeshedding and becomes the right fix.

### Decision 3 — repository-layer containment; `IProviderRlsOverride` never reaches the service layer
Follows ADR-028 point 3. Two downstream cross-tenant writes legitimately needed the bypass (TASK-641
§3 confirmed these are the only two — no third exists):
- **W1** — `MarketplaceService.RecalculateRatingAsync` writes `supplier_metrics` under the
  **supplier's** tenant while the session is the reviewer's. Became
  `IMarketplaceRepository.UpsertMetricsRatingAsync(supplierId, supplierTenantId, rating, ct)` —
  load-or-create + `SaveChangesAsync` in one `ExecuteAsync` block (covers both the UPDATE and the
  cross-tenant INSERT branch).
- **W2** — `SupplierCabinetService.ReplyToReviewAsync` writes `supplier_reviews` under the
  **reviewer's** tenant while the session is the supplier's. Became
  `SetReviewReplyAsync(supplierId, reviewId, replyText, repliedAt, ct)` — filtered load +
  mutate + save in one block; returns `null` when absent (preserves "never reveal existence").

Both composites have narrow, purpose-shaped signatures, touch exactly one table, and cannot express
a general "run this under provider role" request. `IProviderRlsOverride` is deliberately **not**
injected into `MarketplaceService`/`SupplierCabinetService` — there is no per-call trust value for a
service to vouch for, and it keeps the contract surface minimal.

**F10 caller contract** (XML doc + review criterion, not type-enforced): these composites call
`SaveChangesAsync` on the shared `AppDbContext` under the provider role, so they flush **any**
pending tracked change — every caller must flush its own writes first. Verified to hold at both call
sites today (`CreateReviewAsync` flushes the review before `RecalculateRatingAsync`;
`ReplyToReviewAsync` stages nothing before the composite). Both composites also detach the
foreign-tenant entity after the block so no foreign row lingers in the shared change tracker.

`ProviderRlsOverrideContainmentTests` (reflection over **Application + Infrastructure + Api**
assemblies — Api added per TASK-645 C2 because `MarketplaceChatController` is live precedent for a
controller injecting a repository directly) asserts `MarketplaceRepository` is the only type taking
`IProviderRlsOverride` as a constructor parameter or holding it in a field.

### Decision 4 — targeted application-level `TenantId` filtering at 3 `MarketplaceOrderService` sites
A **scoped** adoption of KI-028's rejected option (c) — explicitly **not** a codebase-wide "add
`WHERE TenantId=` everywhere" change. All three filters use `clientTenantId`, which is JWT-derived
(`MarketplaceCooperationController.ResolveTenantId()`), never from the request body:
1. `CheckCatalogConflictsAsync` — `matches.FirstOrDefault(m => m.TenantId == clientTenantId)`, so a
   foreign row never reaches `MarketplaceOrderConflictingItemDto`.
2. `PlanCatalogOutcomeAsync` — `linkedItem is null || linkedItem.TenantId != clientTenantId` →
   the same `LinkedItemNotFoundError` as a genuine miss; collision set filtered to
   `TenantId == clientTenantId`; the disproved doc comment (which asserted "ambient RLS resolves a
   foreign-tenant id to null") rewritten. The matching second copy at
   `MarketplaceOrderReceiptService.cs:153-154` was rewritten in the same change (R5).
3. `ExecuteCatalogPlanAsync` — re-validates `plan.LinkedItem!.TenantId != clientTenantId` before
   `_items.Update` (a genuinely independent second check — pass 1 and pass 2 are loop-separated).

`ItemRepository.GetByAnyBarcodeAsync`/`GetByIdAsync`/`GetByBarcodeAsync` signatures are left
untouched — adding a `tenantId` parameter contradicts `backend-structure.md`'s "trust RLS"
convention. Filtering at the one consumer that sits next to the bypass is the proportionate change.

Consequences:
+ The two composite writes and — after TASK-645 C1 — `NextOrderNumberAsync`'s order-number count no
  longer rest on the leak. C1: `MP-{yyyy}-{NNN}` was only sequential-per-supplier because the leaked
  `provider` role satisfied `marketplace_orders.provider_bypass`; a customer-visible identifier
  scheme was unknowingly resting on the leak. `NextOrderNumberAsync` now counts inside
  `_tenantSessionOverride.ExecuteAsync(supplierTenantId, …)`. There is no unique index on
  `OrderNumber`, so removing the leak without C1 would have silently produced duplicate order
  numbers for two clients of one supplier.
+ F2 (write vector) and F5 (cross-tenant Claude-API-key consumption on `/ai-recommend`) are both
  closed by Part A — F5 for free, since the leak is what enabled it.
- `GET /api/marketplace/suppliers/{id}`, `/items`, `/reviews` each now open 2–3 short explicit
  transactions where they previously opened none — a few extra round-trips on these anonymous
  endpoints. Consistency is unchanged (the statements were already separate).
- Nested-transaction misuse of any override primitive now throws `InvalidOperationException` loudly
  instead of silently joining the ambient transaction.
- One new interface + implementation + DI line + 2 composite repo methods; `MarketplaceOrderServiceTests`
  / `MarketplaceServiceTests` / `SupplierCabinetServiceTests` reworked; 2 new real-Postgres RLS
  integration files. `AddMetricsAsync` (unused after W1 moved) and `GetReviewByIdAsync` (unused
  after W2 moved) deleted from `IMarketplaceRepository`.
- **F7 — the leak was previously bounded to one HTTP request only by Npgsql's default `DISCARD ALL`
  pool reset on connection return (`No Reset On Close=false`, not overridden in any connection
  string).** If that flag is ever set `true` for perf, this class of stale-`app.role` bug returns
  **cross-request** — and `TenantConnectionInterceptor.BuildSetSql` would not save it: when the JWT
  role is absent or not whitelisted it emits no `SET app.role` at all, and `supplier_admin` is not
  in `TenantConnectionInterceptor.ValidRoles`, so supplier-cabinet requests would inherit whatever
  stale value the pooled connection carried. Recorded here and in `backend-structure.md`.

Supersedes: nothing. Introduces a second, independent `SET LOCAL` override primitive alongside
ADR-028's `IAnalyticsRlsOverride`; changes no RLS policy and does not reopen ADR-028.

Task breakdown: TASK-641 (pre-impl threat model, security-reviewer/opus — R1–R7) ∥ TASK-642 (prod
`items` fail-open verification, database-engineer/opus — `database-schema.md:108` was stale, prod
already fail-closed since the 2026-07-16 audit deploy, no migration) → TASK-643 + 643b
(implementation + C1/C2 remediation, backend-developer/opus) → TASK-644 (real-Postgres RLS
regression, qa-tester — leak proven to fail pre-fix) → TASK-645 (independent post-impl review,
security-reviewer/opus — final verdict SHIP) → TASK-646 (this documentation pass). Uncommitted as of
2026-08-30.

## ADR-034: CRM loyalty tier ladder, consumer self-service, support tickets, reviews — phone-change verification, composite-score formula/timing, per-item tier discount, worker-job write boundary, ticket/review pattern reuse, review-ownership resolution path
Date: 2026-08-24
Status: accepted

Context: TASK-613..622 (plan `C:\Users\stass\.claude\plans\goofy-bubbling-naur.md`) extended the
customer/loyalty domain: self-service profile editing with audit history, a per-tenant loyalty
tier ladder with functional accrual/discount benefits, a consumer↔tenant support-ticket channel,
and a per-purchase review channel. Several implementation-time judgment calls were made across the
database-engineer/backend-developer/devops-engineer waves that a future reader is likely to
question without this record — consolidated here by documentation-writer (TASK-623) from the
individual task logs (613-622, 621b) rather than authored upfront as a single design session,
since this was an already-approved, already-executing plan, not a from-scratch architecture
decision.

Decision:

### Decision 1 — Phone-change verification: password re-entry only, no SMS/OTP
`PUT /api/consumer/profile/phone` (TASK-614) gates a phone change behind the caller's current
password, not an SMS one-time code. No SMS gateway exists anywhere in this repo, and — the
decisive point — registration itself (`POST /api/consumer-auth/register`) never verifies phone
ownership either. Requiring OTP only on *change* would make editing a phone number a stronger
security bar than establishing one in the first place, which is backwards. If an SMS gateway is
ever added for another reason, this decision should be revisited.

### Decision 2 — Tier discount applied per item, not as a lump-sum total reduction
`PosService.CreateSaleAsync` (TASK-615) computes the tier discount per `PosTransactionItem` (off
`priceRetail`, additively combined with any existing critical-batch auto-discount, capped at the
item's price), not as one subtraction from `tx.TotalAmount` the way loyalty-balance redemption
already works. Reason: Checkbox fiscal receipt line items are built from `PosTransactionItem` rows,
never backfilled from the transaction total — a lump-sum reduction would leave per-item
`PriceFinal`/`DiscountAmount` inconsistent with what actually printed on the fiscal receipt. Same
reasoning the pre-existing critical-batch auto-discount already follows. No live ПРРО-compliance
owner was available to confirm synchronously at implementation time; this is the accepted default,
not a compliance-verified sign-off — revisit if fiscal integration surfaces a conflict.

### Decision 3 — Composite score: equal-weight `(R+F+M)/3`, computed nightly, never live
`worker/src/jobs/loyalty-tier-recompute.job.ts` (TASK-619) computes each active membership's
Recency/Frequency/Monetary scores via the same `NTILE(5)` quintile approach
`MarketingAnalyticsRepository` already uses for RFM, then averages them with equal weight — no
tenant-configurable weighting exists (default only; revisit if a tenant asks for a different mix).
Computed once nightly (04:00, after `cleanup`, before `weather-fetch`/`ai-order`), never at request
time — because tier assignment has a real functional consequence (accrual multiplier, checkout
discount) the moment it changes, batch computation makes that a predictable, auditable daily event
rather than a mid-shopping-session surprise.

### Decision 4 — Worker job: direct-SQL, and a hard rule that it only ever writes 3 columns
Structured like `weekly-report.job.ts` (raw `pg` queries, `SET app.role = 'worker'` for
`worker_bypass` RLS) rather than the callback-into-API pattern `ai-order.job.ts` uses — that file's
own history of silent bugs (missing `SET app.role`, a stale table name surviving the v4 rename)
made the callback indirection the wrong template to copy here.

**Load-bearing rule, worth restating outside the code comments:** this job writes exactly
`LoyaltyMembership.CurrentTierId`/`CompositeScore`/`TierScoreUpdatedAt` and nothing else on that
table — never `Balance`. `Balance` is protected by an `xmin` optimistic-concurrency token
(TASK-414) that `PosService`/`LoyaltyService` rely on for every sale/redemption/manual-adjustment
write; a nightly batch job touching `Balance` (or touching the row in a way that bumps `xmin`
unnecessarily) would produce spurious concurrency-conflict retries on completely unrelated,
concurrent point-of-sale writes. Any future change to this job — or any other job that touches
`loyalty_memberships` — must preserve this boundary. See `database-schema.md` TASK-613 and
`domain-model.md`'s `LoyaltyMembership` entry.

### Decision 5 — Review ownership resolved via the loyalty-ledger join, not `PosTransaction.CustomerId`
`PurchaseReview` (TASK-617) has no direct link to `PosTransaction.CustomerId` — deliberately.
`ReviewService.IsOwnPurchaseAsync` instead walks
`LoyaltyLedgerEntry.PosTransactionId → MembershipId → LoyaltyMembership.ConsumerAccountId`
(`ILoyaltyRepository.GetLedgerEntriesForTransactionsAsync`, already existing since TASK-410).
Reason: that method's own doc comment already states the ledger link is "the only persisted signal
that loyalty activity happened on that sale" — a stronger, documented invariant than assuming
`CustomerId` maps to exactly one `ConsumerAccount` (true in practice via the phone-match
find-or-create, but never stated as a guarantee anywhere).

**Resulting limitation, accepted, not a bug:** a walk-in or otherwise loyalty-unlinked purchase can
never be reviewed — `IsOwnPurchaseAsync` returns false for it (no ledger entry to walk), and the
consumer gets the same generic 403 as "this is someone else's purchase." A purchase must have
produced at least one loyalty ledger entry (accrual or redemption) to become reviewable.

### Decision 6 — `ConsumerSupportTicket`/`PurchaseReview` mirror the Supplier-side patterns, not ServiceDesk
Both new features (TASK-616/617) are structural mirrors of the already-shipped
`SupplierSupportTicket`/`SupplierSupportTicketMessage` and `SupplierReview` — not the pre-existing
`ServiceDesk` module. ServiceDesk models tenant↔SaaS-provider support (a ShelfGuard customer asking
the platform's own support team for help); this feature models consumer↔tenant support (an end
shopper asking a retail tenant's own staff a question) — a different relationship on both ends,
despite the surface-level similarity of "ticket with messages." Reusing the Supplier-side entities
as the template (rather than either ServiceDesk or inventing a third pattern from scratch) keeps
the codebase to two proven ticket/review shapes instead of three near-duplicates.

Consequences:
+ No SMS/phone-verification infrastructure had to be built to ship Decision 1 — deferred cleanly to
  whenever (if ever) an SMS gateway becomes a real capability of this platform.
+ Fiscal-receipt correctness (Decision 2) is preserved with no special-casing in the Checkbox
  integration itself — the discount is already "just another line-item discount" by the time it
  reaches fiscalization.
+ The nightly job (Decisions 3/4) can never corrupt a concurrent sale's `Balance` update — verified
  live by QA (TASK-622) via a direct SQL dry-run of the job's exact write path.
- Decision 5's limitation is real and by design, not merely deferred: it is not possible today to
  let a walk-in customer review a purchase after the fact, even if staff later link a `Customer`
  record to that transaction retroactively. A future fix would need a direct, first-class
  purchase↔reviewer link rather than routing through the loyalty ledger.
- Composite-score weighting (Decision 3) has no per-tenant override — every tenant using the tier
  ladder gets the same `(R+F+M)/3` formula. Flagged in the plan as needing confirmation before
  implementation; no alternative weighting was requested, so the default shipped as-is.

Task breakdown: TASK-613 (schema, database-engineer) → TASK-614/615/616/617/618/621b (backend,
backend-developer) → TASK-619 (worker job, devops-engineer) → TASK-620/621 (frontend,
frontend-developer) → TASK-622 (QA, qa-tester, no bugs found) → TASK-623 (this documentation pass,
documentation-writer). Mobile screens (profile edit, tier/progress display, review submission,
support-ticket screen) are explicitly out of scope — see the plan §4 mobile hand-off note and
`api-contracts.md`'s new consumer-facing endpoint groups, which the mobile team's (Codex) agent can
build against directly. Mobile hand-off doc: `.claude/logs/handoffs/623-to-mobile-codex.md`.

## ADR-033: Marketplace order receiving — client-confirmed receipt (scan/qty/expiry) replaces the supplier one-click Deliver; new `MarketplaceOrderReceipt`/`Item` entities, `MarketplaceOrder.DestinationStoreId`, split client-write/supplier-read RLS
Date: 2026-08-21
Status: accepted · **amended 2026-09-03** (supplier-portal expansion Phase 3, plan
`1-partitioned-book.md` D4, TASK-683)

### Amendment 2026-09-03 — batch handoff: receipt items become 1→N per order line

Three things change; everything else in this ADR stands.

**1. New table `marketplace_order_item_batches`, with the INVERSE of this ADR's split RLS.**
Decision 3 gave `marketplace_order_receipts`/`_items` a client-write + supplier-read split
(`tenant_isolation` on `ClientTenantId`, `supplier_read` FOR SELECT on `SupplierTenantId`),
because the client is the party that physically receives. The new table records the opposite
half of the same handshake — which `supplier_stock` batches the SUPPLIER picked and shipped —
so it gets the mirror image: `tenant_isolation` (FOR ALL + WITH CHECK) on `SupplierTenantId`,
plus `client_read` (FOR SELECT only) on `ClientTenantId`, plus the usual
`provider_bypass`/`worker_bypass`, all under FORCE RLS. This is the only marketplace table
pointing that way, and it is proved on real Postgres by
`MarketplaceOrderItemBatchRlsIntegrationTests` (supplier writes; client selects but gets 42501
on insert and zero rows on update/delete; a third supplier tenant and a RESET session see
nothing). Documented residual: a client session *can* insert a row naming ITSELF as the
supplier — that row is invisible to the real supplier and can only affect the client's own
draft prefill, whose expiry/batch fields the client already types by hand, so it is not an
escalation; a test pins it so it stays understood rather than discovered.

**2. `MarketplaceOrderReceiptItem` is no longer 1:1 with an order line.** When a shipped order
carries batch allocations, `GetOrCreateDraftAsync` creates one receipt item **per batch**,
prefilled with `QuantityOrdered = batch.Qty`, `ExpiryDate`, `BatchNumber` and the new FK
`SourceOrderItemBatchId`. `ProductId`/`QuantityReceived` stay null, so Decision 5's finalize
gate is untouched — only its expiry third arrives pre-answered, the scan and the count still
happen. Orders with no batches (legacy rows, or a shipment made while the supplier's
`supplier_inventory` module is off) keep the original one-item-per-line shape. `ReceiveAsync`
needed **no change at all**: it already iterates `receipt.Items` producing one `ProductStock` +
one `StockMovement` each, so N sub-rows naturally become N correctly-dated client batches —
which is the whole point of the handoff. It remains the only code path that may set
`Delivered` (Decision 4).

**3. `Shipped` still has no entry in `AllowedTransitions`,** and shipping now has exactly one
implementation: `MarketplaceOrderService.ShipOrderAsync`. The legacy
`POST /api/supplier-cabinet/orders/{id}/status {status:"shipped"}` delegates to it with no
warehouse and no allocations, which reproduces the pre-Phase-3 behaviour exactly; the new
`POST .../ship` is the same method with a source warehouse and an allocation plan. Consequence
worth stating: with the module ON, a supplier that ships through the legacy endpoint ships
without consuming stock — the frontend routes to `/ship` when the module is on, and nothing in
the data model is corrupted either way (the client simply falls back to hand-entered expiries).

Context: `MarketplaceOrderService.UpdateOrderStatusAsync` today lets the **supplier** flip a B2B
marketplace order `Shipped → Delivered` with one click, no verification of what actually arrived.
Product owner wants the **client** (receiving tenant) to confirm physical receipt instead —
mobile employee scans the product barcode, enters received quantity, enters batch expiry date —
replacing the supplier's button as the only path to `Delivered`. Full design brief (3 Explore
agents' research + recommended architecture) is at
`C:\Users\stass\.claude\plans\abundant-popping-ladybug.md`; this ADR resolves that plan's open
points into a concrete, buildable spec and corrects/sharpens several places the plan left as
"decide later." Mobile implementation is **out of scope for this session** (a separate Codex-based
agent builds it against the API contract sketched in Decision 5, from the handoff doc referenced
at the end); this session covers backend + web-adjacent architecture only, database-engineer and
backend-developer implement in follow-up sessions.

A working, already-shipped reference pattern exists for exactly this shape of problem:
`StockReceipt`/`StockReceiptItem`/`ReceiptService` (regular supplier deliveries — ordered vs.
received qty, batch, expiry, non-blocking discrepancy notes, a `ReceiveAsync` gate that requires
every item to have `ExpiryDate` before it creates `ProductStock`+`StockMovement`). The new feature
is a marketplace-specific sibling of that pattern, not a rebuild of it.

Decision:

### Decision 1 — New `MarketplaceOrderReceipt`/`MarketplaceOrderReceiptItem` entities; `StockReceipt` reuse rejected (confirms the plan, with one added argument)

The plan's own rejection reasons hold: `StockReceiptItem.ProductId` is required non-nullable,
but a marketplace receipt item must exist *before* scanning resolves `ProductId` (the row is
created empty from the order's snapshot line, filled in as the employee scans); and
`StockReceipt.SupplierId` points at the tenant's own local `Supplier` catalog row, a different
and incompatible reference from a marketplace order's `SupplierTenantId`.

**One more reason the plan didn't name, and the deciding one for this ADR:** `StockReceipt` is
a **single-tenant** row — RLS is plain `tenant_isolation` on `TenantId`, no counterparty ever
reads it. The new receiving flow needs the **opposite**: the client writes, and (Decision 3) the
supplier tenant needs read-only visibility into what was actually received, for its own cabinet
view (plan section 4). Bolting cross-tenant read access onto `StockReceipt`/`StockReceiptItem` —
an already-shipped, tested, single-tenant table used by every non-marketplace delivery in the
system — to serve one new marketplace-only read case would widen that table's RLS blast radius
for every existing caller, just to save one new (small, low-risk) migration. A separate entity
pair keeps `StockReceipt`'s RLS contract exactly as it is today and gives the new cross-tenant
read requirement its own policy set, scoped to only the rows that need it.

**Exact field lists (verbatim spec for database-engineer):**

**`MarketplaceOrderReceipt`** → table `marketplace_order_receipts`

| Field | Type | Nullable | Notes |
|---|---|---|---|
| `Id` | `Guid` | no (PK) | `gen_random_uuid()` default, matches every sibling entity |
| `MarketplaceOrderId` | `Guid` | no | FK → `marketplace_orders.Id`, `ON DELETE RESTRICT` (orders are never hard-deleted, only status-transitioned — Restrict matches `marketplace_orders`' own FK convention). **UNIQUE** index — enforces the plan's explicit v1 scope limit "one receiving session per order," no partial/multiple receipts |
| `ClientTenantId` | `Guid` | no | Denormalized copy of `MarketplaceOrder.ClientTenantId` at draft-creation time — avoids a join in the RLS policy, same convention `MarketplaceOrderItem` already established for this feature area |
| `SupplierTenantId` | `Guid` | no | Denormalized copy of `MarketplaceOrder.SupplierTenantId` — needed by the new `supplier_read` policy (Decision 3), same reasoning as `ClientTenantId` above |
| `DestinationStoreId` | `Guid` | no | Copied from `MarketplaceOrder.DestinationStoreId` (Decision 2) at draft-creation time — the store `ProductStock` rows get created against. FK → `locations.Id`, `ON DELETE RESTRICT` |
| `Status` | `string` | no | `"draft"` → `"received"` only — **no `"cancelled"` state** (see "Rejected alternatives") — default `"draft"` |
| `CreatedByUserId` | `Guid?` | yes | Client-side user who started the draft. FK → `users.Id`, `ON DELETE SET NULL` |
| `ReceivedByUserId` | `Guid?` | yes | Set on finalize. FK → `users.Id`, `ON DELETE SET NULL` |
| `ReceivedAt` | `DateTimeOffset?` | yes | Set on finalize |
| `CreatedAt` | `DateTimeOffset` | no | `NOW()` default |
| `UpdatedAt` | `DateTimeOffset` | no | `NOW()` default, bumped on every item update |

**`MarketplaceOrderReceiptItem`** → table `marketplace_order_receipt_items`

| Field | Type | Nullable | Notes |
|---|---|---|---|
| `Id` | `Guid` | no (PK) | `gen_random_uuid()` default |
| `ReceiptId` | `Guid` | no | FK → `marketplace_order_receipts.Id`, `ON DELETE CASCADE` (matches `marketplace_order_items.OrderId → marketplace_orders` Cascade) |
| `MarketplaceOrderItemId` | `Guid` | no | FK → `marketplace_order_items.Id`, `ON DELETE RESTRICT` — which ordered line this closes; order items are never deleted in practice |
| `ClientTenantId` | `Guid` | no | Denormalized, same rationale as the parent |
| `SupplierTenantId` | `Guid` | no | Denormalized, same rationale as the parent |
| `ProductId` | `Guid?` | **yes** | Resolved at scan time — the deliberate divergence from `StockReceiptItem.ProductId` (required). FK → `items.Id`, `ON DELETE SET NULL` |
| `ItemNameSnapshot` | `string` | no | Copied from `MarketplaceOrderItem.ItemName` at draft-creation time — lets the mobile UI show "what you're supposed to be scanning" *before* `ProductId` resolves, matching `varchar(500)` (same width as `MarketplaceOrderItem.ItemName`) |
| `QuantityOrdered` | `decimal` | no | `numeric(12,3)` — **matches `MarketplaceOrderItem.Qty`'s precision, not `StockReceiptItem.QuantityOrdered`'s `numeric(10,2)`** — this field is a direct snapshot of `MarketplaceOrderItem.Qty` and must reconcile against it without rounding drift |
| `QuantityReceived` | `decimal?` | yes | `numeric(12,3)`, same reasoning |
| `ExpiryDate` | `DateOnly?` | yes | `date`, matches `StockReceiptItem.ExpiryDate` exactly |
| `BatchNumber` | `string?` | yes | `varchar(100)`, matches `StockReceiptItem.BatchNumber` exactly |
| `DiscrepancyNotes` | `string?` | yes | `text`, matches `StockReceiptItem.DiscrepancyNotes` exactly |

### Decision 2 — `MarketplaceOrder.DestinationStoreId`: nullable `Guid` at the DB level, required by application-layer validation for every new order

Confirmed needed: `MarketplaceOrderService.CreateOrderAsync` (read in full) has zero location
concept today — nothing on `MarketplaceOrder`/`MarketplaceOrderItem` says which of the client's
stores the goods are headed to, and `ProductStock` cannot exist without a `StoreId`. Type: `Guid`,
FK → `locations.Id` (`ON DELETE RESTRICT`, matching `StockReceipt.DestinationStoreId`'s target
entity — the receiving side always models "store" as `Location`, never a separate `Store` type).

**Nullable at the DB column, not `NOT NULL`.** This is the one place this ADR diverges from
reading the plan's field sketch at face value (it didn't flag the tension). Orders placed
*before* this migration ships have no possible value to backfill into this column — nobody can
retroactively know which store a historical order was headed to, and a `NOT NULL` constraint
would force picking *something* (wrong) for every pre-existing row, including ones already
`Delivered`/`Cancelled` that will never be received through this flow again. Instead:
`CreateOrderAsync` gets a new validation branch — `request.DestinationStoreId is null` → 400,
same shape as the existing `EmptyOrderError` check — so every order placed **after** this ships
always has one, while the DB stays permissive for the historical gap. This is the same pattern
already established in this exact codebase for an analogous "column must exist for new rows,
can't be enforced for old ones" situation: ADR-017 point 5, `SupplierItem.category`/`attributes` —
"nullable columns, DEFAULT NULL... a valid state forever, not a temporary migration pit." Set at
the client-side order-creation flow (frontend-developer's job, out of this ADR's scope) — a
required store picker on the order/cart form, not inferred from any ambient "current store"
context, because an order is a future delivery to one specific store, not tied to whatever store
the ordering user happens to be viewing (the plan's own reasoning for rejecting
`usePrimaryStoreId()` here is correct and this ADR endorses it — TASK-583 precedent is about
*viewing* the user's current store context, this is *choosing* a delivery destination).

### Decision 3 — RLS: split `tenant_isolation` (client, full read/write) from a new `supplier_read` (supplier tenant, `SELECT`-only) policy — sharpens the plan's "decide whether the supplier gets read access" into a concrete shape

**Supplier gets read access, not write.** The plan's own section 4 ("Видимість на веб") already
commits to this in practice — it requires the supplier cabinet to show "фактично отримані дані"
(actually-received qty/batch/expiry/discrepancies) after `Delivered` — so the read side isn't
optional, it's already scoped work for frontend-developer downstream. The write side must stay
client-only: this is the client's physical confirmation of receipt, nothing about it is a
supplier-authored fact, and there is no legitimate case for a supplier session to create or edit
a `MarketplaceOrderReceiptItem` row.

**Why this needs a genuinely different RLS shape than every existing two-tenant table in this
feature area, not just a copy-paste of the `marketplace_orders` policy:** `marketplace_orders`/
`marketplace_order_items` use one `tenant_isolation` policy with no `FOR` clause (`SupplierTenantId
= ... OR ClientTenantId = ...`), which in Postgres — no `FOR` clause means the policy applies to
**every** command, and `WITH CHECK` defaults to the same expression as `USING` when not given
separately — grants **both** tenants full read/write on those tables. That's correct there:
client creates the order, supplier updates its status, both are legitimate writers of the same
rows. It would be wrong here: reusing that exact pattern would silently hand the supplier tenant
INSERT/UPDATE/DELETE on the client's receipt data, which nothing in the product requirement calls
for and which this ADR explicitly rules out above. Postgres policies are additive/permissive by
command, so the fix is two named policies instead of one, each scoped with an explicit `FOR`:

```sql
ALTER TABLE marketplace_order_receipts ENABLE ROW LEVEL SECURITY;
ALTER TABLE marketplace_order_receipts FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON marketplace_order_receipts
  USING ("ClientTenantId" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
  WITH CHECK ("ClientTenantId" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);

CREATE POLICY supplier_read ON marketplace_order_receipts
  FOR SELECT
  USING ("SupplierTenantId" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);

CREATE POLICY provider_bypass ON marketplace_order_receipts
  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));

CREATE POLICY worker_bypass ON marketplace_order_receipts
  USING (current_setting('app.role', true) = 'worker');
```

Same four policies, same shape, on `marketplace_order_receipt_items` (substituting the table
name). `provider_bypass` uses the current `IN ('provider', 'provider_admin')` form from day one
(`20260714150000_ExpandProviderBypassToProviderAdmin` is the live source of truth per
`database-schema.md`'s own note — the file's top-of-doc RLS Template section predates that
migration and is stale on this point, don't copy it verbatim). **`worker_bypass` is mandatory,
not optional**, even though no worker cron job touches these tables today: the regression test
`RlsCrossTenantIntegrationTests.AllForceRlsTables_HaveTenantIsolationNullifGuard_
ProviderBypass_AndWorkerBypass` asserts, by exact policy name, that every `FORCE ROW LEVEL
SECURITY` table has all three — a migration that omits it fails that test outright, and per
project memory this is also the exact class of bug (`TASK-343`) that silently dropped worker
writes on other tables until someone noticed rows weren't being written.

### Decision 4 — Status transition: `AllowedTransitions` drops the `Shipped` key entirely; `MarketplaceOrderReceiptService` is the sole writer of `Status = Delivered`

Confirms the plan's mechanism exactly. Today `AllowedTransitions[Shipped] = [Delivered]` is the
**only** entry for `Shipped` — no other transition exists (no `Shipped → Cancelled` either).
Removing `Delivered` from it leaves an entry mapping to an empty array, which is behaviorally
identical to removing the key outright (`TryGetValue` fails either way against
`AllowedTransitions.TryGetValue(order.Status, out var allowed) && !allowed.Contains(...)`) — this
ADR directs **removing the key**, not leaving a dangling empty-array entry, for readability: a
missing key reads unambiguously as "no supplier-initiated transition exists from this status,"
where an empty-array entry invites a future reader to wonder if that's a bug.

**Effect on the existing supplier endpoint, confirmed unambiguous:** `POST
.../orders/{id}/status` with `status: "delivered"` now always falls through to the existing
generic `"Перехід зі статусу 'shipped' у 'delivered' неможливий."` error (400) — no new error
branch, no behavior change to the error path itself, just one fewer reachable transition. This
is exactly the "confirm the backend contract is unambiguous" bar the frontend-developer step
needs: **removing the Deliver button becomes purely a UI change on their side** — the button's
own POST would already 400 today the instant this migration+service change lands, whether or not
the button is removed in the same deploy. `MarketplaceOrderReceiptService` (new, not a method on
`MarketplaceOrderService`) is the only code path that ever sets `Status = Delivered` — its
finalize/receive method reads `order.Status` itself (must be exactly `Shipped`, checked
explicitly, the same way `ReceiptService.ReceiveAsync` checks `receipt.Status` itself rather than
going through any shared transition table) and writes `Status`/`DeliveredAt` directly via
`IMarketplaceOrderRepository`, entirely bypassing `AllowedTransitions`. No double-write path, no
ambiguity about which service "owns" the `Delivered` transition going forward.

One more consistency note for backend-developer: the client's own DB session already has native
RLS write access to `marketplace_orders` (the OR-based policy there, unchanged — see Decision 3's
explanation of why that table is different from the new ones), so **this finalize path needs no
`ITenantSessionOverride` cross-tenant hack**, unlike the supplier→client notification writes in
`EnqueueShippedNotificationAsync`/`SetDelayReasonAsync`. If a future iteration wants to notify the
*supplier* tenant that an order was received (not required by the current plan or this ADR — see
Consequences), that write direction (client session → `NotificationQueue` row targeting
`SupplierTenantId`) is the one that *would* need the override pattern, mirroring TASK-582/584/585
exactly.

### Decision 5 — Client-facing API contract sketch (backend-developer's build spec; also the mobile/Codex handoff shape)

**Controller placement:** extend `MarketplaceCooperationController.cs` with a new region, rather
than a new controller. It already carries the `[Authorize] [RequireModule("marketplace")]` class
gate and the `ResolveTenantId()`/`ResolveUserId()` helpers this flow needs verbatim — four more
actions don't justify duplicating that boilerplate in a new file, and it keeps every
client-facing marketplace endpoint (orders, agreements, receiving) discoverable in one place, the
same "one file per bounded conversation, not one per verb" shape the controller already has.

Route addressing is **order-centric throughout** (`orderId` in every path, never a separately
surfaced `receiptId`) — the receipt is 1:1 with its order (Decision 1's unique index), so mobile
never needs to learn or persist a second id after landing on an order's detail screen.

| # | Method + route | Request | Response | Errors |
|---|---|---|---|---|
| a | `GET /api/marketplace/orders/awaiting-receipt` | — | `200 IReadOnlyList<MarketplaceOrderDto>` (existing DTO, reused as-is — already carries `Items`/`ShippedAt`/`EstimatedDeliveryDays`, everything the list+detail screens need) — filtered server-side to caller's `ClientTenantId` + `Status == Shipped` | — |
| b | `POST /api/marketplace/orders/{orderId}/receipt` | — | `200/201 MarketplaceOrderReceiptDto` — **idempotent create-or-get**: if a draft already exists for this order, returns it instead of erroring (resumes an interrupted receiving session) | `404` order not found/not owned; `400` order.Status != Shipped; `400` order.DestinationStoreId is null (the Decision 2 historical-gap case) |
| c | `GET /api/marketplace/orders/{orderId}/receipt` | — | `200 MarketplaceOrderReceiptDto` — read-only, no side effects; this is also what the web read-only block (plan section 4) and the supplier cabinet's `supplier_read`-gated view call | `404` no receipt exists yet for this order |
| d | `PUT /api/marketplace/orders/{orderId}/receipt/items/{itemId}` | `UpdateMarketplaceOrderReceiptItemRequest { ProductId?, QuantityReceived?, ExpiryDate?, BatchNumber?, DiscrepancyNotes? }` | `200 MarketplaceOrderReceiptDto` | `404` receipt/item not found or not owned; `400` receipt already `received`; `400` `QuantityReceived < 0` |
| e | `POST /api/marketplace/orders/{orderId}/receipt/finalize` | — | `200 MarketplaceOrderReceiptDto` (`Status = received`) — gate gate: every item must have both `ProductId` **and** `ExpiryDate` set (extends `ReceiptService.ReceiveAsync`'s existing expiry-only gate with the new not-yet-scanned case), then creates one `ProductStock` (`SourceType = "marketplace_order_receipt"`, `SourceId = receipt.Id`, `StoreId = receipt.DestinationStoreId`) + one `StockMovement` (`ReferenceType = "marketplace_order_receipt"`, `ReferenceId = receipt.Id`) per item — field-for-field the same construction `ReceiptService.ReceiveAsync` already does, then sets `order.Status = Delivered`, `order.DeliveredAt = UtcNow` directly | `404` not found; `400` already received; `400` N item(s) missing product/expiry |

**One deliberate shape deviation from the `ReceiptsController` template, called out explicitly
per the brief:** `ReceiptsController`'s `PUT /{id}/items` updates **all** items in one bulk
payload (`UpdateItemsRequest.Items: []`). Endpoint (d) above is **per-item**, not bulk. Reason:
the mobile UX here is scan-one-commit-one (plan section 3, steps 3-4) — an employee resolves and
confirms exactly one physical item per scan, there is no "fill out a form for every line, submit
once" moment the bulk shape was built for. The request *field* shape still mirrors
`UpdateItemsRequest`'s per-item payload 1:1 (same four editable fields, same names) — only the
batching granularity changes, so it stays a familiar, easy-to-implement variant of the reference
pattern rather than a new one invented from scratch.

**Authorization:** reads (a, c) stay at the controller's existing class-level gate only (any
authenticated tenant user with the `marketplace` module — matches `GetMyOrders`'s existing
posture, no extra role check). Mutations (b, d, e) should require `AppPolicies.CanReceiveStock`
(storekeeper+) — the direct analog of `ReceiptsController`'s own choice for its equivalent
write actions, and consistent with "this creates real stock, gate it like every other stock-in
action in the system."

### Decision 6 — Barcode resolution: `GET /api/items/by-barcode/{code}` is sufficient as-is, zero new backend work

Confirmed by reading `ItemsController.cs` in full. The endpoint already: 404s cleanly on an
unknown code (`return product is null ? NotFound() : Ok(product)`); sits under the controller's
class-level `[Authorize(Policy = AppPolicies.CanViewStock)]`, the same JWT-bearer auth every
other mobile stock/POS/write-off screen already uses — no new auth wiring needed for a mobile
caller; and is tenant-scoped implicitly through `items` table RLS + `TenantConnectionInterceptor`,
so it can never resolve a barcode into another tenant's catalog row. Nothing about the new
receiving flow requires touching this file.

Consequences:
+ One new, cleanly-scoped entity pair with its own RLS policy set — `StockReceipt`'s existing,
  tested single-tenant contract is untouched, and `marketplace_orders`/`marketplace_order_items`'
  existing bidirectional-write RLS is untouched (Decision 3 only adds new tables, doesn't modify
  either existing policy).
+ The `Delivered` transition has exactly one writer after this ships
  (`MarketplaceOrderReceiptService`), removing the current "supplier can lie about delivery"
  gap entirely — this was the point of the whole feature.
+ `DestinationStoreId` nullable-at-DB / required-at-API-boundary-for-new-orders follows an
  already-precedented pattern in this codebase (ADR-017 point 5) rather than inventing a new one.
- **Flagged, not resolved here — a required pre-deploy check for database-engineer/
  backend-developer, not this ADR:** any `MarketplaceOrder` already sitting in `Status = 'shipped'`
  the moment this migration lands, with `DestinationStoreId IS NULL` (true for every order placed
  before this ships, since the column didn't exist), becomes **permanently un-receivable through
  the new flow** the instant the supplier's self-service `Delivered` path is removed — there is no
  `ProductStock` without a `StoreId`, and nothing backfills one automatically. Before removing the
  `Shipped → Delivered` supplier transition, run:
  ```sql
  SELECT id, order_number, client_tenant_id
  FROM marketplace_orders
  WHERE status = 'shipped' AND destination_store_id IS NULL;
  ```
  against prod. This ADR could not run that query itself (read-only, code/task-log investigation
  only, per this session's scope) — task-log evidence points to the blast radius being small: the
  order-shipping lifecycle itself is brand new (`ShippedAt`/`EstimatedDeliveryDays` landed
  2026-08-20 via TASK-584, same day as `DelayReason`/TASK-585; TASK-359's 2026-07-15 audit of this
  feature area recorded no order-volume figures at all), so this is very likely zero or a
  small handful of rows — but "likely" is not "confirmed," and the fix if the query returns any
  rows is cheap (one manual `UPDATE` per affected tenant's actual delivery store, not a generic
  migration script) — do this check before merging, not after.
- Discrepancy handling stays **non-blocking** (`DiscrepancyNotes` is informational only,
  `QuantityReceived != QuantityOrdered` never blocks finalize) — this ADR agrees with the plan.
  It's the same posture already shipped and working for `StockReceiptItem`, and inventing an
  approval/hold workflow for marketplace-specific discrepancies is out of proportion to what was
  asked for; if a future need for blocking discrepancy review emerges it should be its own ADR,
  not retrofitted here.
- No "order received" notification to the supplier tenant is part of this design — the plan only
  asked for a **read-only** supplier-cabinet display after `Delivered` (Decision 3), not a
  push/outbox notification. Noted as a documented extension point (Decision 4), not built.
- New audit-test surface: the two new tables must appear correctly in
  `AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass`'s scan the
  moment their migration lands (it queries `pg_policies`/`pg_class` directly, no code change
  needed on the test's side) — database-engineer should run that test locally against the new
  migration before considering it done, not just the migration's own `Up()`/`Down()` symmetry.

Rejected alternatives:
- **Extending `StockReceipt`/`StockReceiptItem` directly** — see Decision 1 (nullable-`ProductId`
  precedent risk to an already-tested table, incompatible `SupplierId` meaning, and the new
  cross-tenant-read requirement this ADR adds as a third reason).
- **EXISTS-through-parent RLS for `marketplace_order_receipt_items`** (the codebase's other,
  more common child-table pattern, e.g. `supplier_support_ticket_messages`) — rejected in favor
  of denormalized `ClientTenantId`/`SupplierTenantId` columns, matching the sibling
  `MarketplaceOrderItem`'s own precedent within this exact feature area, and needed anyway for the
  item-level `supplier_read` policy without a join.
- **A `"cancelled"` receipt status** — out of v1 scope; nothing in the plan calls for abandoning
  an in-progress receiving session, and a stuck `"draft"` row is harmless (the order stays
  `Shipped`, re-opening the draft via endpoint (b) just resumes it).
- **Bulk item-update endpoint** (copying `ReceiptsController`'s exact shape) — rejected for
  endpoint (d); see Decision 5's called-out deviation.

Task breakdown: TASK-586 (this ADR). Schema/RLS (database-engineer), service+endpoints+tests
(backend-developer), and the web read-only display + Deliver-button removal (frontend-developer)
are separate follow-up sessions per the approved plan — task numbers to be assigned by
project-manager. Mobile handoff: `.claude/logs/handoffs/586-to-mobile-codex_project-architect.md`
(to be written once backend-developer's actual DTO field names are final, per the plan's own
sequencing — this ADR's Decision 5 sketch is the contract backend-developer builds against, not
a substitute for that final handoff doc).

## ADR-032: Catalog curation — new `productIds` block-prop kind, curated-selection resolution semantics, and a catalog-by-ids read path to keep it correct at scale
Date: 2026-08-19
Status: accepted

Context: Retailer admins reported the consumer app's "Каталог" tab shows every active SKU
uncurated — "не всі позиції потрібно показувати, а ті, які є актуальними для продажу." This is
**Phase 1 of a larger, deliberately-descoped ask**: the user also raised bestsellers, personalized
recommendations, personalized discounts, and a POS-payment bonus in the same message, then
explicitly chose to scope this round to catalog curation only and defer the rest to a separate
future initiative — none of that is designed or placeholder-scaffolded here.

Page-level curation already works today with zero changes: `mobile/app/(personal)/catalog.tsx`
already renders the block-driven `ConfiguredRetailPage` when the admin has configured the
"catalog" page in App Builder, falling back to the uncurated `StaticConsumerCatalogScreen` only
when no blocks exist. **The actual gap is one level down**: `productGrid`/`productCarousel`
(`BlockRegistry.cs`) only have a `limit` (int) prop — both `resolveBlocks.ts` (mobile) and
`blockPreviews.tsx` (web preview) resolve them as `ctx.catalog.slice(0, limit)`, and
`ConsumerContentRepository.GetCatalogPagedAsync` orders by `i.Name` — so a block can only ever show
"the first N products alphabetically," never a deliberately-chosen set. Scope boundary (unchanged
from the brief, not re-litigated here): only `productGrid`/`productCarousel` gain curation —
`promotionGrid`/`promotionCarousel` already pull from `ctx.promotions` (items with an active
`Discount`), a data source that is inherently curated by a different mechanism, out of scope.

### Decision 1 — new `BlockPropTypes.ProductIds` kind, not a `stringArray` + name special-case

`BlockPropertyEditor.tsx`'s `PropField` (frontend) switches only on `def.type` — six cases matching
`BlockPropTypes.cs` — and states explicitly in its own comment that this is "the sole switch in the
file; there is deliberately no branch anywhere on the block's own `type`," so a new block type
renders with zero changes to that file as long as its props use the six existing kinds. The existing
`stringArray` type's UI (`StringArrayField`) has exactly two modes: a fixed-`AllowedValues`
badge-picker (used by `quickActions.actions`, a small closed set known at compile time), or a raw
free-text tag input. Neither fits "search and pick from a tenant's live catalog, potentially
thousands of SKUs, by name, with a thumbnail" — the value set here is dynamic, per-tenant, and
requires an async lookup, not a static list.

**Decision: add `BlockPropTypes.ProductIds = "productIds"`, a 7th kind.** Reusing `stringArray` and
special-casing the new UI by prop **name** (`if (def.name === "productIds")`) would violate this
file's own stated single-switch-on-type invariant and set a precedent for name-based branching to
keep multiplying as later curation-adjacent features (bestsellers, etc. — deferred, but the pattern
would recur) arrive. The cost of a real 7th kind is small and contained: one `BlockPropTypes.cs`
constant, one `stringArrayFieldSchema`-reuse case each in `fieldSchemaFor`/`coerceValue`, one new
`ProductPickerField` component wired into `PropField`'s switch. `MinItems`/`MaxItems` (already on
`BlockPropDefinition`) bound the selection count exactly like they already bound `quickActions`;
`AllowedValues` is left `null` — the point of this kind is precisely that the valid set isn't static.

`productCarousel.productIds`: `Required: false, Default: [], MinItems: 0, MaxItems: 20` (matches
`productCarousel.limit`'s own `Max: 20` — an admin can never usefully pick more than could ever
display). `productGrid.productIds`: same shape, `MaxItems: 30` (matches `productGrid.limit`'s
`Max: 30`). `MobileConfigValidator`/`MobileConfigWhitelists` are **not** touched — block `props`
stays free-form JSON at save-time by this registry's own already-documented, already-tested
decision (`BlockRegistry.cs`'s class remarks); adding a registry entry only changes what
`GET /api/v1/mobile/blocks` advertises, exactly the same boundary ADR-031/TASK-561 already drew.

### Decision 2 — resolution semantics: curated selection overrides the alphabetical fallback; `limit` becomes a cap, not a page-size driver

Both `resolveBlocks.ts` (mobile) and `blockPreviews.tsx` (web preview) must implement **identical**
logic (ADR-031's "preview must never lie about what the real app shows" carries over unchanged):

1. Read `props.productIds` (array of strings, defensively filtered), default `[]`.
2. **If non-empty:** resolve items by walking `productIds` *in the admin's chosen order* (order of
   selection in the picker is authoritative display order — no separate `order` field), looking each
   id up in the available catalog data. Skip an id silently if it doesn't resolve, or if it resolves
   but `priceRetail === null` (the same "not sellable, don't show" filter the existing fallback
   branch already applies to every item). Then `.slice(0, limit)` — `limit`'s existing prop/bounds
   are unchanged in meaning-adjacent-but-different: with a curated selection present, it caps the
   curated list's length rather than driving which page of the catalog gets fetched.
3. **If empty/absent:** fall back to **exactly today's behavior**, byte-for-byte —
   `ctx.catalog.filter(item => item.priceRetail !== null).slice(0, limit)`, alphabetical-first-N.
   Every already-saved block (no `productIds` in its `props`) renders identically to today; this is
   a strictly additive capability, never a replacement of `limit`.

**Stale/deleted product handling: silently skip, no placeholder.** A selected product can later be
deactivated (`Item.IsActive = false`) or hard-deleted; the block should just render as if it was
never selected. Precedent, not invention: every other read path in this feature area already treats
a referenced-but-now-invalid row the same way — `GetCatalogPagedAsync`'s own `IsActive` filter
silently excludes deactivated items from the general browse, and neither the banner nor promotion
read paths ever render a "this item no longer exists" placeholder anywhere in the consumer app.
Curated selections get the same treatment for consistency, not a new UX pattern.

### Decision 3 — a real correctness gap this feature would otherwise ship with: catalog fetches are capped short of "the whole tenant catalog," on both sides

This is the equivalent of ADR-031's "prop-forwarding gap" finding — a concrete bug the brief didn't
ask about but that would make curation *silently unreliable*, not just incomplete, for exactly the
retailers who need it (large catalogs):

- **Mobile:** `PageRenderer.tsx` calls `useConsumerCatalog(context, { page: 1, pageSize: 30 })` —
  hardcoded. `ctx.catalog` (what `resolveBlocks.ts` reads) only ever contains the first 30 active
  items **alphabetically**. A curated pick outside that window would resolve as "not found" and
  silently vanish — not because it's stale, but because it was never fetched. Any tenant with >30
  active SKUs (the overwhelmingly common case for a feature whose entire purpose is picking specific
  SKUs out of a big catalog) hits this immediately.
- **Web preview:** `AppPreviewPanel.tsx` maps `useCatalogProducts()` → `/api/items` (admin catalog),
  which defaults to `pageSize=50` with no way to reach page 2 from this screen and no `search`/`ids`
  filter at all today. Same failure mode, different cap.

**Decision: add a bounded "fetch by exact id list" read path on both sides**, used only when a page
actually has a curated selection (zero added requests for every page that doesn't):
- New `IConsumerContentRepository.GetCatalogByIdsAsync(tenantId, storeId, ids, ct)` +
  `GET /api/consumer/{tenantId}/catalog/by-ids?storeId=&ids=...` (same DTO shape, `IsActive` filter,
  and store-availability annotation as `GetCatalogPagedAsync`, bounded to ≤30 ids — the larger of
  the two `MaxItems` values). `PageRenderer.tsx` unions every `productIds` referenced on the current
  page, fetches this endpoint only when that set is non-empty, and merges the result into a new
  `catalogById: Map<string, ConsumerCatalogItem>` passed alongside the existing (unchanged)
  `catalog` array — the alphabetical-fallback branch keeps reading `catalog` exactly as before; only
  the curated-lookup branch reads `catalogById`. This is also the natural place to give
  `/api/items` (admin) a `search` (name `ILike`, mirroring `GetCatalogPagedAsync`'s own existing
  pattern) and `ids` filter — `search` is what makes a "search among thousands of SKUs by name"
  picker viable at all (today `/api/items` has no text search of any kind), and `ids` gives
  `AppPreviewPanel.tsx` the same by-id resolution the mobile side needs, via one shared endpoint
  change instead of two different mechanisms.

### Decision 4 — no new reusable component exists for "pick several products by name"; build one, scoped narrowly

`PromoProductsSection.tsx`'s product field is a single-select native `<select>` bound to
`useCatalogProducts()` — not a multi-select, no search. A new `ProductPickerField.tsx` (debounced
name search via the new `search` param, thumbnail+name+price result rows, ordered selected-chips
list, respects `MaxItems`) is genuinely new work, wired into `BlockPropertyEditor.tsx`'s `PropField`
switch as the `productIds` case. Selection order = display order; no drag-reorder in this phase
(remove-and-re-add covers reordering) — kept out to match this feature's own "Phase 1, deliberately
descoped" framing rather than gold-plating the picker beyond what curation needs.

Decision: Approved as scoped. Task breakdown TASK-571..576 (`.claude/logs/tasks/
570_2026-08-19_catalog-curation-architecture_project-architect.md`).

Consequences:
- `BlockPropTypes.cs` grows from 6 to 7 kinds; every place that already switches exhaustively on
  type (`BlockPropertyEditor.tsx`'s `fieldSchemaFor`/`coerceValue`/`PropField`, this codebase's only
  three such switches) gains one case each — small, additive, no restructuring.
- `/api/items` (admin) gains two optional query params (`search`, `ids`) with no behavior change
  when neither is passed — existing callers (`PromoProductsSection.tsx`, any other consumer of
  `useCatalogProducts()`) are unaffected.
- A new anonymous, feature-gated consumer endpoint (`catalog/by-ids`) is added alongside the existing
  paginated one — same auth posture, same `[RequireConsumerFeature("catalog")]` gate, no new feature
  flag.
- `architecture.md` is **not** updated — same reasoning as ADR-031: no layer/module/service boundary
  change, this is read-path and prop-schema work inside the already-documented MobileConfig/
  ConsumerContent/Catalog features. `domain-model.md`'s Block Registry section gets a short addition
  for the new prop kind and the two new curated props (data-model-level change); see that file.
- Bestsellers, personalization, personal discounts, and the POS-payment bonus remain fully
  undesigned — no field, prop, or endpoint here anticipates or scaffolds any of them.

## ADR-031: App Builder live preview — web-native mirror components (not RN-web reuse), entirely client-side, 4 new resizable size props on the Block Registry
Date: 2026-08-19
Status: accepted

Context: Retailer Admin's App Builder (`/consumer-app/pages`, `AppBuilderCanvas.tsx`, TASK-539/541)
lets a tenant admin add/remove/reorder blocks and edit their props (`BlockPropertyEditor.tsx`,
TASK-540), but has no visual feedback — the only way to see the result is to save the draft and
open the real mobile app. Product decision (same day, follow-on to today's removal of the
mobile-side draft-preview screen — `docs/mobile/STAGE_17_REPORT.md`, `STAGE_18_REPORT.md`): draft
preview is a staff/web-admin-only capability, and it should be *instant* (no save round-trip),
Elementor-style — add/remove/reorder and property edits reflected live, plus a genuinely new
resize control for the 4 block types with a currently-fixed, currently-unconfigurable visual
dimension.

Two implementation choices were fixed by the user ahead of this ADR; both are evaluated and
confirmed below rather than re-litigated:

**1. Rendering approach: web-native mirror components, not react-native-web.** `AppBuilderCanvas.tsx`
already holds the entire draft `MobileConfigDocument` in React state pre-save (TASK-539's
read-modify-write design) — the enabler for a true pre-save live preview. The question is only how
to *render* it. Reusing `mobile/features/server-driven-ui/blocks/CoreBlocks.tsx`'s actual React
Native components server-side-rendered into the Next.js admin would require adding
react-native-web/NativeWind-web to the frontend bundle and stubbing every RN-only API the blocks (or
their transitive imports, e.g. `expo-router`'s `useRouter`, `Ionicons`, `SafeAreaView`-adjacent
layout) touch — `StoreListBlock` alone pulls in `expo-router`, `@expo/vector-icons`, and two loyalty
hooks. That is a permanent, ongoing maintenance surface for a feature whose only job is an
approximate visual preview. **Confirmed: build new plain React/inline-style "mirror" components in
`frontend/features/consumer-app/`, one per block type, matching CoreBlocks.tsx's exact proportions
(280/210/170px carousel cards, 190px hero minHeight, 48%/31% grid widths) rather than reusing RN
components.** This is the same boundary `ThemeEditorSection.tsx`'s existing `ThemePreview` already
drew for the theme mockup ("a static mock, not the real mobile block renderer") — this ADR extends
that same boundary to block-level content, still deliberately approximate, not pixel-perfect.

**2. Resize scope: a new prop, not just live-reflection of existing props.** Confirmed scoping,
narrowed to the 4 block types with a real fixed dimension and no prop for it today —
`CoreBlocks.tsx` hardcodes `heroBanner` minHeight (190), and carousel card widths for
`bannerCarousel`/`promotionCarousel`/`productCarousel` (280/210/170, fixed, not prop-driven).
`promotionGrid`/`productGrid` already have a resizable `columns` prop (2 or 3) wired end-to-end —
left untouched, out of scope. The other 6 types (loyaltyCard, loyaltyBalance, sectionHeader,
quickActions, newsList, storeList) are content-list/fixed-layout blocks with no single meaningful
size dimension — no new prop for them; their preview still updates live for content/order changes.

New `BlockPropDefinition` entries (`BlockRegistry.cs`), each `int`, optional, bounds bracketing
today's hardcoded value so the default *is* the exact current visual (no jump on first render of an
old saved config):

| Block type | Prop | Default | Min | Max |
|---|---|---|---|---|
| `heroBanner` | `heightPx` | 190 | 120 | 260 |
| `bannerCarousel` | `cardWidthPx` | 280 | 200 | 360 |
| `promotionCarousel` | `cardWidthPx` | 210 | 150 | 270 |
| `productCarousel` | `cardWidthPx` | 170 | 120 | 220 |

Card *image height* inside each carousel card (130px for banners, 120px default for
promotion/product cards) is deliberately **not** tied to the new width prop — resizing width only
avoids a cascading aspect-ratio recompute this task doesn't need to solve.

**A concrete correctness finding that shapes the mobile task:** `mobile/features/server-driven-ui/
resolveBlocks.ts`'s `resolveBlock()` *rebuilds* the props object for `bannerCarousel`,
`promotionCarousel`/`promotionGrid`, and `productCarousel`/`productGrid` (`return { ...block, props:
{ items: ... } }` etc.) — any static authored prop not explicitly listed in that literal is silently
dropped before it reaches `CoreBlocks.tsx`. `heroBanner` has no `case` in that switch (falls through
to `default: return block`, unchanged) so its new `heightPx` passes through for free — but the 3
carousel types do **not** get `cardWidthPx` for free; `resolveBlocks.ts` must explicitly forward it
in each of those 3 `case` blocks or the new prop silently no-ops on real devices while still
"working" in the web preview. Called out explicitly so the mobile task doesn't ship a preview that
lies.

No backend endpoint changes: `MobileConfigValidator` already treats block `props` as free-form JSON
(container-type-checked only, see `domain-model.md`'s Block Registry section) — adding registry
entries is purely additive, no validator/whitelist change. The preview itself introduces **zero new
backend endpoints** — it is 100% client-side, computed from documents/data the admin already has
access to (`useMobileConfigDraft`'s in-memory `configDoc`, plus existing `useBanners`/
`usePromoProducts`/`useCatalogProducts`/`useLocations`/`useMobileTheme` reads, all already gated
`AtLeastEnterpriseAdmin`+ on their own controllers). `MobileConfigPreviewController`/
`MobileConfigPreviewService` (TASK-547, reads the last *saved* draft from DB) are unrelated to this
feature and are not used by it — the web admin's in-memory `configDoc` is strictly richer
(pre-save) than what that endpoint could ever return.

Decision: Approved as scoped by the user; task breakdown TASK-561..566 (`.claude/logs/tasks/
560_2026-08-19_app-builder-live-preview-architecture_project-architect.md`).

Consequences:
- Preview content for 4 of 12 block types needs an admin-side data source with no 1:1 "the real
  consumer read" equivalent: `promotionCarousel`/`promotionGrid` need a `storeId` (`usePromoProducts`
  is store-scoped) that the App Builder screen has no selector for — resolved by silently using the
  tenant's first `useLocations()` result for preview purposes only (a preview-only convenience, not
  a real store-selection UI). `loyaltyCard`/`loyaltyBalance` render clearly-labeled sample data (an
  admin has no consumer session to read real balance from). `newsList` mirrors mobile's own current
  (interim) behavior of reusing banner data — matching what mobile *actually* renders today rather
  than inventing a different, more-honest-looking placeholder that would make the preview lie.
- Two client-side prop catalogs (web mirror components' expected shapes vs. mobile's resolved
  `BlockComponentProps<T>` shapes) must be kept conceptually aligned by hand, same manual-mirroring
  convention this feature already accepted for `MOBILE_CONFIG_BLOCK_TYPES`/`THEME_*` (no shared
  package between `frontend/` and `mobile/`).
- `architecture.md` is **not** updated by this ADR — no layer/module boundary changes, no new
  service, no new endpoint; this is UI work inside the already-documented MobileConfig/Consumer App
  module. `domain-model.md`'s existing Block Registry section gets a short addition for the 4 new
  props (data-model-level change); see that file.

## ADR-030: SubscriptionPlan → Features — `Tenant.Plan` gates consumer features through the existing TASK-543 flag hook; no billing, no enforcement yet
Date: 2026-08-19
Status: accepted

Context: `docs/architecture/TARGET_ARCHITECTURE.md` §2 ЕТАП 18 asks for a "subscription-ready
feature architecture" — `START`/`BUSINESS`/`PRO`/`ENTERPRISE` tiers gating which consumer-app
features a tenant may enable, explicitly "no billing." TASK-543 (Stage D) already built
`IConsumerFeatureFlagService`/`ConsumerFeatureFlagService` (`backend/ShelfGuard.Application/
Features/MobileConfig/{IConsumerFeatureFlagService,ConsumerFeatureFlagService}.cs`), the real
mechanism that resolves each of `MobileConfigWhitelists.FeatureKeys` (`loyalty`, `promotions`,
`catalog`, `coupons`, `news`, `receipts`, `delivery`, `personalOffers`) from a tenant's published
`MobileConfigurationVersion`, defaulting fail-open (enabled) until a tenant explicitly disables a
key. As part of that same task, `ISubscriptionPlanFeatureGate`/`SubscriptionPlanFeatureGate`
(same directory) was added as an explicitly documented no-enforcement placeholder: it reads and
returns `Tenant.Plan` and nothing else. `ConsumerFeatureFlagService` never calls it, no endpoint
is denied based on its result, and its own XML doc says so in plain terms — it exists "purely as
a documented, already-wired seam" for whoever implements ЕТАП 18 for real. TASK-555 (this task) is
the last registered task of the entire Stage 6 initiative (TASK-527–555, Stages A–F complete) and
was scoped to formalize that future architecture and confirm the seam holds, not to build the
enforcement itself.

Decision:

1. **Target architecture (not yet built):** `Tenant.Plan` → a plan→features mapping (does not
   exist yet — no table, no static dictionary) → constrains which of
   `MobileConfigWhitelists.FeatureKeys` a tenant's published configuration is allowed to enable →
   enforced inside `ConsumerFeatureFlagService.IsEnabledAsync`, by combining today's
   config-driven result with a plan-driven `AND`: a flag reads as enabled only if the published
   document says so *and* the tenant's plan permits that key. `ISubscriptionPlanFeatureGate`
   supplies the plan; `IConsumerFeatureFlagService` remains the single call site
   `RequireConsumerFeatureAttribute` and the consumer controllers depend on, so no caller-facing
   contract change is implied by wiring this in later.

2. **Confirmed: TASK-543's `ISubscriptionPlanFeatureGate`/`SubscriptionPlanFeatureGate` already
   satisfies the ЕТАП 18 hook as built — verified by reading the code, not assumed.** Both types
   exist, both are DI-registered (`ShelfGuard.Application/DependencyInjection.cs:170-171`,
   `AddScoped<IConsumerFeatureFlagService, ConsumerFeatureFlagService>` /
   `AddScoped<ISubscriptionPlanFeatureGate, SubscriptionPlanFeatureGate>`), and
   `GetTenantPlanAsync` already round-trips through `ITenantRepository` to the real `Tenant.Plan`
   column — this is a live, working read path today, not a stub that still needs writing. A
   future implementer's job is additive only: define the plan→features mapping, inject
   `ISubscriptionPlanFeatureGate` into `ConsumerFeatureFlagService` (or a decorator around it),
   and fold its result into `IsEnabledAsync`'s existing return. No interface signature changes,
   no new DI wiring, no controller/attribute changes, and no rework of `ConsumerFeatureFlagService`
   callers are needed to add real enforcement later — the seam is clean.

3. **Open reconciliation item — deliberately not resolved here.** `Tenant.UpdatePlan`
   (`backend/ShelfGuard.Domain/Entities/Tenant.cs:41-49`) only accepts
   `basic`/`standard`/`enterprise`/`trial` (case-insensitive, lowercased on write). ЕТАП 18 in
   `docs/CLAUDE CODE SPEC — Web Admin, App Builder & Backend.md` names the tiers
   `START`/`BUSINESS`/`PRO`/`ENTERPRISE`. These two vocabularies do not line up — there is no
   1:1 mapping implied anywhere in the code or spec today (e.g. it is not obvious whether `basic`
   ≈ `START`, whether `enterprise` on both sides means the same tier, or how many ShelfGuard plans
   collapse into how many spec tiers). Whoever schedules real plan-gating must explicitly choose
   one of: (a) remap `Tenant.Plan`'s valid values and every existing row to the spec's tier names,
   or (b) keep `Tenant.Plan` as-is and add an explicit translation layer (a small
   `PlanTier`-lookup) between `Tenant.Plan` and the plan→features mapping's keys. This is a
   product/naming decision, not an implementation detail — recorded here so it is not
   rediscovered from scratch, and not silently guessed at by whichever task picks it up.

4. **No billing/payment implementation is in scope now, or implied by this ADR.** ЕТАП 18 itself
   is explicit that this stage is feature-gating architecture only ("no billing"). This ADR
   documents a target read-path for an existing field; it does not introduce plan purchase,
   upgrade/downgrade flows, payment provider integration, invoicing, or any billing UI. Nothing in
   this decision requires touching payments to be useful — plan values can continue to be set
   the way they are today (provider/admin-set `Tenant.Plan`, per `Tenant.UpdatePlan`'s existing
   authorization) with real feature-gating layered on top whenever that follow-up task is
   scheduled.

Consequences:
+ Confirms Stage 6 closes with zero rework debt on this hook — TASK-543 already built the correct
  seam, so ЕТАП 18 implementation later is additive (mapping + one `AND` in
  `IsEnabledAsync`), not a redesign
+ The plan-naming mismatch is now a citable, explicit open item instead of a landmine a future
  task would discover mid-implementation
+ `Tenant.Plan` continues to mean exactly what it means today (billing-adjacent tenant metadata,
  set by provider/admin) until a follow-up task explicitly decides to enforce it — no behavior
  changes ship from this ADR
- Real feature-gating remains fully unenforced until that follow-up task is scheduled and the
  naming reconciliation (point 3) is resolved — this ADR intentionally leaves both undone
- Whoever eventually implements ЕТАП 18 must resolve the naming mismatch *before* writing the
  plan→features mapping, or risk keying that mapping on the wrong vocabulary from day one

See: `backend/ShelfGuard.Application/Features/MobileConfig/{ISubscriptionPlanFeatureGate,
SubscriptionPlanFeatureGate,IConsumerFeatureFlagService,ConsumerFeatureFlagService,
MobileConfigWhitelists}.cs`, `backend/ShelfGuard.Domain/Entities/Tenant.cs:41-49`,
`docs/architecture/TARGET_ARCHITECTURE.md` §2 row 18, `docs/architecture/CURRENT_STATE.md` §1,
`docs/CLAUDE CODE SPEC — Web Admin, App Builder & Backend.md` (ЕТАП 18 / START/BUSINESS/PRO/
ENTERPRISE), `.claude/tasks/mobile-roadmap.md` TASK-543, TASK-555.

## ADR-029: Consumer-platform "Tenant" = existing `tenants` table; `UserTenant` has no shipped generic equivalent yet
Date: 2026-08-17
Status: accepted (point 1) / open (point 2 — recorded as a decision to make explicitly, not to
guess)

Context: three new spec files appeared in `docs/` without any prior formal spec ever existing —
`MASTER SPEC — Multi-Tenant Retail & Loyalty Platform.md`, `CLAUDE CODE SPEC — Web Admin, App
Builder & Backend.md`, `CODEX SPEC — Mobile Application.md` — describing one shared consumer
mobile app where a customer joins multiple retailers (`tenantId`), each with its own server-driven
theme/navigation/content. TASK-526 (Stage 6, `.claude/tasks/mobile-roadmap.md`) audited the
backend/web-admin side against this target and produced `docs/architecture/CURRENT_STATE.md` /
`docs/architecture/TARGET_ARCHITECTURE.md`. This is not a greenfield start: `eaacfa7d`
(`MobileAuthController`, `ConsumerAccount`), `29ec2fd4`/`4fa15f7d` (universal cross-tenant loyalty
code), `075af2f9`/`9acf6ff5`/`db7c5d40` (network catalogue, preferred store), and
`0dccb0d9`/`2cff57e5`/`c17a772c`/`72e33308`/`7208f89f` (banners with draft/publish, promo products,
catalog admin) already implement large pieces of this target model, without the spec having
existed yet to name them consistently. The critical open question the audit had to resolve with
evidence, not assumption: does the spec's "Tenant" map onto ShelfGuard's existing B2B `tenants`
table, or does it imply a new, parallel entity?

Decision:

1. **The spec's "Tenant" is ShelfGuard's existing `tenants` table — confirmed, not a new entity.**
   Evidence, not inference: `Tenant.cs`'s existing `Id`/`Name`/`Slug`/`CreatedAt` already satisfy
   the spec's own minimal ЕТАП 1 model (only `LogoUrl`/`UpdatedAt` are missing — additive, not
   structural). `TenantConnectionInterceptor` already sets `app.tenant_id` from the exact same JWT
   claim every pre-existing tenant-scoped feature reads, and the canonical RLS
   `tenant_isolation`/`provider_bypass`/`worker_bypass` triad (`database-schema.md`) already
   enforces the spec's ЕТАП 4 "Tenant A cannot read/write Tenant B data" requirement for every
   tenant table in the schema. Most directly: `Banner` — the newest tenant-scoped entity in the
   codebase, added for this exact initiative *before* the spec existed — was given a plain
   `TenantId` FK to `tenants` with the same RLS shape as every older table, not a new isolation
   model invented for "the consumer platform." Building a second, parallel `Retailer`/`Tenant`
   entity for the consumer-facing side would duplicate the entire existing RLS/isolation
   infrastructure for no isolation benefit and would fork "which tenant a user belongs to" into two
   incompatible answers depending on which app asked. `MobileConfiguration.TenantId`,
   `MobileTheme.TenantId`, and every future consumer-platform entity should FK straight to the
   existing `tenants` table, exactly like `Banner` already does.

2. **The spec's `UserTenant` (MASTER SPEC §14 — a generic "consumer joined this retailer" row,
   with a separate tenant-specific `LoyaltyAccount` hanging off it) has no shipped equivalent
   independent of loyalty — left as an open decision, not silently resolved either way.**
   `ConsumerAccount` (ADR-023) is confirmed as the spec's global "User" — no `TenantId`, no RLS,
   one JWT reading across every tenant it holds a relationship with, same shape the spec asks for.
   But the only existing join mechanism, `LoyaltyMembership`, conflates "joined this retailer" with
   "enrolled in this retailer's bonus program" into one entity: `POST
   /api/consumer/loyalty/{tenantId}/join` **is** the only join action that exists, and retailer
   discovery (`LoyaltyService.GetAvailableNetworksAsync`) only lists tenants with
   `HasModule("loyalty")` enabled and `LoyaltyProgramSettings.IsEnabled`. A tenant that wanted a
   consumer-app presence (banners, catalog, theme) without running a bonus program structurally
   cannot appear in retailer discovery today. Two shapes were identified for closing this gap
   (`TARGET_ARCHITECTURE.md` §3, open decision #1): keep the coupling as-is (cheaper, matches what
   already shipped, but permanently ties "consumer app membership" to "loyalty module"), or
   introduce a genuinely generic `ConsumerTenantMembership`/`UserTenant` that `LoyaltyMembership`
   optionally extends (matches the spec literally, touches every existing
   join/discovery/preferred-store call site). Deliberately NOT decided here — this is a
   product/architecture tradeoff about what "joining a retailer" is allowed to mean, not an
   implementation detail project-architect should pick unilaterally. Recorded so no future agent
   assumes `LoyaltyMembership` already *is* the spec's `UserTenant` by another name.

Consequences:
+ Zero new isolation infrastructure needed for the consumer platform — it inherits the existing,
  already-audited RLS triad and `TenantConnectionInterceptor` mechanism verbatim, the same way
  `Banner` already does
+ `MobileConfiguration`/`MobileTheme`/future App Builder entities have an unambiguous, already-
  precedented FK target (`tenants.Id`) from day one — no risk of a later, costly identity merge
  between "two kinds of tenant"
+ The `UserTenant` question is now explicit and citable (`TARGET_ARCHITECTURE.md` §3 open decision
  #1) instead of being silently decided by whichever future task happens to touch it first
- Until decision #2 is made, retailer discovery/join work (ЕТАП 2/14) cannot start — this is an
  accepted blocking dependency, not an oversight
- A tenant without the `loyalty` module currently has no path to a consumer-app presence at all;
  this is the concrete, load-bearing cost of leaving decision #2 open rather than a hypothetical one

See: `docs/architecture/CURRENT_STATE.md` §1/§3, `docs/architecture/TARGET_ARCHITECTURE.md` §1/§3
(TASK-526 full audit and proposed follow-up task breakdown, not yet registered in
`.claude/tasks/mobile-roadmap.md`).

## ADR-028: KI-033 fix — `IAnalyticsRlsOverride` + a dedicated `marketing_analytics_bypass` role value, narrowing `pos_transactions.store_scope` for one already-authorized read path
Date: 2026-08-11
Status: accepted — implemented and verified (TASK-509 implementation, TASK-510 security review:
SHIP/0 blocking findings, TASK-511 independent QA re-verification: byte-identical to the
RLS-exempt baseline for the originally-affected account; all 2026-08-11). See KI-033 in
`known-issues.md` for the closed-out status and the network_manager/KI-031 side-effect nuance.

Context: KI-033 (`.claude/docs/known-issues.md`, found by TASK-504 QA of the store-migration
feature, full repro in `.claude/logs/handoffs/504-to-backend_qa-tester.md`). `pos_transactions`'
RESTRICTIVE `store_scope` policy (`20260719193545_AddLocationStoreScopeRlsPolicies.cs`, ADR-022
Stage 3) only admits a row when the caller's role is in `('provider', 'provider_admin', 'worker',
'enterprise_admin')` OR the caller has a `user_locations` grant for that row's `LocationId`. This
is *correct* for every store-scoped operational table/read path — ADR-022's whole point was giving
`store_manager`/`network_manager` "only your own store" visibility on 9 tables. It is *wrong* for
`MarketingAnalyticsRepository` specifically: every RFM/store-migration query's entire premise is a
tenant-wide comparison (e.g. "did this customer's first/last purchase move between stores"), so
scoping it to the caller's granted subset doesn't just undercount — for store-migration it
silently **reclassifies** a genuinely-migrated customer as "not migrated" when their true
first/last transaction sits at a store the caller isn't granted (live-reproduced, see the handoff
above). This is debt the whole `MarketingAnalyticsController` already had (confirmed on the
pre-existing RFM overview endpoint too, not just the new store-migration one) — any caller whose
role isn't in that bypass list (i.e. `store_manager`/`network_manager`, this module's actual
target users; the frontend already trusts `store_manager`+ to export unmasked PII from it) gets
confidently wrong analytics with no partial-data signal anywhere in the response.

Every entry point into `MarketingAnalyticsController` already requires BOTH
`[Authorize(Policy = AppPolicies.MarketingAnalyticsViewOrCapability)]` AND
`[RequireModule("marketing_analytics")]` before any repository call happens, and every query is
already hard-scoped to the caller's own JWT `tenant_id` (never externally supplied). By the time
`MarketingAnalyticsRepository` runs, the caller is already confirmed authorized to view
network-wide marketing analytics for their own tenant — `store_scope`'s per-store narrowing is the
only thing left disagreeing with that. `tenant_isolation` is untouched by this decision and
continues to enforce tenant boundaries exactly as before.

Decision:

1. **Mechanism: a new dedicated RLS bypass role-value (`'marketing_analytics_bypass'`), not
   reuse of `'enterprise_admin'`.** Both were evaluated. Reuse (`SET LOCAL app.role =
   'enterprise_admin'` for the query) would need zero migration, since `enterprise_admin` is
   already in `store_scope`'s IN-list. Checked directly whether this is actually safe: grepped
   every `CREATE POLICY` in `backend/ShelfGuard.Infrastructure/Migrations` referencing
   `app.role` against all 5 tables `MarketingAnalyticsRepository` touches (`pos_transactions`,
   `pos_transaction_items`, `items`, `customers`, `locations`). Each carries only the canonical
   triad (`tenant_isolation` keyed on `app.tenant_id` only, `provider_bypass` keyed on
   `app.role = 'provider'`, `worker_bypass` keyed on `app.role = 'worker'`) plus, on
   `pos_transactions` only, `store_scope` itself. `enterprise_admin` as an `app.role` value
   appears **nowhere else** in the schema's RLS policies — reuse would today be safe on exactly
   these 5 tables. Rejected anyway, for reasons beyond today's snapshot:
   - **Future-policy risk.** `enterprise_admin` is a real, live-growing role that new bypass
     grants get added to over time (see `provider_admin`'s own addition to `store_scope`, flagged
     mid-migration as "not in the original brief"). A future policy added to `customers`/`items`/
     `locations`/anything else keyed on `app.role = 'enterprise_admin'` would silently widen this
     override's reach with nobody noticing, because nothing about the override's own code would
     change — the whole point of a narrow security-contract primitive is that its blast radius is
     legible by reading its own definition, not by re-auditing the entire schema every time a new
     migration ships.
   - **Misattribution.** If anything ever logs/audits `current_setting('app.role')` (nothing does
     today — checked, no trigger or app-layer code reads `app.role` outside RLS `USING` clauses
     and `TenantConnectionInterceptor`), a `store_manager`'s marketing-analytics query would
     falsely claim to be `enterprise_admin` mid-transaction. A dedicated sentinel value that no
     real user is ever assigned is unambiguous in any such trail, present or future.
   - Matches this codebase's own established posture: `store_scope`'s own migration doc comment
     already treats "which role strings get bypass on which table" as something to flag and
     justify explicitly, never to reuse implicitly for a new purpose.

   `'marketing_analytics_bypass'` (not the brief's suggested generic `'analytics_bypass'` —
   this codebase has a separate, unrelated `AnalyticsController`/`analytics.view_margin` module,
   ADR-027; a module-qualified name avoids future confusion between the two). It is added ONLY to
   `store_scope`'s IN-list on `pos_transactions` (the only one of `store_scope`'s 9 governed
   tables `MarketingAnalyticsRepository` ever queries — `product_stock`, `daily_sales`,
   `pos_shifts`, `write_offs`, `discounts`, `stock_receipts`, `stock_movements`,
   `stock_transfers` are untouched, in policy and in migration scope). It must never be added to
   `TenantConnectionInterceptor.ValidRoles` (so a crafted/stale JWT role claim can never set it)
   and never assigned as a real `User.Role`/`TenantRole` — `UserService.ValidRoles` stays exactly
   as-is, no new entry.

2. **New interface, not an extension of `ITenantSessionOverride`.** `ITenantSessionOverride`
   (TASK-417) overrides *which tenant* an operation runs as, for session shapes that structurally
   never carry `app.tenant_id` at all — its security contract requires the caller to pass in an
   already-validated `tenantId` as a business-trust decision made by the calling code. This
   problem is different in kind: `app.tenant_id` is untouched and correct for every
   `MarketingAnalyticsRepository` caller already (an ordinary staff JWT request); only `app.role`
   needs to change, and the trust boundary is not per-call at all — it was already fully
   established once, at the controller's `[Authorize]`/`[RequireModule]` gate, before the
   repository is ever reached. That means the new primitive needs **no parameter** (unlike
   `ExecuteAsync(Guid tenantId, ...)`), which would be an awkward, easy-to-misuse fit bolted onto
   `ITenantSessionOverride`'s shape. Two distinct interfaces, same `SET LOCAL` + explicit-
   transaction mechanism:

   ```csharp
   // ShelfGuard.Application/Services/IAnalyticsRlsOverride.cs
   namespace ShelfGuard.Application.Services;

   /// <summary>
   /// Lets MarketingAnalyticsRepository's queries run under a session role that
   /// store_scope's RESTRICTIVE policy on pos_transactions recognizes as exempt, for the
   /// duration of one repository method only (TASK-508/KI-033).
   ///
   /// SECURITY CONTRACT — read before adding a new call site: this is NOT a general-purpose
   /// "bypass RLS" escape hatch. It may ONLY be called from inside
   /// ShelfGuard.Infrastructure.Data.Repositories.MarketingAnalyticsRepository. The trust
   /// boundary it relies on is established once, upstream, before any repository method
   /// runs: every MarketingAnalyticsController action requires BOTH
   /// [Authorize(Policy = AppPolicies.MarketingAnalyticsViewOrCapability)] AND
   /// [RequireModule("marketing_analytics")], and every repository query is already scoped
   /// to the caller's own JWT tenant_id (tenant_isolation is untouched by this override —
   /// it changes app.role only, never app.tenant_id). A future call site outside that
   /// repository has NOT inherited that trust boundary just by being in the same codebase —
   /// do not reuse this for any other repository or table without re-deriving the same
   /// argument from scratch and updating this contract.
   ///
   /// Implemented with Postgres SET LOCAL app.role = 'marketing_analytics_bypass' inside an
   /// explicit transaction — reverts automatically on commit or rollback, so it can never
   /// leak into a query that runs after this call returns or into a later request reusing
   /// the same pooled connection. 'marketing_analytics_bypass' is a value store_scope's own
   /// bypass IN-list recognizes; it is not a real role, is never in
   /// TenantConnectionInterceptor.ValidRoles, and is never assignable to any User/TenantRole
   /// — its only reason to exist is this one bypass check.
   /// </summary>
   public interface IAnalyticsRlsOverride
   {
       Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct = default);
   }
   ```

   Implementation (`ShelfGuard.Infrastructure/Services/AnalyticsRlsOverride.cs`) mirrors
   `TenantSessionOverride` exactly: `BeginTransactionAsync` → `SET LOCAL app.role =
   'marketing_analytics_bypass'` (fixed string literal, never interpolated from any input —
   there is no parameter to inject) → run `action()` → `CommitAsync`. `await using` on the
   transaction guarantees rollback-reverts-the-SET-LOCAL on any exception from `action`.

3. **Scope: every method of `MarketingAnalyticsRepository`, wrapped inside the repository
   itself — not per service-layer call site.** Confirmed this belongs to the whole class, not
   just the 3 store-migration methods: the same `pos_transactions`-tenant-wide-vs-scoped
   mismatch already existed on the pre-existing RFM overview endpoint (shipped TASK-406/409),
   store-migration just made the consequence more visible (misclassification, not just
   undercounting). Read every method in `IMarketingAnalyticsRepository`
   (`GetScoredCustomersAsync`, `GetCustomerBaseCountsAsync`, `GetTopProductsAsync`,
   `GetBehaviorAsync`, `GetLtvAsync`, `GetAffinityAsync`, `GetBasketAsync`,
   `GetExportCustomersAsync`, `GetProductBuyerCustomerIdsAsync`,
   `GetProductPairBuyerCustomerIdsAsync`, `GetActivePeriodCustomerCountAsync`,
   `GetStoreMigrationFlowsAsync`, `GetStoreMigrationCustomersAsync`) — all but
   `GetExportCustomersAsync` query `pos_transactions` (directly or via `pos_transaction_items`
   join) and are affected; `GetExportCustomersAsync` only queries `customers` (no `store_scope`
   policy exists on that table at all) and is already unaffected, but gets wrapped too anyway —
   uniform "every method in this repository always runs through the override" is a simpler rule
   than carrying an exception, and wrapping a query the override doesn't change is harmless.

   Chose the repository layer over the service layer (contrast: `ITenantSessionOverride`'s
   established call-site convention wraps at the Application/service layer, e.g.
   `LoyaltyService.JoinAsync`) because this is structurally a different kind of decision: there
   is no per-call trust value for a service method to vouch for (point 2 above) — the override
   applies unconditionally to every query this one repository ever issues, which is a
   repository-level invariant, not a business decision `MarketingAnalyticsService`'s ~11 methods
   (some calling into 2-4 repository methods each, e.g. `GetSegmentDetailAsync`) would each need
   to remember to opt into correctly. Wrapping in the repository guarantees full coverage by
   construction — including any future method added to this repository — with zero risk of a
   forgotten call site. Each method wraps its own existing body (including methods that already
   issue multiple sequential `SqlQueryRaw` calls, e.g. `GetBehaviorAsync`'s three queries) in one
   `_analyticsRlsOverride.ExecuteAsync(...)` call — one short-lived transaction per repository
   method call, not one shared transaction across a whole HTTP request. `MarketingAnalyticsService.
   ExplainSegmentAsync`'s Claude "explain more" call (`IMarketingAdvisor`, external HTTP) must stay
   entirely outside any such transaction regardless of layer — the repository-level wrap satisfies
   this automatically, since the override only ever wraps a single repository method's own DB
   work, never anything the service layer does around it.

4. **`pos_transactions.store_scope` migration**: `ALTER POLICY store_scope ON pos_transactions`
   (or `DROP POLICY` + `CREATE POLICY`, matching this codebase's existing convention for policy
   edits, e.g. `V4LocationsRename`'s `location_zones` policy update) so its `USING` clause reads:
   `current_setting('app.role', true) IN ('provider', 'provider_admin', 'worker',
   'enterprise_admin', 'marketing_analytics_bypass')` — the rest of the clause (the
   `user_locations` EXISTS fallback) is unchanged. No other table's `store_scope` policy changes.

Consequences:
+ Fixes KI-033 for the whole `MarketingAnalyticsController` (RFM overview + store-migration, and
  any future action added to this repository) with one small migration and a mechanical,
  low-risk repository change — no controller/service/DTO changes needed
+ Auditable: a query issued under this override is unambiguously labelled
  `marketing_analytics_bypass` in `current_setting('app.role')`, never a false claim of being a
  real elevated role
+ `ADR-022`'s store-scope design for every OTHER `pos_transactions` read path (POS shift reports,
  receipts, etc.) and for the other 8 `store_scope`-governed tables is completely untouched —
  this narrows nothing else, widens nothing else
+ No service-layer discipline required going forward — every current and future
  `MarketingAnalyticsRepository` method is correct by construction, not by convention
- One new interface + implementation + one migration (TASK-509, not built here)
- `MarketingAnalyticsService` methods that call several repository methods per request now open
  several short-lived transactions instead of one combined one (contrast
  `LoyaltyService.LoadNetworkDetailsAsync`'s "batch multiple reads into one override block"
  precedent) — accepted: every `MarketingAnalyticsRepository` method is already a self-contained,
  read-only, order-independent unit (the module's own design already recomputes R/F/M scoring
  fresh on every call, no cross-call consistency requirement), so the extra `BEGIN`/`SET
  LOCAL`/`COMMIT` round trips are pure overhead, not a correctness risk, on a non-hot-path
  analytics surface
- `'marketing_analytics_bypass'` is one more string a future reader of `store_scope` needs to
  recognize as "not a real role" — mitigated by the migration's own doc comment and this ADR

Supersedes: nothing. Narrows ADR-022 Stage 3's `store_scope` policy with one additive, single-
table bypass value for one specific, already-authorized read path; does not reopen or relitigate
the `store_manager`/`network_manager` exclusion ADR-022 made for every other `pos_transactions`
(or any of the other 8 tables') read path, which is unchanged and correct as designed.

## ADR-027: Analytics margin — `Item.PricePurchase` as retroactive cost source, network_manager+ authorization floor, deferred cashier/payment-type drill-downs
Date: 2026-08-07
Status: accepted

Context: Interactive-analytics-and-margin initiative (TASK-479..487) makes `/analytics` and
`/analytics/pos` clickable (drill-down panels instead of navigate-away links) and adds
margin/profitability figures, which do not exist anywhere in the product today. TASK-479
(database-engineer) added the supporting covering index. This entry — written by TASK-480
(backend-developer), which shipped `AnalyticsAuthorization.CanViewMargin` and the
`analytics.view_margin` capability — records the two decisions that constrain every later task in
the initiative: where margin numbers come from, and which two requested drill-downs are explicitly
not being built this phase. TASK-481/482 (backend-developer) wire the actual DTOs/endpoints against
both decisions; TASK-483/484/485 (frontend-developer) build the UI; TASK-486 (qa-tester)/TASK-487
(security-reviewer) verify the result.

Decision:

1. **Margin cost source: current `Item.PricePurchase`, applied retroactively to every historical
   `PosTransactionItem` — not a true point-of-sale cost snapshot.** Reconstructing the real cost of
   the specific batch sold (`PosTransactionItem.ProductStockId → ProductStock →
   StockReceiptItem.PricePurchase`) was checked by reading `ReceiptService.cs`, `TransferService.cs`,
   `ProductionService.cs`, and `StockService.cs`, and the chain breaks at multiple points:
   - `TransferService` writes the destination `ProductStock` row with `SourceType="transfer"` — the
     original purchase cost is only reachable by recursively walking the transfer chain, which can
     itself pass through further transfers or a production batch rather than terminating at a
     receipt.
   - `ProductionService` writes `SourceType="production"` with no cost link at all — resolving a
     manufactured batch's cost would need full recipe costing (ingredient costs × BOM quantities), a
     distinct, larger feature that does not exist today.
   - `PosTransactionItem.ProductStockId` is nullable and is `SET NULL` when the referenced batch is
     later deleted — so even sales that originally pointed at a cleanly-received batch lose that link
     over time, independent of source type.

   Resolving this exactly would require either a `WITH RECURSIVE` SQL walk per sale or a new
   denormalized cost column written at every `ProductStock`-creating call site plus a backfill
   migration — a materially larger and riskier scope than this phase's authorization-and-read-endpoint
   work, and even then would not reach full coverage (rows that already lost their `ProductStockId`
   stay unrecoverable either way). Given that, margin is computed as
   `Revenue − Quantity × Item.PricePurchase`, using the item's CURRENT catalog purchase price against
   ALL historical quantities: cheap (one pass over already-aggregated points), always available (no
   dependency on `ProductStockId` surviving), but not what the batch actually cost at the time it was
   sold if the supplier price has changed since.

   **UI requirement, binding on TASK-483/484 (frontend-developer):** every rendered margin figure
   (amount or percent, on any of the three new endpoints) must carry a visible "estimated" label —
   Ukrainian "оцінна маржа" — not a tooltip-only caveat. This is a correctness disclosure, not a
   nice-to-have: the figure is a retroactive approximation, and presenting it unlabeled would
   overstate its precision to a network_manager+ viewer using it for a real decision.

   `WriteOffItem.LossAmount` needs no equivalent decision — it is already computed and stored at
   write-off time, not reconstructed afterward — so `losses/by-product` carries no estimation caveat
   and, per `AnalyticsAuthorization.CanViewMargin`'s own doc comment, is also not gated by it (losses
   are already shown in aggregate to every store_manager+ today).

   **Deferred fast-follow, explicitly not built this phase:** a nullable `CostAtSale` snapshot column
   on `PosTransactionItem`, resolved and written once at the moment of sale (the same call site that
   already writes the row). Every sale from that point on would carry its own exact cost forever, with
   no retroactive query and no dependency on `ProductStockId` surviving later batch deletions — but it
   does nothing for sales that already happened, which stay on the `Item.PricePurchase` estimate
   permanently. Not scheduled; recorded here so the idea isn't lost.

2. **Two requested analytics interactions are deferred as backlog, not built this phase:**
   - **Cashier sales-trend drill-down** (clicking a row in `PosCashierStatsTable`). No
     `AnalyticsController` endpoint filters by `cashier_id` today; the shape would closely mirror
     TASK-482's per-product trend endpoint (`GetProductSalesTrendAsync`), but is a new endpoint and
     repository method, not a parameter added to an existing one. Left undone rather than half-done —
     a clickable row with no working destination is worse UX than a non-interactive one.
   - **Payment-type filtering on `/analytics/pos`** (from `PosPaymentPieChart`). No endpoint on the
     POS analytics surface accepts a `PaymentType` filter today. Unlike the scoped drill-down panels
     this phase does build (one clicked data point expands into a detail panel), this is a request to
     refilter the ENTIRE page by a new dimension — a page-wide filter-bar addition touching every
     card/table on `/analytics/pos` at once, a different and larger shape of work than a scoped panel,
     and out of scope for this phase's brief.

3. **Authorization primitive shipped this task (TASK-480):**
   `AnalyticsAuthorization.CanViewMargin` (`backend/ShelfGuard.Infrastructure/Authorization/
   AnalyticsAuthorization.cs`) gates the margin figures above — network_manager+
   (`AppPolicies.AtLeastNetworkManagerRoles`) by default, OR the new
   `TenantRoleCapabilities.AnalyticsViewMargin` (`"analytics.view_margin"`) capability — one tier
   above the store_manager+ floor (`AnalyticsViewOrCapability`) that already gates the whole
   `AnalyticsController`. Same imperative in-body-check shape as
   `MarketingAnalyticsAuthorization.CanExportPii`, for the same structural reason: it narrows two
   fields of an otherwise-successful response (server nulls `MarginAmount`/`MarginPercent`, it does
   not 403 the request), not a whole endpoint. Full reasoning for the network_manager+ threshold
   (versus the base store_manager+ floor) lives in the method's own doc comment, not repeated here.
   Per KI-030, the capability branch is currently inert in production (`TenantRole.Capabilities`
   doesn't reach the JWT yet) — the same standing gap `MarketingAnalyticsExportPii`'s capability
   branch already has; the role branch (network_manager+) is unaffected and works today. TASK-481/482
   wire this check into the actual DTOs/endpoints; TASK-487 (security-reviewer) re-verifies
   server-side enforcement end to end (including that it does not simply duplicate the already-known
   KI-030 finding).

Consequences:
+ Margin ships this phase without a multi-call-site cost-snapshot migration and backfill — a smaller,
  lower-risk scope for TASK-481/482
+ The authorization threshold is decided and tested up front (TASK-480, `AnalyticsAuthorizationTests`),
  so TASK-481/482 implement against a settled contract instead of relitigating it mid-endpoint
- Margin figures are a retroactive estimate against the CURRENT catalog price, not the actual cost at
  time of sale — will silently drift from reality for any item whose `PricePurchase` has changed since
  old sales; mitigated only by the mandatory "estimated" UI label, not by the number's own accuracy
- Sales made from transferred or produced stock, and any sale whose original batch has since been
  deleted, have no path to exact cost even after the deferred `CostAtSale` fast-follow ships, unless
  that snapshot is written going forward — historical accuracy for those rows is permanently capped at
  the estimate
- Cashier trend drill-down and payment-type filtering remain non-interactive on `/analytics/pos` after
  this phase ships — two of the four requested `/analytics/pos` interactions land, two stay backlog
- Capability-based widening of `CanViewMargin` is inert until KI-030 is fixed — until then, granting a
  specific sub-network_manager role the `analytics.view_margin` capability does not actually widen
  their access in production, identical to the standing `marketing_analytics.export_pii` gap

Supersedes: nothing — additive within the interactive-analytics-margin initiative (TASK-479..487).

