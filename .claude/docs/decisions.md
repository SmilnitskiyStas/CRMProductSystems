# Architecture Decisions (ADR Log)

**Owner:** project-architect
**Updated:** 2026-09-01

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
Status: accepted

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

## ADR-026: Forgot-password redesign — temporary password replaces link/token, third RLS exception retired, auth-locale default flips to English
Date: 2026-08-04
Status: accepted

Context: ADR-024/TASK-455..460 shipped a one-time email/Telegram link+token forgot-password flow
to production on 2026-07-30 (commit `647bde4c`). Days later the product owner asked for a
different UX: instead of a link the user clicks to then enter a new password on a separate page,
the system should generate a temporary password the user can log in with immediately — no second
step, no link, no token. TASK-464 (database-engineer), TASK-465 (backend-developer), TASK-466
(frontend-developer) implemented this over 2026-08-04, fully replacing (not extending) ADR-024's
design end-to-end. This ADR records the cross-cutting decisions, verified against the shipped code.

Decision:

1. **A temporary password overwrites `User.PasswordHash` directly; no separate token/link
   entity.** `AuthService.ForgotPasswordAsync` generates a 14-character
   `RandomNumberGenerator`-backed password (letter and digit classes constructively guaranteed —
   one character drawn from a letters-only pool, one from a digits-only pool, the rest from the
   combined pool, then an unbiased Fisher–Yates shuffle so the guaranteed positions aren't
   predictable — never left to chance, so it always passes `PasswordValidator.Validate`; visually
   ambiguous characters 0/O/1/I/l excluded), calls the pre-existing `user.ChangePassword(hash)`
   with it, and sets `User.TempPasswordExpiresAt = UtcNow.AddHours(3)` via the new
   `SetTempPasswordExpiry` method (TASK-464). This becomes the account's real, immediately-usable
   password — logging in with it goes through the ordinary `POST /api/auth/login`, no new
   endpoint. The credential write commits on its own, before the activity log / outbox
   notification, so the password change is durable independent of whether logging or notification
   delivery succeeds.
2. **`password_reset_tokens` is dropped entirely, not deprecated — the third fail-open RLS
   exception it required (ADR-024 point 2) is retired with it.** `database-schema.md`'s documented
   fail-open exceptions list is back to exactly two rows (`users`, `refresh_tokens`), matching the
   state before ADR-024/TASK-455. The temporary-password design has no pre-auth token lookup to
   perform at all — `ForgotPasswordAsync` only ever writes to `users`, which already carries its
   own necessary fail-open exception for login. No new narrower RLS policy was needed to replace
   it; the whole category of problem ("look this row up before the caller's tenant is known")
   disappears along with the token table, it isn't relocated.
3. **`POST /api/auth/reset-password` is removed, not repointed — password changes flow through the
   existing authenticated `change-password` endpoint instead.** There is no second step in the new
   design. Completing a password change — whether starting from a temporary password or not — goes
   through the existing *authenticated* `POST /api/auth/change-password`, which now also calls the
   new `user.ClearTempPasswordExpiry()` right after a successful change (the one place a user
   "takes control" back from a temp password). `POST /api/auth/forgot-password` itself keeps its
   existing shape, rate limit (5/min/IP), and always-204 no-enumeration behavior — only its payload
   changed, from a link to a directly-usable credential. `AuthUserDto` gained
   `passwordIsTemporary`/`temporaryPasswordExpiresAt`, computed fresh at every mint site through
   the shared `ToDto` mapper, and `POST /api/auth/login` gained one new specific 401 ("Temporary
   password has expired. Please request a new one.") — reachable only after a real hash match
   against an expired temp password, never on a genuinely wrong password, so it adds no new
   account-enumeration signal.
4. **TASK-467 (security-reviewer, 2026-08-05) reviewed this whole redesign and returned CLEAR TO
   SHIP — 0 HIGH findings, 2 MEDIUM findings, both recommended to fix soon, neither a deploy
   blocker. Both MEDIUM findings are now fixed — TASK-469 (backend-developer, 2026-08-05), same
   day — see the closing paragraph below.**
   - **No per-user forgot-password cooldown.** The old design's 60-second `PasswordResetCooldown`
     (`HasRecentActiveTokenAsync` against the token table, added by TASK-460 as a MEDIUM fix from
     TASK-458's review) has no equivalent here: TASK-465's brief specified a 9-step
     `ForgotPasswordAsync` sequence with no cooldown, and TASK-464 added no field that could back
     one independent of `TempPasswordExpiresAt` itself — the old cooldown was keyed off the
     now-deleted token table. The per-IP rate limit (`auth-forgot-password`, 5/min) is once again
     the *only* throttle, and `known-issues.md` KI-014 already documents per-IP limiting as
     unreliable in production (the hosting provider's edge does not preserve client source IPs).
     TASK-467 judged this **materially worse** than in the superseded design: there, repeated
     forgot-password calls only spammed notifications while the real password stayed untouched
     until a separate reset step completed; here, every call immediately overwrites `PasswordHash`,
     so an attacker who knows/guesses a victim's email can loop the endpoint and keep invalidating
     whatever credential the legitimate user currently holds — a low-effort, repeatable
     account-lockout/denial-of-access vector, not just harassment. Kept at MEDIUM rather than HIGH
     because it needs no new capability beyond what KI-014 already concedes and crosses no
     tenant/account boundary. Low-cost fix identified, no new migration needed: derive "when was
     the current temp password issued" from the existing `TempPasswordExpiresAt` field and
     no-op/skip re-issuance within a ~60s window.
   - **`ForgotPasswordAsync` never calls `RevokeAllForUserAsync`, unlike the superseded
     `ResetPasswordAsync`.** The old design's reset step revoked every refresh token as its last
     write — an explicit anti-hijack measure TASK-458 had confirmed present. The new
     `ForgotPasswordAsync` has no equivalent call anywhere in its body; only the pre-existing,
     authenticated `UserService.ChangePasswordAsync` still does (unchanged — TASK-467 re-confirmed
     it at `UserService.cs:419`). Concrete impact: if an attacker already holds a live, stolen
     refresh token (7-day TTL) from an earlier, unrelated compromise, and the legitimate user runs
     forgot-password specifically *because* they suspect that compromise, this design no longer
     evicts the attacker's session as a side effect of recovery — the stolen token keeps minting
     access tokens until the user separately completes a full `change-password`, a follow-up step
     nothing forces (a temp password is fully usable for up to 3 hours and can be re-requested
     indefinitely without ever visiting "set a new password"). MEDIUM rather than HIGH because it
     requires a pre-existing compromise to matter — a failure to fully remediate a takeover, not a
     standalone way to gain access nobody already had. Fix identified: add
     `await _refreshTokens.RevokeAllForUserAsync(user.Id, ct)` inside `ForgotPasswordAsync`,
     mirroring `ChangePasswordAsync`'s existing call, ideally flushed in the same early
     `_users.SaveChangesAsync` round trip that already durably commits the credential change.

   **Both fixes landed the same day, in TASK-469 (backend-developer).** Cooldown: `AuthService`
   now derives `issuedAt = TempPasswordExpiresAt - TempPasswordValidHours` (no new column/migration)
   and, when a temp password was issued <60s ago, no-ops the re-issuance — zero side effects, same
   204 response — checked after the unknown/inactive-email branch so that branch's
   timing/enumeration posture is unchanged. Revocation: `ForgotPasswordAsync` now calls
   `await _refreshTokens.RevokeAllForUserAsync(user.Id, ct)`, mirroring
   `UserService.ChangePasswordAsync`, placed before the early `_users.SaveChangesAsync(ct)` so both
   the credential change and the revocation commit in the same round trip. Verified: build 0
   warnings/0 errors, tests 1222/1222 (net +2 new). Full detail:
   `.claude/logs/tasks/467_2026-08-05_security-review-temp-password-redesign_security-reviewer.md`
   (original findings) and
   `.claude/logs/tasks/469_2026-08-05_fix-forgot-password-medium-findings_backend-developer.md`
   (fixes).
5. **Auth-page default locale flips from Ukrainian to English for non-`uk-*` browsers — a smaller,
   independent change bundled into the same TASK-466.** `DashboardIntlProvider` gained a
   `defaultLocale` prop (default `"uk"`, so every existing dashboard call site stays
   behavior-identical); `app/(auth)/layout.tsx` passes `"en"` for the two public auth pages
   (`/login`, `/forgot-password`) — the dashboard's own default is untouched. Not a security or
   architecture decision on its own; recorded here only because it shipped inside the same task as
   the rest of this redesign and would otherwise go undocumented.

Consequences:
+ Removes an entire class of link/token bugs (expiry math, single-use enforcement, URL
  construction) — ADR-024 point 4's `Frontend__BaseUrl` env-var plumbing is now unused by this
  flow specifically, though the underlying "Application layer has no `IConfiguration`" pattern it
  established remains valid precedent for the next service that needs it
+ One fewer standing fail-open RLS exception to reason about — `database-schema.md`'s exceptions
  table is back to a shorter, easier-to-audit two-row list
+ Simpler user flow end-to-end: one email/Telegram message, one login, no intermediate page —
  matches the product owner's explicit request
- TASK-467 confirmed two MEDIUM gaps versus the superseded design (full detail at point 4): no
  per-user forgot-password cooldown — worse here than in the old design, since every call
  overwrites the real password immediately rather than just sending another link — and no
  `RevokeAllForUserAsync` call in `ForgotPasswordAsync`, so a stolen refresh token from an earlier
  compromise survives a forgot-password request where the old `ResetPasswordAsync` would have
  evicted it. Verdict was CLEAR TO SHIP with 0 HIGH — neither gap blocked the design already live.
  **Both are now fixed, same-day, by TASK-469** — see point 4 above
- A temporary password is a directly-usable credential in transit (email/Telegram) — a materially
  bigger blast radius than a single-purpose link if a delivery channel is compromised or
  intercepted. TASK-465 carried forward the same pre-`logNotifications()` redaction ADR-024/
  TASK-460 established for `resetUrl` (now redacting `tempPassword` the same way), so the live
  value never reaches `notification_queue`/`GET /api/notifications/history` — but the underlying
  channel security (email/Telegram delivery itself) is unchanged from ADR-024's own accepted
  posture, and the credential itself is now higher-stakes than the link it replaced
- ADR-024 is left superseded rather than deleted, per this repo's documentation convention —
  future readers must follow the superseded pointer rather than assume its content is current if
  they land on it directly (e.g. via search)

Supersedes: ADR-024 (points 2 and 5 specifically; points 1, 3, 4 carry over unchanged in
substance — see the superseded-note added to ADR-024 below for the exact breakdown).

## ADR-025: Mobile offline boundary — durable drafts and limited cached reads, online-only mutations
Date: 2026-08-01
Status: accepted

Context: TASK-443 and TASK-444 made POS and operational form state durable, but deliberately did
not introduce automatic mutation replay. The current create contracts do not provide a universal
client idempotency key or reconciliation lookup, and POS additionally crosses stock, loyalty,
shift and fiscal boundaries. Product owner confirmed the first mobile release targets Android and
iOS phones, portrait-only; tablet adaptation is deferred; preview builds use the production API.
The selected offline scope is durable drafts plus limited offline reads, with no mutation queue and
no full offline POS. This ADR defines that boundary before persisted server-state queries are added.

Decision:

1. **Goals and non-goals.** Mobile preserves user-entered POS/warehouse/production draft state and
   may show explicitly selected, last-successful read models while disconnected. It must never
   represent cached stock, price, entitlement, shift, loyalty, fiscal or module state as current.
   Completing a sale, write-off, transfer, receipt operation, production order, loyalty redemption,
   shift action or any other business mutation requires confirmed online connectivity and a fresh
   server validation. Offline mutation replay and full offline POS are explicit non-goals.
2. **Allowed cached read models.** Initial allowlist: product/catalog summaries needed to identify
   an item, non-secret customer display/search summaries, recipe summaries, notification/list
   summaries, schedules, marketplace/supplier summaries, and recent read-only document/list views.
   Stock quantities/batches, active POS shift, prices/discounts, loyalty balances, permissions,
   module activation, fiscal state and operational eligibility may be cached only for display and
   must carry a prominent stale marker; they can never authorize or parameterize an offline submit.
   Detail payloads containing secrets, rotating loyalty QR/code values, TOTP/recovery/challenge
   values, auth tokens, payment data or unrestricted PII are excluded.
3. **Staleness UI, TTL and retention.** Every cached surface displays `Офлайн-дані` and the
   last successful server timestamp in local time. Missing timestamps mean no usable offline data.
   Default soft TTL is 15 minutes for stock/price/loyalty/shift-derived views, 24 hours for catalog,
   customers, recipes, schedules and documents, and 6 hours for notifications/marketplace. Expired
   data may remain viewable for up to 7 days with an explicit `можуть бути застарілими` state, but
   is never silently treated as fresh. Cache retention is capped at 7 days; durable drafts have a
   30-day retention target and require explicit user discard or confirmed-success cleanup.
4. **Ownership and storage.** Persisted keys are versioned and namespaced by environment,
   tenant ID, user ID, query family and normalized scope/filter. Rehydration fails closed until the
   authenticated tenant+user owner is known. Account/tenant switching must synchronously hide the
   previous namespace. AsyncStorage may hold the allowlisted, minimized read models and draft
   payloads; SecureStore remains for auth secrets only. Native iOS Keychain/Android Keystore-backed
   encryption protects secrets, not arbitrary query caches. No claim is made that AsyncStorage is
   encrypted at rest; sensitive fields are excluded rather than relying on device storage alone.
5. **Query persistence and connectivity.** React Query remains the owner of server state. A
   versioned, allowlisted persistence adapter may dehydrate only approved query keys and must
   validate schema, owner, timestamp and size before rehydration. NetInfo is a UX/input signal, not
   proof that the API is reachable: online submit additionally requires a successful fresh API
   request/revalidation. Reconnect invalidates or refetches stale active-screen queries; logout or
   terminal session cleanup cancels queries, clears in-memory private data and deletes that owner's
   persisted query cache and drafts according to the existing explicit session-cleanup contract.
6. **Submit, idempotency and conflicts.** All business submit controls are disabled offline.
   Before submit, mobile refetches the authoritative dependencies appropriate to the flow
   (including shift, stock/batch, recipe/module, price/discount and loyalty state) and rejects a
   stale/conflicting draft with actionable UI. FEFO and stock allocation remain exclusively
   server-authoritative. A locally generated correlation ID may be logged, but it is not an
   idempotency guarantee. Until the backend contracts in TASK-443/444 handoffs support idempotency
   or lookup, timeout/no-response remains `uncertain`, automatic retry is forbidden, and `409`
   remains an explicit conflict requiring reconciliation. No background worker drains mutations.
7. **POS/fiscal/loyalty limit.** An offline POS cart/customer choice can be restored, but checkout,
   payment finalization, loyalty redemption/accrual, shift open/close and Checkbox/PRRO fiscalization
   cannot start offline. Cached balance, price, discount and shift data are informational only and
   must be revalidated online. This avoids duplicate sales, overselling, replayed loyalty codes and
   undocumented deferred fiscalization.
8. **Platform and presentation boundary.** The same behavior ships on Android and iOS phones.
   Portrait is the only supported launch orientation. iOS background suspension and Keychain access
   classes, and Android process death/Auto Backup/device-transfer behavior, must be tested separately;
   query/draft caches must not be included in cloud/device backup unless an explicit security review
   approves it. Tablet and landscape POS layouts are deferred and do not alter this data boundary.
9. **Observability and privacy.** Record cache schema/version, family, age bucket, rehydrate outcome,
   invalidation reason, online revalidation result and conflict class. Never log payload bodies,
   query contents, names, phones, tokens, QR/TOTP/recovery data, payment fields or draft values.
   Tenant/user identifiers must be omitted or irreversibly pseudonymized in telemetry. Metrics are
   aggregate operational signals, not a second store of user data.
10. **Rollout and migration.** Introduce the read cache behind a mobile feature flag and allowlist,
    starting with catalog/schedules/marketplace before stock/customer/loyalty-derived surfaces.
    Schema changes bump the persistence version; unknown/corrupt/legacy read-cache records are
    deleted fail-closed. Existing TASK-443/444 draft schemas remain in place and migrate only via
    explicit owner-safe version handlers. Rollout acceptance requires Android and iOS process-death,
    logout/account-switch, reconnect, stale-data, storage-pressure and privacy tests.

Consequences:
+ Users retain in-progress work and can consult bounded last-known information during outages.
+ The online server remains the single authority for FEFO, stock, prices, permissions, loyalty,
  shifts and fiscal state; no hidden queue can duplicate or reorder business operations.
+ One cross-platform policy applies to Android and iOS phone launch; portrait-only reduces the
  initial layout/test matrix while preserving a later tablet adaptation path.
- Offline users cannot complete a sale or warehouse/production mutation; the UI must make this
  limitation explicit rather than suggesting that an operation was queued.
- Cached reads add storage, privacy, invalidation and stale-data UX complexity and therefore must
  be introduced per-query-family, never by persisting the whole React Query cache.

Rejected alternatives:

- **Durable drafts only:** safer but insufficient for useful read-only work during a temporary
  outage; rejected in favor of a strict cached-read allowlist.
- **Generic mutation queue:** rejected because current contracts lack universal idempotency and
  reconciliation, stock changes conflict, and ordering/retry can duplicate irreversible actions.
- **Full offline POS:** rejected for launch because shift, stock, price, loyalty, payment and
  Checkbox/PRRO rules require a separately designed and legally validated synchronization model.
- **Persist every React Query response:** rejected because it would cache secrets/PII and
  authorization-sensitive state without deliberate TTL, ownership or UX review.

Follow-up: TASK-461 (allowlisted query-cache foundation), TASK-462 (offline read UX rollout), and
TASK-463 (cross-platform offline security/device acceptance). TASK-443/444 handoffs remain the
authority for future idempotency contracts; they do not authorize a mutation queue.

## ADR-024: Forgot/reset-password flow — outbox reuse, third fail-open RLS exception, env-var frontend URL, 400 not 401
Date: 2026-07-30
Status: **superseded by ADR-026** (2026-08-05) — kept below verbatim as historical context, do not
build against it.

**⚠️ Why superseded.** Product owner asked for a different UX only days after this design shipped
to prod (2026-07-30, commit `647bde4c`): a temporary password the user receives and can log in
with directly, not a one-time link+token requiring a second "click link, enter new password"
step. TASK-464..466 (2026-08-04) implemented the replacement end-to-end; ADR-026 above records it.
Of the 5 decisions this ADR made, **(2)** the third fail-open RLS exception and **(5)** the
400-vs-401 reasoning for `POST /api/auth/reset-password` no longer apply — `password_reset_tokens`
and that endpoint are both gone entirely. **(1)** outbox reuse, **(3)** email-primary/
Telegram-fallback channel choice, and **(4)** the `Environment.GetEnvironmentVariable`
Application-layer pattern all remain true of the new design too, unchanged in substance — see
ADR-026 for exactly what carried over vs. what changed.

Context: ShelfGuard had no way for a user locked out of `/login` to recover a forgotten
password — a repo-wide grep (`backend`/`frontend`/`mobile`/`worker`/`.claude`) for
forgot/reset-password confirmed zero existing code, and `v1-spec.md` never specified the
flow either. Two possible delivery channels exist for reaching that user: email
(`worker/src/services/email.ts` — complete, working code, but blocked today: `RESEND_API_KEY`'s
domain `agrusystems.pp.ua` has not passed Resend's DNS verification, TASK-260, blocked since
2026-06-19) and Telegram (works today, but only for a user who already linked their account —
an authenticated, opt-in flow that cannot be the *only* channel for someone locked out right
now). User confirmed via `AskUserQuestion`: email as the primary channel, Telegram as fallback
for already-linked accounts; build complete, correct code now so it activates the instant
TASK-260 unblocks — the same posture already accepted for `weekly-report`. TASK-455 (schema),
TASK-456 (backend + worker), TASK-457 (frontend) implemented this; this ADR records the
cross-cutting decisions behind it, verified against the shipped code.

Decision:

1. **Delivery reuses the existing Postgres outbox (ADR-018), not a new C# BullMQ producer.**
   ADR-018 already settled this question for backend-originated notifications in general: no
   new cross-language job-producer infrastructure — the triggering C# service inserts a row
   into `notification_queue` and `notification-dispatch.job.ts` (Node, 1-min poll) picks it up.
   `AuthService.ForgotPasswordAsync` follows this exactly: `INotificationRepository.EnqueueAsync`
   with `UserId` set (a **targeted**, not broadcast, intent row — the same shape ADR-019
   introduced for temporary-access-grant expiry notifications), `EventType =
   "auth.password_reset_requested"`, `Payload = {resetUrl, expiresInMinutes}`. The worker's
   `dispatchTargeted()` (added by ADR-019) already handles single-recipient delivery via
   `TARGETED_EVENT_CHANNELS`; this task only adds one map entry (`["email", "telegram"]` —
   deliberately no `"push"`, not implemented anywhere in this codebase yet) plus the
   `formatEmail`/`formatText` branches that turn the payload into an actual clickable-link
   message. Zero new delivery infrastructure of any kind.
2. **`password_reset_tokens` is the third documented fail-open RLS exception — not a fourth,
   and not a new kind of exception.** `database-schema.md`'s exceptions table already correctly
   lists exactly `users` / `refresh_tokens` / `password_reset_tokens` (`notification_settings`
   was removed from that list on 2026-07-15, TASK-360 — it never had a real pre-auth access
   path to begin with, so its old fail-open branch was a plain leak, not a necessary exception).
   The reasoning for the new table is identical to `refresh_tokens`'s: an anonymous
   forgot/reset-password request must find its token/user row through an `EXISTS`-through-`users`
   join before `TenantConnectionInterceptor` has any `app.tenant_id` to `SET` — there is no
   tighter alternative, since the interceptor only ever `RESET`s session vars for unauthenticated
   connections rather than setting them to something narrower.
3. **Email primary / Telegram fallback is a product decision (`AskUserQuestion`), not an
   engineering default — and it carries an explicit, tracked dependency.** The email channel will
   not actually reach a real user until TASK-260 (Resend DNS verification for
   `agrusystems.pp.ua`) unblocks — the same standing dependency already accepted for
   `weekly-report`. Telegram works today for any user who has already linked their account via
   the existing `/start <code>` flow and does not depend on TASK-260 at all.
   `.claude/tasks/blocked.md`'s TASK-260 entry now cross-references this flow rather than a new
   `known-issues.md` entry being created for it — it is a new dependent of an already-tracked
   blocker, not a new problem.
4. **`Frontend__BaseUrl` is read via `Environment.GetEnvironmentVariable`, not
   `IConfiguration`.** `ShelfGuard.Application.csproj` carries no `Microsoft.Extensions.
   Configuration` package reference at all (confirmed directly) — `AuthService` lives in the
   Application layer and physically cannot resolve `IConfiguration["Frontend:BaseUrl"]`.
   `TelegramLinkService.cs` already established the exact precedent for this same constraint
   (`Environment.GetEnvironmentVariable("Telegram__BotUsername") ?? "shelfguard_bot"`, with a
   comment stating "Application layer has no IConfiguration dependency — env var with a sane
   default"); `AuthService`'s constructor copies this pattern verbatim for `Frontend__BaseUrl`
   (default `http://localhost:3000`). Env plumbing (`.env.staging.example`,
   `.env.production.example`, both `docker-compose.*.yml`) follows the existing per-environment
   convention — no new mechanism, and no new appsettings.json entry.
5. **`POST /api/auth/reset-password` returns `400`, not `401`, on failure — unlike
   `2fa/verify`.** `2fa/verify` is mid-authentication (the password already checked out; the
   code is the second factor of that *same* login attempt), so a rejected code is genuinely an
   authorization failure — `401` fits. `reset-password` authenticates nothing and issues no
   tokens; it is a state-changing action gated by possession of a single-use, out-of-band
   secret — the same category as `change-password`/`public-leads`, both already `400`. Using
   `401` here would incorrectly imply the caller was attempting to authenticate as someone,
   which is not what this endpoint does.

Consequences:
+ Zero new delivery infrastructure — the outbox/`dispatchTargeted()` path from ADR-018/019
  absorbs a fourth event type with one map entry and two formatting branches
+ The fail-open RLS list stays a closed, understood, three-row exception set rather than
  growing unboundedly — a future table needing a similar "look this up before we know the
  tenant" flow is still expected to get its own narrower policy per `database-schema.md`'s
  existing warning, not join this list by default
+ Email ships fully built and correct, ready to work the moment TASK-260 unblocks — no
  half-finished code to revisit later — but also no way to demonstrate real end-to-end email
  delivery until that DNS dependency clears; Telegram is the only channel demonstrably live
  today, and only for already-linked accounts
+ One more confirmed precedent (`Frontend__BaseUrl`) for "Application layer has no
  `IConfiguration`, use an env var with a default" alongside `Telegram__BotUsername` — no
  architectural surprise for the next similar case
- The generic reset-link error text ("Invalid or expired reset link.") deliberately conflates
  three distinct backend states (token not found, token expired/used, owner account gone or
  inactive) into one message — correct for not leaking account state to the caller, but means
  support/debugging must rely on server-side `ActivityLog`/logs, never the client-visible error,
  to tell these apart

Extends: ADR-018 (Postgres outbox mechanism) and ADR-019 (`dispatchTargeted()` single-recipient
delivery, introduced for temporary-access-grant expiry notifications) — reuses both verbatim for
a fourth targeted event type; introduces no new notification-delivery primitive.

## ADR-023: Loyalty program & RFM marketing analytics — cross-tenant ConsumerAccount identity, TOTP-based live QR, independent module keys, RfmSegment naming
Date: 2026-07-26
Status: accepted

Context: `docs/uployal/RFM_ANALYSIS.md` is a competitive analysis of a retail RFM/marketing-
analytics dashboard. Reproducing it exposed a blocker: `PosTransaction.CustomerId` (nullable FK,
existed since v1) is never written by any code path — every sale is anonymous today, so RFM/LTV
would show all-zero data with no way to attribute a receipt to a person. Plan
`C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` splits the work into Фаза 0 (a loyalty/bonus
program that gives customers a reason to identify themselves at checkout — scan a QR, earn bonus
points — and thereby writes `CustomerId`) and Фаза 1 (the RFM dashboard itself, built on Фаза 0's
now-populated data). TASK-404 through TASK-414 implemented both; this ADR records the
architectural decisions behind the identity model and naming, verified against the actual shipped
code (not just the plan).

Decision:

1. **`ConsumerAccount` is a new, separate, global (no `TenantId`, no RLS) entity — not an
   extension of `Customer` or `User`.** `Customer` is tenant-scoped CRM data (phone unique only
   *within* a tenant — confirmed via `CustomerRepository.ExistsByPhoneAsync`); `User` is a
   tenant-scoped staff account. Neither can back "one login reads every tenant's bonus balance"
   (the plan's explicit requirement — a multi-tenant "wallet of cards"): a `ConsumerAccount` JWT
   carries `consumer_account_id` and **no** `tenant_id` at all, and reads across every
   `LoyaltyMembership` it holds regardless of tenant. Extending `Customer` would have required
   either making `Customer.Phone` globally unique (breaking existing tenant-scoped semantics — two
   unrelated tenants legitimately have customers who share a phone number) or bolting a parallel
   global-identity concept onto an entity whose entire reason to exist is tenant scoping. A
   `LoyaltyMembership` join row (tenant-scoped, FK to both `ConsumerAccount` and `Customer`)
   composes both without compromising either — `Customer` keeps its existing tenant-local meaning;
   `ConsumerAccount` is the only genuinely global identity concept added by this series.
   Consequence, accepted deliberately: `consumer_accounts` is the one table in the project with
   **no RLS** (same precedent as `tenants`), reviewed explicitly by security-reviewer (TASK-412,
   item #1 — verdict OK, no generic non-owner lookup exists anywhere in the codebase) and
   documented as a standing convention in `database-schema.md`, not a gap to "fix" later.

2. **The "live" rotating QR/barcode reuses the existing TOTP infrastructure (`Otp.NET`/
   `ITotpService`, already used for `User` 2FA) instead of inventing a new rotating-token format.**
   The plan's requirement (protect against screenshot-sharing — a static code defeats the whole
   point of scan-to-earn) is structurally identical to what TOTP already solves for 2FA: a shared
   secret plus a time-step counter produces a code that rotates on a fixed interval and can be
   verified with a bounded-window anti-replay check. `LoyaltyMembership.TotpSecret` +
   `LastRedeemedTimestep` mirror `User`'s 2FA columns exactly; `ITotpService` gained one new
   method, `GenerateCode(secret)` (the server computes the *current* code and hands it to the
   wallet screen) — the mirror image of the existing `VerifyCode` used for staff 2FA login. The QR
   payload itself (`SGLOY1.{membershipId}.{code}`) is a thin, new, deliberately simple wrapper —
   version tag + membership id for O(1) staff-side lookup + the rotating TOTP code — not a new
   cryptographic primitive. Anti-replay reuses the same "atomic claim of a monotonically
   increasing counter" shape `ProductStock`'s optimistic concurrency already established in this
   codebase (`ILoyaltyRepository.TryClaimTimestepAsync`, a single WHERE-guarded
   `ExecuteSqlInterpolatedAsync` UPDATE — verified genuinely atomic and parameterized by
   security-reviewer, TASK-412 item #3). Rejected: inventing a bespoke rotating-token scheme — it
   would duplicate exactly what TOTP already does correctly and add a second, unaudited crypto
   primitive to the codebase for no behavioral gain.

3. **`"loyalty"` and `"marketing_analytics"` are two independent `Tenant.modules` keys, not one.**
   A tenant can run a bonus program without ever activating the RFM dashboard (e.g. a small
   single-store client that wants scan-to-earn but has no marketing analyst to act on
   segmentation), and — less obviously but just as real — a tenant could in principle activate
   `marketing_analytics` without `loyalty` at all, since Фаза 1's RFM engine only needs
   `PosTransaction.CustomerId` populated, which POS's plain customer-search-and-attach path
   (`CustomerId` alone, no membership/balance involved) already provides independently of the
   bonus mechanism. Coupling both features behind a single module key would force an all-or-
   nothing activation that doesn't match either real usage pattern, and would entangle two
   independently-evolving features' rollout/pricing decisions behind one flag. Both keys were
   added to `Tenant.UpdateModules`'s `valid[]` list together (TASK-405) since Фаза 1 depends on
   Фаза 0's data-writing path existing, but they gate unrelated endpoint sets
   (`[RequireModule("loyalty")]` on `LoyaltyController` vs. `[RequireModule("marketing_analytics")]`
   on `MarketingAnalyticsController`) and can be toggled independently per tenant from day one.

4. **Naming discipline: always `RfmSegment...` (`RfmSegmentKey`, `RfmSegmentDetailDto`, ...),
   never a bare `Segment...`.** `Item.Segment` (nav property to `ProductSegment`) already means the
   promo-cannibalization demand segment from v2 (`Features/Cannibalization/`) — confirmed in
   `Item.cs` before any RFM code was written. A bare `SegmentDto`/`ISegmentService` in a new
   `Features/MarketingAnalytics/` module would silently collide in meaning (not in compiler-checked
   namespace — C# would happily compile two different `SegmentDto`s in two namespaces — but in the
   mind of every future reader grepping "Segment" across the codebase) with an unrelated,
   already-shipped concept. `RfmSegmentKey`'s own doc comment states this explicitly, and the
   convention is applied consistently across the whole new module — DTOs, the classifier, the
   repository methods, the frontend `types.ts` transcription (TASK-409) — with zero exceptions.

Consequences:
+ Cross-tenant loyalty wallet works with zero compromise to `Customer`'s existing tenant-scoped
  uniqueness semantics — no migration risk to the many existing tenant-scoped tables' assumption
  that a phone number is only meaningful within one tenant
+ Zero new cryptographic primitive — the QR "liveness" mechanism is exactly as auditable as the
  already-shipped, already-reviewed 2FA TOTP path, just pointed at a different entity
+ Independent module activation matches real tenant variation (bonus-only, analytics-only-via-
  plain-attach, or both) without a forced bundle
+ `RfmSegment...` naming avoids a real, confirmed collision with `ProductSegment`'s existing meaning
- `ConsumerAccount` carrying no RLS is a permanent, deliberate exception to this codebase's
  otherwise-universal "every tenant-touching table gets RLS" rule — every future reader must learn
  this is intentional (documented in three places: the migration's own class doc comment,
  `database-schema.md`, and this ADR) rather than assume it is an oversight
- Two module keys instead of one is marginally more provider-panel/admin-panel surface (two
  checkboxes, two i18n label pairs) for a feature pair that, in practice, most tenants will
  probably activate together — accepted, since the independent-activation case is real, not
  hypothetical
- `LoyaltyMembership`/`LoyaltyLedgerEntry`'s identity-based `consumer_self_access` RLS policy
  (`database-schema.md`) is the first of its kind in this repo — a new pattern future agents must
  learn alongside the existing role-based `tenant_isolation`/`provider_bypass`/`worker_bypass` triad

Extends: reuses ADR-020's `TenantRoleCapabilities`/`RoleOrCapabilityRequirement` mechanism
verbatim for `marketing_analytics.view`/`marketing_analytics.export_pii` (new "Маркетинг"
capability group) — no new authorization primitive introduced for Фаза 1's own access control.

**Addendum (TASK-419/420, 2026-07-27) — Фаза 2 price segments + frequency/reactivation.** Same
plan (`deep-cooking-nygaard.md` §"Фази 2-4"), same module key (`marketing_analytics`, no new one),
same `RfmSegment`-style naming discipline extended to `PriceSegmentKey`/`PriceAudienceKey`/
`FrequencyAudienceKey`. Three decisions worth recording:

1. **`PERCENTILE_CONT` (ordered-set aggregate), not `NTILE` (window function), for price-segment
   boundaries — a different quantile primitive than Фаза 1, deliberately.** Фаза 1's R/F/M scoring
   needs a per-customer **bucket assignment relative to the current query's own rows**
   (`NTILE(5)` — "which fifth is this customer in, among these rows, right now") and is always
   recomputed fresh; the assignment is never reused as a standalone number. Фаза 2 needs the
   opposite: an actual **₴ cutoff value** (P20/P40/.../P97) that must mean the same thing across
   three separate call sites — the comparison table, the all-time table, and the frequency tab's
   `priceSegment` filter all need to agree on what "Tier3" *is* in currency terms, not just which
   rows fall in it this query. `NTILE` has no notion of an interpolated cutoff that survives outside
   the query that produced it; `PERCENTILE_CONT(0.20/.../0.97) WITHIN GROUP (ORDER BY
   median_check)` computes exactly that reusable boundary, which `PriceSegmentCatalog.RangeLabelUa`
   renders as `"120–190 ₴"`. Implementation trap, not a design point: every `PERCENTILE_CONT` call
   must be cast `::numeric` — Postgres always returns `double precision` from it regardless of the
   input column's type, caught live when 7/10 of TASK-420's integration tests threw
   `InvalidCastException` before the cast was added at all 15 call sites (task log 420).

2. **Segment boundaries are computed all-time, never from the active comparison window.**
   `PriceSegmentsRepository.GetBoundariesAsync` carries no date filter at all — one P20..P97 cutoff
   set per tenant, shared by the 30/60/90-day comparison view, the all-time view, and the frequency
   tab's segment filter alike. Not an arbitrary simplification: the competitor analysis
   (`docs/uployal/PRICE_SEGMENTS_ANALYSIS.md` §8.3) directly observed the competitor's own
   boundaries holding identical across every period it tested and concluded "це вказує на мережеві,
   а не періодичні межі сегментації" — empirically confirmed competitor behavior, not a guess filled
   in where the source was silent. Recomputing boundaries per-window would also make a customer's
   tier label mean a different ₴ range depending only on which period filter happens to be active —
   actively confusing for a label whose whole purpose is a stable, nameable price tier.

3. **`Stable` (comparison mode) ships as a full first-class `PriceAudienceKey` member from day
   one — list, sort, paginate, export, and a real recommendation — not just the KPI number the
   competitor limits it to.** The competitor computes and displays a `Стабільні` count but
   deliberately gives it no card/list/export (analysis doc §7.4/§25.3 flags this as a functional gap,
   not a design worth copying). Since `PriceAudienceKey`/`PriceSegmentCatalog`/the repository's
   shared classification CASE ladder already treat all 4 audiences identically end-to-end, full
   parity for `Stable` cost nothing beyond the 4th enum member and its recommendation copy.

Consequences: (+) tier labels stay stable, comparable numbers across every view instead of shifting
meaning per filter; (+) `Stable` gives marketers a genuine "protect this base" workflow the
competitor's page structurally can't offer; (-) a brand-new tenant with little history gets
boundaries computed over a small all-time sample — `PriceSegmentSettings.
MinReceiptsForBoundaries` is persisted but not yet read by `GetBoundariesAsync`, flagged by
security-reviewer (TASK-422) as an inert functional gap for a follow-up task, not a security one.

**Addendum (TASK-428/429/431, 2026-07-27) — Фаза 3 AudienceBuilder: accept the Seq Scan; do not
mark `texticlike` LEAKPROOF or add a SECURITY DEFINER search function for v1.**

Context: TASK-428 (database-engineer) live-verified that `idx_items_name_trgm` — the new GIN
trigram index added specifically for AudienceBuilder's text-term search — is **structurally
unusable** by the query planner on the real, RLS-protected app connection. `items` has canonical
RLS + `FORCE ROW LEVEL SECURITY`; `ILIKE` compiles to `texticlike`, which Postgres's own `pg_proc`
marks `proleakproof = false`. Under `FORCE ROW LEVEL SECURITY`, a predicate built from a
non-LEAKPROOF function can only be applied as a post-scan `Filter`, never pushed into an index
condition — this holds even for the table owner. Live-measured: ~1085ms Seq Scan (real app role,
500k synthetic rows, rolled back after) vs ~2ms Bitmap Index Scan (superuser bypassing RLS, same
index/data; `enable_seqscan=off` on the app-role side still produced no index plan at all — proof
the planner has no alternative, not merely a deprioritized one). Not new to this feature: the same
live test against the pre-existing `idx_notification_queue_title_trgm` shows the identical
Filter-not-Index-Cond behavior — that index has, as best this session could tell, never actually
accelerated a real tenant-scoped keyword search in production either.

Three options were on the table (TASK-428's log; decided by the orchestrating session before
TASK-429 began, per CLAUDE.md's clarify-before-implementing gate — marking a core Postgres
function LEAKPROOF is a schema-wide security-posture change, not an isolated indexing decision):

1. **Mark `texticlike` (and related pattern-matching support functions) `LEAKPROOF`.** Would fix
   the index path for every RLS table using LIKE/ILIKE across the whole codebase, not just
   `items` — broadest fix, broadest blast radius. Rejected for v1: `LEAKPROOF` is Postgres's
   promise that a function reveals nothing about its arguments through side channels (errors,
   timing) to a caller who shouldn't see the underlying rows — asserting that for a core
   string-matching primitive used everywhere is a real security claim about timing side-channels
   that needs its own dedicated review, not a decision to make as a side effect of one feature's
   index tuning.
2. **A `SECURITY DEFINER` search function**, owned by a privileged role, that bypasses RLS
   internally but re-applies its own hardcoded, provably-safe `TenantId = current_setting(...)`
   guard before returning rows — narrower blast radius than (1) (scoped to whichever call sites
   adopt it, not every ILIKE in the codebase), same spirit as the existing `provider_bypass`/
   `worker_bypass` policy escape hatches. Rejected for v1: still a new, hand-written RLS-bypass
   surface that has to be gotten exactly right (the whole point of RLS is that the tenant guard is
   enforced uniformly by Postgres, not re-implemented correctly by every function that opts out of
   it) — worth building only if the Seq Scan cost actually becomes a measured problem.
3. **Accept the Seq Scan at realistic per-tenant catalog sizes, change nothing.** `items.Name`
   text search is a "type a term, press Enter" field, not a live-autocomplete search — at the scale
   this actually runs at (thousands of SKUs per tenant, not the 500k-row/all-tenants synthetic
   worst case TASK-428 tested), a few hundred milliseconds is not a UX problem worth a new
   security-posture decision to solve pre-emptively. **Chosen.**

Option 3 was picked as the most conservative of the three: it changes zero existing security
posture, defers both (1) and (2) as available future fixes rather than foreclosing them, and costs
nothing beyond documented latency at a scale this feature doesn't run at today. The tradeoff is
recorded redundantly in code (not just here), so a future reader doesn't have to rediscover it from
scratch: `IAudienceBuilderRepository`'s class-level doc comment, `AudienceBuilderRepository`'s
class doc comment, and an inline comment on `SearchCategoriesAsync` (the categories-`ILIKE` path
has the identical tradeoff, smaller table) — all three cite TASK-428's actual measurement.
security-reviewer (TASK-431) independently re-verified this is a **performance-only** tradeoff, not
a tenant-isolation bypass: TASK-428's own `EXPLAIN ANALYZE` shows the RLS tenant predicate still
applies as a `Filter` regardless of the index question, and every AudienceBuilder CTE additionally
carries its own redundant, explicit `TenantId = {0}` filter on top of whatever RLS does
(defense-in-depth, consistent with existing repository convention) — only query latency at large
multi-tenant catalog sizes is the accepted cost, never correctness or isolation.

Consequences: (+) zero new security-posture surface, zero new attack surface, decision fully
reversible later if (1) or (2) becomes worth it; (+) the same tradeoff note now also explains why
the pre-existing `idx_notification_queue_title_trgm` has likely never helped production either,
closing a question that would otherwise have resurfaced independently; (-) `idx_items_name_trgm`
is inert on the only connection that matters (the real app role) until (1) or (2) is adopted —
flagged as a known v1 limitation in `database-schema.md`, not a defect to "fix" by re-tuning the
index itself; (-) the identical class of bug (non-LEAKPROOF cross-type comparison functions) can
recur silently for any future raw-SQL query that compares a `timestamptz` column against a bare
`DateOnly`-derived parameter — mitigated here by explicit `::timestamptz` casts at every
`t."CreatedAt"` comparison in `AudienceBuilderRepository` (TASK-428's own side-finding, applied by
TASK-429, confirmed consistent by TASK-431), but the general pattern is worth remembering for the
next raw-SQL repository, not just this one.

**Addendum (TASK-471/472/473/474/477, 2026-08-05/06) — Фаза 4 post-campaign audience analysis:
first persisted entity in the marketing-analytics series, reused RFM/phone-matching infrastructure,
and a two-round XLSX-import security fix.**

Context: same plan (`deep-cooking-nygaard.md` §"Фази 2-4"), same module key (`marketing_analytics`,
no new one), full spec `docs/uployal/AUDIENCE_ANALYSIS.md`. Фаза 4 compares an externally-sourced
list of customers (an SMS blast, a raffle list, a Фаза 3 AudienceBuilder export) against equal
before/after date windows around a campaign — a different question from Фаза 1-3's ("did THIS
specific already-contacted list of people actually come back," not "who bought THIS" or "who is
valuable"). Five decisions worth recording, the last two the most consequential for future readers:

a. **Фаза 4 needs a persisted entity, breaking Фаза 1-3's "fully stateless, computed live on every
   request" precedent — a deliberate, necessary exception, not scope creep.** Every prior mode in
   this series (RFM, Price Segments, Audience Builder) computes its entire response fresh from
   `pos_transactions`/`items`/`customers` on every call, with nothing of its own persisted anywhere
   (TASK-432's own docs pass confirmed this explicitly for Фаза 3: "no new entities... computes
   everything live"). Фаза 4 cannot follow that pattern, for two reasons the source doc requires:
   (1) the customer list itself is **externally sourced** — uploaded once, not derivable from any
   live query — so it has to be stored somewhere between the import call and every later report
   call; (2) the source doc's own §7 ("Чернетка та застосований сегмент") requires "draft" (what's
   currently uploaded) and "analyzed" (what the current report reflects) to be two distinct,
   explicitly-tracked states — re-uploading must NOT silently invalidate an already-computed report
   until the user explicitly re-runs analysis. A stateless design has no way to represent "uploaded
   but not yet analyzed" at all. `PostCampaignSegment`'s `AfterStart`/`AfterEnd`/`BeforeStart`/
   `BeforeEnd` (all `DateOnly?`) ARE that draft-vs-analyzed state directly — no separate
   boolean/enum column exists anywhere in the schema. All four null (together with a null
   `AnalyzedAt`) means draft; all four set means frozen/analyzed. `POST .../segments/{id}/analyze`
   is the only place that ever writes them, and re-running it on an already-analyzed segment
   overwrites all four (plus `SegmentHash`/`AnalyzedAt`) in place rather than minting a new segment
   row. See `database-schema.md` TASK-471 and `glossary.md` "Draft vs. analyzed segment" for the
   schema/wire detail.

b. **Import matches an uploaded token against `Customer.Id` (GUID) OR the customer's normalized
   phone — reusing Фаза 0's existing phone-matching infrastructure verbatim, not a new identity
   concept.** `SegmentImportParser.Classify` calls the same `PhoneNormalizer.Normalize` that Фаза
   0's consumer registration/login and POS customer-attach flows already use, behind the same
   strict character-class pre-check discipline (never handed arbitrary text — the source doc's
   §5.3 competitor-bug avoidance; TASK-474 item 4 independently re-verified every adversarial case
   by hand). No second phone representation, no bespoke matching table —
   `PostCampaignRepository.FindCustomersByIdsOrPhonesAsync` is a plain `Customer.Id IN (...) OR
   Customer.Phone IN (...)` bulk lookup against the tenant's existing `Customer` rows, the same
   `list.Contains(x) => ANY(@p)` EF translation `MarketingAnalyticsRepository.
   GetExportCustomersAsync` already established. Phone matching is exact-string against whatever
   normalized format is actually stored — the same known limitation `LoyaltyService.
   FindOrCreateCustomerAsync` already accepts elsewhere in this codebase, not a new gap.

c. **The RFM migration matrix reuses `IMarketingAnalyticsRepository.GetScoredCustomersAsync` +
   `RfmSegmentClassifier` completely unchanged, called a THIRD time (all-time) — the single most
   reusable/elegant piece of this phase.** `GET .../segments/{id}/migration` (and
   `GET .../customers`, which needs the same before/after RFM key per row) needs to classify each
   matched customer's RFM segment independently in the before window and the after window, then
   cross-tabulate into a 12×12 transition matrix. Rather than write a second RFM implementation,
   `PostCampaignService.ComputeRfmKeysAsync` calls Фаза 1's existing, unmodified
   `GetScoredCustomersAsync` three times: once for the before window, once for the after window,
   and — new to this feature — once **all-time** (`DateOnly.MinValue` through the after-window's
   end, the same "all" period convention `MarketingAnalyticsController.ResolvePeriod` already
   uses). The third call's only purpose: telling apart, for a customer absent from a given window's
   scored rows, (1) genuinely zero purchases ever (Фаза 1's own "Без покупок" null bucket) from (2)
   real all-time purchase history that simply has none in this specific window — which must
   classify as an ordinary (if low-R) real segment, never null. `ClassifyForWindow` resolves case
   (2) by feeding the SAME `RfmSegmentClassifier.Classify` a sentinel worst-case R=F=M=1 alongside
   the customer's real lifetime facts (first-purchase-date/lifetime-receipt-count/last-purchase-date
   — all window-independent per `RfmScoredCustomerRow`'s own doc comment, so any of the three
   calls' row is an equally valid source for them). Verified by hand and by test: case (2) resolves
   to `Hibernating` (R≤2 ∧ F≤2 ∧ M≤2), never null. Zero changes to `IMarketingAnalyticsRepository`
   itself — this is three ordinary calls to its existing public method, not a new overload or a
   forked classifier. A future reader building a fifth marketing-analytics mode that needs "segment
   membership as of some window, correctly distinguishing never-purchased from zero-in-window"
   should reach for this exact three-call pattern rather than re-deriving it.

d. **The XLSX import security story — verify the fix, not just the intent; this is now the
   required pattern for every file-upload feature in this codebase, not just this one.**
   TASK-474 (security-reviewer) found a HIGH resource-exhaustion risk: `ExcelImportService.
   ParseXlsx` copied every cell into memory with no size guard, and `PostCampaignService.
   MaxAcceptedRows` (20,000) was only checked afterward. TASK-477's first fix looked reasonable on
   its face — add `ImportLimits.MaxRows`/`MaxColumns` (25,000/300, ~1.25x the real business cap)
   and reject based on the parsed range's `RowCount()`/`ColumnCount()` **before** the per-cell
   `GetString()` copy loop ran. It shipped, tested green, and was recorded as closing finding A.

   It did not close finding A. A same-day empirical follow-up (a throwaway xUnit probe, not
   committed — see TASK-477's own log addendum for the full method) measured `new
   XLWorkbook(stream)` — ClosedXML's constructor, called BEFORE any row/column count is available
   to check — in isolation, against synthetic `.xlsx` files built via direct ZIP-entry surgery (the
   classic OOXML zip-bomb shape: every cell references the same shared-strings-table entry, so the
   file stays tiny on disk regardless of row count). The result: the constructor alone performs the
   full, expensive per-cell materialization; checking `RowCount()`/`ColumnCount()` afterward guards
   a loop that was never the expensive part. Measured numbers, release build / .NET 8 / ClosedXML
   0.105.1, a file comfortably under the controller's 10 MB upload cap in every row:

   | rows | file on disk | `new XLWorkbook(stream)` alone |
   |---|---|---|
   | 25,000 (the row guard's own ceiling) | 0.12 MB | 374 ms / 41.6 MB allocated |
   | 250,000 | 1.17 MB | 4,866 ms / 410.8 MB allocated |
   | 1,048,576 (Excel's own hard row ceiling) | 4.86 MB | 37,703 ms / 1,725.8 MB allocated (~496 MB retained live after a forced GC) |

   A **file under 5 MB** — well inside the already-correctly-enforced 10 MB request cap — costs
   roughly **~38 seconds of wall time and ~1.7 GB allocated inside the constructor call by
   itself**, in a shared, multi-tenant API process where one tenant's crafted upload can degrade or
   hang request handling for every other tenant. Cost also scales super-linearly with row count
   (~10x rows from 25k→250k gave ~13-15x constructor time), so the exposure gets
   disproportionately worse the closer an attacker pushes toward what the 10 MB cap and Excel's own
   row ceiling allow.

   The real fix had to run BEFORE ClosedXML ever touches the stream, at the ZIP-container level,
   not the parsed-workbook level: a `.xlsx` is a standard ZIP archive, so `System.IO.Compression.
   ZipArchive` + `ZipArchiveEntry.Length` can read every part's real UNCOMPRESSED size directly off
   the ZIP central directory — no decompression required, confirmed empirically cheap (0-9 ms even
   against an entry that decompresses to 85 MB) regardless of the declared size. `ImportLimits.
   MaxUncompressedZipEntryBytes` (20 MB — real headroom over the ~2 MB a legitimate 25,000-row/
   few-column workbook actually needs, per the same measurement, while firmly rejecting the
   demonstrated attack sizes) now gates `ExcelImportService.ParseXlsx` before the `XLWorkbook`
   constructor runs at all. The original row/column guard was not deleted — it still catches a
   workbook that passes the ZIP-size check but is unreasonably tall/wide within that budget — but
   it is no longer the layer doing the actual resource-exhaustion protection; that is now the
   ZIP-level check.

   **This two-layer guard shape — a cheap container-level size check BEFORE the expensive parse,
   THEN a structural row/column check after parsing — is now the required pattern for this
   codebase's next file-upload feature, not merely a fact about this one.**
   `IExcelImportService`'s own doc comment already frames itself as shared, reusable infrastructure
   for "any future feature needing to let the user upload an .xlsx" — Фаза 4 is the first
   file-upload feature in the entire codebase (Фаза 0-3's exports only ever *produce* `.xlsx` files
   via the already-hardened `ExcelExportService`; nothing before this task ever consumed one), so
   there was no prior convention to inherit, and this addendum's measured numbers are now that
   convention's evidence base. A future agent adding a second upload-accepting feature should read
   `ImportLimits`'s own doc comment and this addendum before assuming a post-parse size/count check
   is sufficient on its own — it measurably is not, for any library that fully deserializes its
   input before exposing any way to bound the work.

e. **`CanImportSegments` is role-only (`AtLeastStoreManagerRoles`), deliberately with no new
   `TenantRoleCapabilities` catalog entry — narrower than the read-only report tabs it sits
   alongside.** TASK-474 finding B: the source doc's §32 explicitly calls for "окреме право на
   upload" (a separate upload-specific permission), and as originally shipped `Import` shared the
   exact same `MarketingAnalyticsViewOrCapability` floor as every read-only report GET on the same
   controller — meaning the population that could trigger finding A's resource-exhaustion risk was
   no smaller than the population that could merely view an already-analyzed report. TASK-477
   considered two shapes: a new `marketing_analytics.import`-style capability (mirroring
   `MarketingAnalyticsExportPii`'s own shape), or reusing `AppPolicies.AtLeastStoreManagerRoles`
   directly with no capability-widening escape hatch at all (matching `CanExportPii`'s own default
   floor, minus the capability branch). It chose the second, narrower option, for the same reason
   `TenantRoleCapabilities.ReceiptsView`'s own doc comment already gives for excluding
   Create/Receive/Cancel from the capability catalog (ADR-020 point 3): a write-heavy, cost-bearing
   action does not automatically earn a delegable capability just because its sibling read actions
   have one. Import creates DB rows and, per finding A, is this controller's single most
   resource-costly action — exactly the shape that precedent says to keep role-gated rather than
   capability-delegable. A tenant that genuinely needs a sub-store_manager "marketing specialist"
   role to import segments can still grant `store_manager` outright; a dedicated capability can be
   added later if that specific need actually materializes, rather than speculatively widening the
   catalog now. `MarketingAnalyticsAuthorization.CanImportSegments` follows `CanExportPii`'s
   existing imperative, in-body-check shape (needed for the same reason: it narrows ONE action
   within an otherwise class-level-gated controller, so it cannot be a blanket `[Authorize]` policy
   attribute) but returns only the role check, no capability branch — confirmed by dedicated tests
   that a capability holder alone (without the role) is correctly still rejected.

Consequences: (+) the draft-vs-analyzed persisted state finally lets this series represent
"uploaded, not yet analyzed" at all, which no prior Фаза could express; (+) zero duplicate RFM
logic anywhere in the codebase — a third mode now reuses the exact same classifier a third,
independent way; (+) the two-layer XLSX guard is a concrete, measured pattern the next upload
feature can adopt directly rather than re-discovering the same gap from scratch; (-) Фаза 4 is now
the one mode in this whole series an operator must remember has real, growing storage (segments +
members), unlike Фаза 1-3's zero-footprint designs — no retention/cleanup policy exists yet for
old/abandoned draft segments, a candidate for a future follow-up, not filed as a
`known-issues.md` entry by this task (out of scope per this task's own brief); (-) `Import`'s
role-only floor means a tenant cannot delegate "upload segments" to a capability-holding
non-store_manager the way it can delegate `marketing_analytics.export_pii` or `.view` — an
intentional, not accidental, gap per point (e) above.

## ADR-022: Store-scoped user assignment & data visibility (`user_locations` + RLS)
Date: 2026-07-19
Status: accepted (Stage 1 live in production; Stage 3 written and tested but deliberately not
deployed — see rollout checklist)

Context: `User.StoreId` ("assigned home store") has existed since `AddAuth` (2026-06-03) but was
a dead field — unmapped (no `HasColumnName`), no FK, no index, and no code path anywhere ever
read it for access control (unlike the ~19 other pre-v4 entities carried through
`V4LocationsRename`). Meanwhile every store-scoped business table (`product_stock`,
`daily_sales`, `pos_shifts`, etc.) is only tenant-isolated by RLS — any user in a tenant sees
every store's data tenant-wide regardless of role. Product owner asked for real store-scoped
visibility: a `store_manager`/`cashier`/etc. should see only their assigned store(s)' stock/
sales/POS/write-offs, not the whole tenant.

Decision:
1. **`enterprise_admin` — unconditional bypass.** No `user_locations` rows needed or ever
   written for this rank. Every other rank (`network_manager`, `store_manager`, `merchandiser`,
   `storekeeper`, `cashier`, `staff`) is scoped through a new many-to-many `user_locations`
   table — **including single-location roles**, which get exactly one row rather than being
   special-cased through `User.StoreId`. One enforcement mechanism for every restricted rank,
   not a shortcut for the common single-store case.
2. **New `user_locations` table**: `Id`, `TenantId` (direct column with its own leading index —
   Stage 3's RLS policy will `EXISTS`-subquery into this table from 9 other tables, so it needs
   to be efficiently scannable on its own), `UserId` (FK→users, Cascade), `LocationId`
   (FK→locations, Cascade), `AssignedByUserId` (FK→users, SetNull, audit field), `CreatedAt`.
   Unique `(TenantId, UserId, LocationId)` + secondary `(TenantId, LocationId)`. No soft-delete —
   pure leaf assignment table, hard DELETE revokes. RLS at this stage is the standard
   `tenant_isolation`/`provider_bypass`/`worker_bypass` triad only — **not** yet the RESTRICTIVE
   store-scope policy (that is Stage 3, point 5 below); nothing reads this table for access
   control until then.
3. **`User.StoreId` fixed, not removed.** Now correctly `.HasColumnName("LocationId")` +
   `SetNull` FK to `locations` (same nullable/optional shape as `ProviderRoleId`/`TenantRoleId`).
   It stays a UI/invite-time "default home location" hint only — **never** read by access-control
   enforcement. `user_locations` is the single source of truth for that; the two must not be
   conflated.
4. **API**: `PUT` / `GET /api/users/{id}/locations` (full-replace / current list) —
   `AtLeastEnterpriseAdmin`-only, **no** capability-OR bypass, same anti-escalation posture as
   `AssignTenantRole` (ADR-020) — this endpoint decides what real business data a whole role
   will see once Stage 3 lands, so a `users.manage` capability holder must never be able to grant
   it to themselves or others. `UserService.InviteAsync`/`UpdateAsync` write the single
   `user_locations` row automatically for single-location roles (`store_manager, merchandiser,
   storekeeper, cashier, staff`) from the existing `storeId` field; `network_manager`'s
   (potentially multi-location) assignment is managed only through the dedicated endpoint. New
   `ILocationService.BelongsToTenantAsync` closes a pre-existing gap where `storeId` accepted any
   GUID with zero tenant-ownership check.
5. **Three-stage rollout — deliberately not one migration:**
   - **Stage 1 (deployed to production)** — additive schema + `user_locations` API + assignment
     UI (invite modal, user detail panel, `UserLocationsEditor`). Zero behavior change: nothing
     queries `user_locations` for access control yet.
   - **Stage 2 (not code — a manual, per-tenant admin task)** — every existing
     `network_manager`/`store_manager`/`merchandiser`/`storekeeper`/`cashier`/`staff` user must
     get at least one `user_locations` row via the Stage 1 UI/API before Stage 3 can safely
     apply. Tracked via a coverage-gap SQL report in
     `.claude/docs/store-scope-rollout-checklist.md`.
   - **Stage 3 (written, tested, held back)** — RESTRICTIVE `store_scope` RLS policy,
     `EXISTS`-scoped through `user_locations`, on 9 tables: `product_stock`, `daily_sales`,
     `pos_shifts`, `pos_transactions`, `write_offs`, `discounts`, `stock_receipts` (one-sided,
     `DestinationLocationId` — a receipt comes from a supplier, not another store),
     `stock_movements`/`stock_transfers` (two-sided OR-match, `From`/`ToLocationId`). Bypass
     roles: `provider`, `provider_admin` (added beyond the original brief — it already has full
     bypass parity with `provider` via the pre-existing `provider_bypass` policy on these same
     tables; omitting it here would have silently regressed that already-audited access),
     `worker`, `enterprise_admin`. Migration `AddLocationStoreScopeRlsPolicies` exists, is fully
     tested (9 new xunit integration tests + manual live-verification scenarios against the real
     non-superuser app role, rollback/reapply round-trip confirmed), and is committed **only** on
     local branch `stage3-rls-enforcement-hold` — **not merged to `main`, not deployed anywhere.**
6. **Fail-closed, product-owner-confirmed.** The instant Stage 3's policy applies, a user in a
   scoped role with **zero** `user_locations` rows sees **zero** rows on all 9 tables — not a
   bypass, not a tenant-wide fallback. This is why Stage 2's backfill must reach zero gap
   *before* Stage 3 can ever be applied to a real environment; applying it early is an immediate,
   total functional outage for every un-backfilled user (their whole job — stock, sales, POS,
   write-offs — goes blank at once, tenant-wide, the moment the migration commits). Full gating
   procedure, the coverage-gap query, and the emergency rollback command live in
   `.claude/docs/store-scope-rollout-checklist.md` — not duplicated here.
7. **Child tables need no new policy** (`stock_receipt_items`, `stock_transfer_items`,
   `write_off_items`, `pos_transaction_items`) — Postgres re-applies a referenced table's
   RESTRICTIVE RLS inside any subquery/join that reads it, so they inherit the new scoping
   through their existing parent-`EXISTS` `tenant_isolation` policy for free, same mechanism
   `supplier_chat_messages` already relies on (ADR-017 era).

Consequences:
+ Single enforcement mechanism (`user_locations`) for every restricted rank — no special-cased
  single-store shortcut to keep in sync with the multi-store path
+ `enterprise_admin`/`provider`/`provider_admin`/`worker` bypass paths are unconditional and
  unchanged — zero risk of locking out administrative or platform-operational access
+ Explicit three-stage gate keeps the highest-risk step (Stage 3) reversible right up until the
  moment it's applied, and cheaply reversible after (`Down()` drops all 9 policies in one shot)
+ Child tables inherit store-scoping for free through existing parent-`EXISTS` policies — no
  additional migration surface
- Real operational dependency on Stage 2 being done *thoroughly* — a single missed user in any
  tenant sees a complete, immediate outage the moment Stage 3 ships; the rollout checklist's
  coverage-gap report is the only safety net and must be re-run right before cutover, not just once
- `User.StoreId` now has two "home location" concepts (the legacy hint field, and the real
  `user_locations` rows) a future reader could conflate — mitigated by the code comment on
  `User.StoreId` and this ADR stating explicitly it is UI-hint-only, never an access-control input
- Stage 3 sits on a long-lived side branch (`stage3-rls-enforcement-hold`) rather than `main` —
  normal branch-hygiene drift risk while it waits, accepted deliberately since merging code that
  isn't safe to run yet would invite an accidental deploy

Extends: ADR-020 (reuses its `AtLeastEnterpriseAdmin`-only, no-capability-bypass anti-escalation
posture for the new location-assignment endpoints).

## ADR-021: TenantRole — per-role sidebar tab visibility (`AllowedTabs`)
Date: 2026-07-19
Status: accepted (Tier 1 enforcement only — see point 5; Tier 2 explicitly deferred)

Context: ADR-020's `TenantRoleCapabilities` gates backend *actions* (can this capability holder
call this endpoint) — it says nothing about sidebar *visibility*. This left a real, confirmed
gap: a user granted e.g. `analytics.view` via a TenantRole template, but whose base `Role` rank
is below whatever `Sidebar.tsx` requires for the "Аналітика" NavGroup, passes every backend check
ADR-020 wired for them yet has no navigable link to the data they can legitimately call the API
for. The same shape recurs for `users.manage`/`schedules.manage` (workforce). Confirmed by
reading `Sidebar.tsx`'s `buildNavGroups()`/NavItem `roles` arrays directly against ADR-020's 8
gated controllers — the mismatch is real, not hypothetical.

Decision:
1. New `TenantRole.AllowedTabs: List<string>` column (`text[]`, default `[]`) — deliberately
   **the same storage shape as `Capabilities`**, not the `jsonb` the initial task brief assumed:
   the real `Capabilities` column (`AppDbContext.cs`) is a native Postgres `text[]`, matching
   `ProviderRole.Permissions`/`SupplierRole.Permissions` exactly, with no `HasConversion`/
   `EnableDynamicJson`. `AllowedTabs` follows that verified, three-entity-precedent pattern
   rather than the brief's unchecked wording.
2. **Fixed catalog of 10 tab keys** (`TenantRoleTabs`, `ShelfGuard.Domain.Constants`, mirrors
   `TenantRoleCapabilities`'s shape): `dashboard, operations, sales, procurement, marketplace,
   auto_service, production, analytics, workforce, support`. Verified 1:1 against `Sidebar.tsx`'s
   real `NavGroup.key` values (9 groups) plus the standalone `dashboard` NavItem (not a
   NavGroup, but a real, separate nav destination). Labels copied verbatim from
   `frontend/messages/uk.json`, not re-authored.
3. **Deliberately excluded, forever**: `admin` (provider-only NavGroup — a tenant-scoped
   TenantRole must never unlock the provider panel), `supplier_cabinet` (supplier_admin-only,
   governed by the separate `SupplierRole` mechanism), `settings` (always-visible
   personal-preferences NavItem, not a business module — nothing there is meant to be hidden
   per role).
4. **Additive, same compositional principle as `Capabilities`** — `AllowedTabs` only ever
   *widens* what a user sees beyond their base `Role`'s default nav; it never narrows or
   replaces the existing role-based sidebar/route logic.
5. **Enforcement is two-tier, and only Tier 1 exists today:**
   - **Tier 1 (real, live today):** for the tabs that correspond to an ADR-020 capability
     already wired to a real backend gate (`workforce` → `users.manage`/`schedules.manage`,
     `analytics` → `analytics.view`), granting the matching capability *and* the tab together is
     coherent end-to-end — the capability is what the backend actually checks; the tab is what
     makes the frontend show the link and pass the new `useRequireTab` page guard. This is the
     only case where `AllowedTabs` sits in front of something the backend genuinely enforces.
   - **Tier 2 (explicitly deferred, not built this wave):** the remaining tab keys (`sales`,
     `procurement`, `marketplace`, `auto_service`, `production`, `support`, plus `dashboard`/
     `operations`) have no matching ADR-020 capability at all today. Granting one of these makes
     the sidebar link appear (`Sidebar.tsx`'s tab check is generic across all 10 keys), but
     nothing server-side or page-level consults it — the destination page/API falls back to
     whatever role-only gate (or absence of one) already existed. This is a UX gap (a link that
     may lead to a page/API that still says no — not a security hole, since `AllowedTabs` never
     grants backend access on its own), to be closed only if/when new capabilities
     (`sales.view`, `marketplace.view`, etc.) or a generic `TabOrRoleRequirement`/Handler are
     built. Not scheduled — build only if a real specialty template needs it.
6. **Frontend wiring**: `Sidebar.tsx` computes `tabsSet` from `me.tabs` (null when empty/absent)
   and OR's it into the NavGroup visibility filter, positioned after the Legal Entities
   special-case and before the generic `item.roles` check — it bypasses only the coarse role
   check, never the narrower Legal Entities gate. New `useRequireTab(tabKey, alreadyAllowed)`
   hook is a page-level route guard: `effectiveAccess = alreadyAllowed || me.tabs.includes(tabKey)`,
   redirects to `/dashboard` otherwise. Wired to 3 pages so far: `/users` (tightens direct-URL
   access below store_manager rank — the actual point of the feature, closing a page that
   previously had no page-level gate at all), `/schedules` (wired but inert — that page has no
   role restriction to begin with, every role already reaches it), `/analytics` (also fixed the
   page's own pre-existing `access` variable to fold in the hook's result, so a tabs-granted user
   doesn't hit a dead sidebar link followed by an `AccessDenied` page).
7. **JWT/`AuthUserDto` plumbing mirrors ADR-020's Capabilities mechanism exactly**:
   `AuthService.BuildEffectiveTabsAsync` (parallel to `BuildEffectiveCapabilitiesAsync`, same
   null/archived-role handling, deliberately a *separate* `TenantRole` read — Tabs and
   Capabilities are independent axes per point 5, so one being empty must never suppress the
   other), comma-joined JWT `tabs` claim (absent when empty), new `AuthUserDto.Tabs`.
   `GET /api/tenant-roles/tabs` (`AtLeastEnterpriseAdmin`, same gate as `/capabilities`) serves
   the catalog for the role-editor UI.

Consequences:
+ Closes a real, confirmed capability-vs-visibility mismatch for the 3 capabilities ADR-020
  already enforces (`users.manage`, `schedules.manage`, `analytics.view`)
+ Zero behavior change for any user with no `TenantRoleId` — the `tabs` claim is simply absent,
  and `useRequireTab`'s OR degrades to whatever gate already existed
+ Same storage/JWT/DTO mechanism as Capabilities — one pattern to learn; `TenantRoleTabs.cs`'s
  own doc comment explains why the two lists are kept separate rather than merged into one
- Tier 2 is a known, unclosed gap: granting a tab outside {workforce, analytics} today produces
  a visible-but-not-fully-enforced nav destination — acceptable short-term (no security exposure,
  since `AllowedTabs` never grants backend access by itself) but a real UX rough edge if a
  template ever grants e.g. `sales` alone
- Two independent per-TenantRole axes (`Capabilities`, `AllowedTabs`) to reason about when
  designing a template, rather than one — mitigated by `TenantRoleTabs.cs`'s explicit rationale
  comment and by both following the identical mechanical pattern

Extends: ADR-020 (adds a second, independent per-TenantRole axis — `AllowedTabs` alongside
`Capabilities` — reusing the same storage/JWT/DTO mechanism rather than inventing a new one).

**Addendum (TASK-398, 2026-07-20) — item-level granularity:** product feedback confirmed the
original 10 group-level keys are too coarse (granting `operations` unlocks all 7 pages in that
group at once, no way to grant e.g. only Receipts). Added 27 item-level keys — the literal
`NavItem.href` per page (`"/inventory"`, `"/receipts"`, ...) — unioned into the same
`TenantRoleTabs.All`/`Validate` set as the original 10; no new column, no new JWT claim, no schema
change. The 10 group-level keys are kept exactly as-is, forever, for backward compat with
already-configured templates. `GET /api/tenant-roles/tabs` now returns a hierarchy
(`TenantRoleTabGroupDto[]` — a group's own bulk-grant key plus its nested per-page items; the
standalone Dashboard section has `groupKey: null`) instead of TASK-391b's flat list, so a future
editor UI can offer both granularities. **Deliberately backend/catalog-only** — `Sidebar.tsx`
still only ever checks the group-level key (`tabsSet.has(group.key)`, point 6 above); wiring
item-level enforcement into the sidebar/route guards is a separate, not-yet-scheduled follow-up.
Until then, granting only an item-level key does **nothing** client-side (Sidebar.tsx doesn't read
these keys yet) — one step behind even the Tier 2 status the original 10 keys had at ship time.
One included item is worth flagging for that follow-up: `"/settings/legal-entities"` is a real
Workforce NavItem so it's in the catalog for completeness, but Sidebar.tsx's TASK-397 carve-out
already excludes that one href from `tabsSet` entirely (visibility is `canManageLegalEntities`-only,
by design) — the follow-up should keep excluding it rather than newly wire it up.

## ADR-020: TenantRole — named custom-role templates with real backend capability enforcement
Date: 2026-07-13
Status: accepted

Context: User (enterprise_admin) wants named, reusable custom-role templates ("HR",
"Бухгалтер", "Фінансист", "Відділ закупки" — cashier skipped, already in `AppRoles`), each
an arbitrary capability set, assignable to many users, edited centrally (propagates to all
assignees). Clarified: (1) enforcement must be real on the backend, not UI-only; (2)
templates, not per-user snapshots; (3) sane per-specialty defaults, hand-tunable later via
UI. Precedent: `ProviderRole`/`SupplierRole` (`backend/ShelfGuard.Domain/Entities/
{ProviderRole,SupplierRole}.cs`) — free-form `List<string> Permissions`, resolved via
`User.ProviderRoleId`/`SupplierRoleId`. `User.Role` cannot become a free-form template
string — `AppPolicies.cs` gates ~50 controllers with `RequireRole(fixedRoleArray)`,
entirely independent of `User.Permissions`.

**The blocking discovery**: every controller the 5 specialties need (`SchedulesController`,
`AnalyticsController`, `IntegrationsController`, `OrdersController`, `SuppliersController`,
`ReceiptsController`, `AiOrdersController`, `UsersController`) already gates its *entire*
action set behind one class-level `[Authorize(Policy = X)]` — `AtLeastStoreManager` or
tighter (`CanViewAnalytics`, `CanReceiveStock`) — evaluated by ASP.NET Core's authorization
middleware *before* the action body runs. An imperative in-body check (the
`LegalEntityAuthorization.CanManage` pattern, `backend/ShelfGuard.Infrastructure/
Authorization/LegalEntityAuthorization.cs`) only ever *narrows* access the class-level gate
already let through — it cannot admit a user that gate rejected. `LegalEntitiesController`
works only because its class-level gate (`AtLeastStoreManager`) is *looser* than the
enterprise-admin-only check layered on top. A capability-only user below store_manager
rank is 403'd before any per-action logic runs, on 7 of these 8 controllers.

Decision:
1. **New minimal base role `AppRoles.Staff = "staff"`**, rank 0 (below cashier) in
   `UserService.RoleRank`, added to `UserService.ValidRoles` (invite whitelist) and
   `TenantConnectionInterceptor.ValidRoles` (session `app.role` whitelist) — it is a real,
   sanctioned role string, not a template name. Added to `AppRoles.All`. It is **not** added
   to any existing `AppPolicies` role array — by itself it grants nothing beyond bare auth
   (own profile, own notifications, `GET /api/schedules/my-shifts`, already ungated).
2. **`tenant_roles` table**: `Id, TenantId, Name, Capabilities (jsonb List<string>), IsActive,
   CreatedByUserId, CreatedAt, UpdatedAt`. Partial unique index `(TenantId, Name) WHERE
   "IsActive"`. `User.TenantRoleId Guid?` FK `ON DELETE SET NULL` (mirrors `ProviderRoleId`).
   Archiving sets `IsActive = false`; never hard-deleted (users may still reference it).
3. **New `TenantRoleCapabilities` constants class** (`ShelfGuard.Domain.Constants`, format
   `module.action`, shape of `TenantUserPermissions`) — capability → unlocked actions:
   `users.manage` (HR — Invite/Update/Deactivate on `UsersController`; excludes
   `UpdatePermissions`/`GrantTemporaryPermission`/tenant-role assignment, enterprise_admin-
   only, no escalation path), `schedules.manage` (HR — Create/Update/Delete + shift CRUD on
   `SchedulesController`), `analytics.view` (Бухгалтер/Фінансист — all GET on
   `AnalyticsController`, read-only controller), `integrations.view`/`integrations.manage`
   (Бухгалтер — GetAll/GetByService vs Upsert/Delete on `IntegrationsController`),
   `legal_entities.manage` (Бухгалтер — **reuse** the existing `TenantUserPermissions.
   LegalEntitiesManage` key), `orders.manage` (Закупка — `OrdersController.Calculate`),
   `suppliers.view`/`suppliers.manage` (Закупка — Get* vs Create/Update/Delete on
   `SuppliersController`), `receipts.view` (Закупка — Get* on `ReceiptsController`;
   Create/Receive/Cancel stay role-gated, write-heavy stock path), `ai_orders.view`/
   `ai_orders.manage` (Закупка — Get* vs Generate/Update/Accept/Reject on `AiOrdersController`).
4. **Enforcement — custom `IAuthorizationHandler`, not in-body checks.** New
   `RoleOrCapabilityRequirement(string[] allowedRoles, string capability)` +
   `RoleOrCapabilityHandler` (`ShelfGuard.Infrastructure/Authorization/`) succeeds when the
   caller's role ∈ `allowedRoles` (unchanged for every existing role) **OR** the JWT
   `capabilities` claim contains `capability`. For each capability in point 3, register one
   new named policy in `AppPolicies.Configure` (e.g. `AnalyticsViewOrCapability` =
   `CanViewAnalyticsRoles` ∪ `"analytics.view"`) and move the affected actions from the
   controller's class-level policy to **per-action** `[Authorize(Policy = ...)]` — class-level
   attribute removed only on these 8 controllers. `LegalEntitiesController` instead extends
   `LegalEntityAuthorization.CanManage` to OR-in `TenantRoleAuthorization.HasCapability(user,
   "legal_entities.manage")` — already has the imperative-check shape, no new policy needed.
   **Every other controller (POS, stock write-off, transfers, fiscalization) is untouched.**
5. **`TenantRoleAuthorization.HasCapability(ClaimsPrincipal, string)`** (mirrors
   `LegalEntityAuthorization`) reads the new `capabilities` claim — shared by point 4's
   handler and the `LegalEntitiesController` extension.
6. **Template CRUD**: `TenantRolesController` (`/api/tenant-roles`, GET list/GET id/POST/
   PUT/DELETE-archives) and `POST /api/users/{id}/tenant-role` (new action on
   `UsersController`) — both `[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]`, no
   capability bypass, per the brief's anti-escalation requirement.
7. **JWT merge**: new `AuthService.BuildEffectiveCapabilitiesAsync(User, ct)`, parallel to
   `BuildEffectivePermissionsAsync` (ADR-019) — resolves `user.TenantRoleId` (empty if null
   or inactive) into a `List<string>`. `JwtService.GenerateAccessToken` gets an optional
   `capabilities` param, serialized as a comma-joined claim (shape of `permissions`). Called
   at both mint sites (login, refresh) and fed into `AuthUserDto`. Same ~15-min propagation
   delay already accepted in ADR-019.
8. **RLS**: `tenant_roles` gets `tenant_isolation` + `provider_bypass` + `worker_bypass` in
   one migration — worker will never touch this table, added anyway per convention.
9. **Frontend contract**: new tab on `/users` (or `/users/roles`), reusing the
   `frontend/features/supplier-cabinet/components/RolesTab.tsx` +
   `frontend/features/provider/components/RolesSection.tsx` skeleton — name field + checkbox
   list of capabilities grouped by specialty, sourced from `GET /api/tenant-roles/
   capabilities` (backend is the source of truth for grouping, ADR-017 pattern, not a
   frontend hardcode). Assignment: new `<TenantRoleSelector>` next to `frontend/features/
   users/components/UserPermissionsEditor.tsx`, enterprise_admin-only visible, calling
   `POST /api/users/{id}/tenant-role`.

Consequences:
+ Real backend enforcement — a capability-only user cannot bypass it via direct API calls,
  unlike the page-slug `Permissions` mechanism this extends alongside
+ Zero behavior change for every existing role on every untouched controller — additive,
  OR-composed with the current `RequireRole` arrays; template edits propagate to every
  assignee automatically, bounded by the same JWT delay as ADR-019
+ `legal_entities.manage` reuses the existing key — one definition, two grant paths
- New per-action policy surface (~10 policies) instead of one blanket class-level gate on
  7 controllers — more `AppPolicies.Configure` entries, but each a narrow, auditable OR
- Two role-hierarchy mechanisms now compose (base `Role` rank + `TenantRoleId` capabilities)
  — mitigated by capabilities never granting rank and template management staying
  enterprise_admin-only

## ADR-019: Temporary/permanent access grants beyond role — additive layer over `User.Permissions`
Date: 2026-07-12
Status: accepted

Context: User wants to grant a user MORE access than their role gives — including two users
with the identical role diverging — either forever or until a deadline. Clarified with user
(AskUserQuestion): the existing ~15-min JWT-refresh propagation delay is acceptable (no move to
live per-request DB checks); granularity stays at the existing per-page level (`PAGES` /
`ValidPages` in `frontend/features/users/types.ts` / `UserService.cs`), no new per-action
granularity; expiry must notify the user via the ADR-018 outbox.

Today `User.Permissions` (`backend/ShelfGuard.Domain/Entities/User.cs:42`) is the only
role-independent per-user override — a `Dictionary<string,bool>?` (true=grant, false=deny,
absent=role default), always permanent, edited via `UserService.UpdatePermissionsAsync`
(`UserService.cs:251-297`, private `RoleRank` dict at line 29, mirrored on frontend as
`ROLE_RANK` in `types.ts:87`) and `PUT /api/users/{id}/permissions`
(`UsersController.cs:96`). It is baked into JWT claims at token-mint time only —
`AuthService.cs:132` (refresh) and `:326` (login) call
`_jwt.GenerateAccessToken(..., user.Permissions)`; `JwtService.cs:47-52` serializes only the
`true` entries into a comma-joined `permissions` claim, which `LegalEntityAuthorization.cs`
already reads the same way for `legal_entities.manage` — i.e. the "bake into JWT at mint time"
mechanism the user was told about already exists and is directly reusable.

Decision:
1. **New table `user_permission_grants`**, additive only — `User.Permissions` and
   `UpdatePermissionsAsync`/the PUT endpoint are untouched. Columns: `Id`, `TenantId`,
   `UserId` (recipient), `PermissionKey` (validated against the same page-slug set as
   `ValidPages`), `ExpiresAt timestamptz NOT NULL` (always temporary — permanent overrides
   keep living exclusively in `User.Permissions`, so this table never needs a `Granted bool` or
   a null-`ExpiresAt` "permanent" case), `GrantedByUserId`, `GrantedAt`, `RevokedAt?` (early
   revoke), `RevokedByUserId?`, `NotifiedExpiringAt?`, `NotifiedExpiredAt?` (worker dedupe
   markers). Standard `tenant_isolation` + `provider_bypass` RLS (pattern used by every table
   since ADR-016/017/018, e.g. `AddLegalEntities` migration). Index on `(TenantId, UserId)` and
   a partial index on `ExpiresAt WHERE "RevokedAt" IS NULL` for the worker scan. Rejected:
   folding permanent grants into the same table "for one audit trail" (per the brief) — two
   independent mechanisms with a narrow, explicit merge step is less regression risk to the
   existing, working permanent-override path than widening it.
2. **Merge happens once, at JWT-mint time**, not per request. `AuthService.cs` gets a new
   private `BuildEffectivePermissionsAsync(User, ct)`: start from `user.Permissions` (or empty),
   then for every grant with `ExpiresAt > utcNow AND RevokedAt IS NULL`, force
   `effective[PermissionKey] = true` — a temporary grant always wins over even an explicit
   permanent `false`, since it is the more specific and more recent authorization. Call this at
   both existing call sites (`:132`, `:326`) in place of `user.Permissions`, and also feed the
   same result into `ToDto`/`AuthUserDto` (`:389`) so the client's own `effectivePageAccess()`
   sidebar logic doesn't disagree with the JWT the server issued it.
3. **API — extend `UsersController`/`UserService`, no new controller.**
   `POST /api/users/{id}/permission-grants` (`permissionKey`, `expiresAt`, future-only),
   `GET /api/users/{id}/permission-grants` (active + recent, for the editor), `DELETE
   /api/users/{id}/permission-grants/{grantId}` (early revoke). Server-side authorization reuses
   the exact `RoleRank` check already in `UserService.UpdatePermissionsAsync` (editor rank >
   target rank, no self-grant, target must be same tenant) — same rule, same table, just called
   from the new methods too.
4. **Worker job `worker/src/jobs/permission-grant-expiry.job.ts`**, cron every 15 min (matches
   the JWT refresh cadence already accepted as the propagation delay). Two scans: expiring within
   24h (`NotifiedExpiringAt IS NULL`) and already expired (`NotifiedExpiredAt IS NULL`), both
   `RevokedAt IS NULL`. Each match inserts one outbox row into `notification_queue`
   (`Channel="system"`, `Status="pending"`, `EventType = "access.temporary_expiring_soon"` /
   `"access.temporary_expired"`) — same shape as `ReceiptService.EnqueueReceivedNotificationAsync`
   — then stamps the corresponding `Notified*At`. New event types added to `ValidEventTypes` in
   `NotificationService.cs:96-109`.
5. **`notification-dispatch.job.ts` needs one new capability**: it currently only does
   role-matrix fan-out (`DISPATCH_EVENT_ROLES`) and doesn't even `SELECT "UserId"` from the
   intent row. This notification is for one specific person (whose access is expiring), not a
   role broadcast — the outbox row must set `UserId = grant.UserId`, and the job needs a new
   branch: when `row.user_id` is present, skip the role matrix and deliver straight to that user
   (their own `notification_settings` for the event type still apply), then mark dispatched.
6. **Frontend**: `UserPermissionsEditor.tsx` gets a second, separately-applied section —
   temporary grants are NOT part of the existing tri-state Save-all-pages flow (different
   backing store, different lifecycle, instant-apply is more honest than batching two mechanisms
   behind one button). Add "Тимчасово до…" alongside the existing grant action, plus a list of
   active grants with a revoke button. New hooks alongside `useUsers.ts`; new
   `TemporaryGrantDto` in `types.ts`. New labels for the two event types in
   `frontend/features/notifications/types.ts` (`EVENT_TYPE_LABELS`, `EVENT_TYPE_SOURCE`,
   `NotificationEventType` union).

Consequences:
+ Zero regression risk to the existing permanent-override path — it is untouched
+ Reuses three already-accepted mechanisms end to end: JWT-bake merge point, ADR-018 outbox, RoleRank check — no new authorization model
+ 15-min worst-case propagation is already the accepted norm for `legal_entities.manage`
- `notification-dispatch.job.ts` needs a genuinely new (if small) code path for single-user targeted delivery, not just a new matrix entry
- Two independent per-user permission mechanisms (dict + table) to reason about instead of one — mitigated by the merge being a single, well-documented function

## ADR-018: Notification categories expansion + filter drawer — Postgres outbox instead of C# BullMQ producer
Date: 2026-07-12
Status: accepted

Context: `notifications` page only surfaces `weekly_report` in practice (expiry/IoT alerts exist
but `iot.temp_alert`/`iot.offline` have no frontend label — display bug). User wants 4 new
categories (надходження, поповнення/AI order, повідомлення постачальника, підписання документів)
with full triggers, plus a collapsible filter drawer (search/employee/category/date/store).
Today's delivery pipeline: `worker/src/jobs/notification.job.ts` (BullMQ "notifications" queue)
resolves role-based recipients + `notification_settings`, delivers via `deliver()`, and is the
only writer of real `NotificationQueue` history rows (`logNotifications`, one row per
user×channel, `Status` = sent/skipped/failed). `expiry-check.job.ts`/`mqtt-listener.ts` are
BullMQ producers, both in Node. `ai-order.job.ts` bypasses this pipeline entirely — it calls
`sendTelegramMessage` directly, no settings check, no history row. Backend (ASP.NET Core) has
**no** existing Redis/BullMQ producer (`grep` for `StackExchange.Redis`/`bullmq` under
`/backend` — zero hits) — the three new backend-originated triggers (receipt received, supplier
chat message, agreement signed) have no way to reach the worker's delivery logic today.

Decision:
1. **Backend-originated events use a Postgres outbox, not a new C# BullMQ producer.** Adding a
   BullMQ-compatible job producer in .NET (matching BullMQ's Lua-script job format) is new
   cross-language infra for 3 call sites. Instead, the triggering C# service inserts one
   broadcast-intent row directly into `NotificationQueue` (`UserId = null`, `Channel = "system"`,
   `Status = "pending"`) via `INotificationRepository` — reuses the existing table, no new
   dependency. A new worker cron `notification-dispatch.job.ts` (poll every 1 min, same shape as
   `fiscalization-retry.job.ts`) selects `Status = 'pending' AND Channel = 'system'` rows,
   resolves recipients by role (same matrix pattern as `EXPIRY_EVENT_ROLES`) +
   `notification_settings`, delivers, writes real per-user×channel rows via the existing
   `logNotifications`, then marks the intent row `Status = 'dispatched'` (terminal, excluded from
   `GetHistoryAsync` so it never appears as a phantom "system" notification in the feed).
2. **`ai-order.job.ts` is rewired to the same in-process pattern as `handleIotAlert`** (query
   users by role → check `notification_settings` → `deliver()` → `logNotifications()`), dropping
   its direct `sendTelegramMessage` loop — it already runs in the Node worker with DB access, so
   no outbox hop is needed there, only the missing settings/history integration.
3. **`NotificationQueue` gains `StoreId Guid?` and `Title string?`.** `StoreId` backs the "by
   store" filter (repeats the `EventType.namespace.action` DB-only-hardcoded-set pattern already
   used for events/channels — no new enum table). `Title` is a short human-readable line
   (e.g. "Надійшла поставка №1234 — Хрещатик") populated by whichever service enqueues the row,
   so keyword search runs `ILIKE`/trigram against `Title` instead of parsing the `Payload` JSONB
   on every query — cheaper and matches the existing "Payload is opaque, UI parses it lazily"
   convention in `NotificationDetailDrawer.tsx`. Add `pg_trgm` GIN index on `Title` for the
   keyword filter, plus btree indexes on `(TenantId, CreatedAt)`, `(TenantId, EventType)`,
   `(TenantId, StoreId)`, `(TenantId, UserId)` for the other filters.
4. **Filter drawer is a hand-rolled overlay, not a new shadcn `Sheet`.** `components/ui/sheet.tsx`
   does not exist in this repo and `NotificationDetailDrawer.tsx` already implements a fixed-panel
   + backdrop drawer by hand — the new `NotificationFilterDrawer` follows the same pattern for
   visual/behavioral consistency rather than introducing a new shadcn primitive for one page.
5. **Filter state lives in component state + React Query key, not the URL.** No page in this repo
   currently syncs filters to `useSearchParams` (checked — zero matches under `frontend/features`
   outside auth). Introducing URL-synced filters here would be a new, unprecedented pattern for a
   single page; skip it. React Query key includes the filter object so results stay cached per
   filter combination.

Consequences: `notification.job.ts` and the new `notification-dispatch.job.ts` share the
role-matrix + settings-check + `logNotifications` pattern — worth extracting to a shared helper
in a follow-up if a 4th producer appears. `Channel = "system"` is an internal sentinel, not added
to `ValidChannels` in `NotificationService.cs` (backend inserts the outbox row directly via the
repository, bypassing the public validate path, same way the worker's `logNotifications` already
bypasses `NotificationService` entirely). `GetHistoryAsync` must filter out `Channel = 'system'`
rows so undispatched intents never leak into the UI feed.

## ADR-017: Provider nav split (Клієнти/Постачальники) + per-item категорії з JSONB attributes
Date: 2026-07-03
Status: accepted

Context: v4.1 (ADR-016) додав supplier-as-tenant. Два подальші UX/дані запити:
(A) провайдер-панель показує всіх тенантів одним списком (`ProviderService.GetTenantsAsync`,
`frontend/features/provider/`, сторінка `/provider` з табами `tenants`/`logs`) — незручно шукати
серед клієнтів і постачальників разом; (B) `SupplierItem` (marketplace listing постачальника,
не Item catalog) не має категорії — постачальник, який працює в кількох галузях (продукти,
автозапчастини, медикаменти, будматеріали), не може задати категорійно-специфічні поля
(OEM-номер, дозування/рецептурний статус, партія/термін придатності, клас сертифікації) для
кожного товару окремо.

Decision:
1. **Feature A — один список, client-side split, без нового роуту.** Сторінка `/provider`
   лишається одна; `activeTab` розширюється з `"tenants" | "logs"` на
   `"clients" | "suppliers" | "logs"`. Дані й API-виклик (`useTenants()`) без змін — фільтрація
   `business_type === "supplier"` виконується на клієнті над уже завантаженим списком (список
   тенантів невеликий, provider-only, пагінації немає). Причина проти нового бекенд-ендпоінта
   чи нового Next-роуту: нуль нових абстракцій, нуль ризику розсинхронізації лічильників
   (health-картки лишаються на весь список), TenantDetailPanel/CreateTenantWizard реюзаються
   без змін. Лічильник міняється лише в лейблі табу (`Клієнти (N)` / `Постачальники (M)`).
2. **Feature A — фільтрація по business_type, не по slug.** `platform-marketplace` (BUG-014,
   системний, IsActive=false) вже виключається на рівні `TenantRepository.GetAllAsync` — таб
   «Постачальники» бачить тільки реальні supplier-tenant-и, створені онбордингом (ADR-016 п.3/TASK-289).
3. **Feature B — категорія товару: `category` string (nullable) + `attributes JSONB (nullable)` на `SupplierItem`.**
   Обрано (b) єдину JSONB-колонку над (a) фіксованими nullable-колонками per category:
   набір категорій зростатиме (спека вже передбачає 4 старт-категорії, будматеріали/медикаменти
   реально розширяться підкатегоріями), і кожна нова категорія з підходом (a) означала б нову
   міграцію + розпухання entity. Прецедент у кодовій базі: `Item.Barcodes` — `List<string>` →
   `jsonb`, EF Core вже сконфігурований на dynamic JSON (Npgsql `EnableDynamicJson`, див.
   пам'ять проєкту); тут форма JSON я — довільний `Dictionary<string, object?>` (не List), тому
   на рівні EF — `.HasColumnType("jsonb")` + serialize/deserialize через `System.Text.Json`
   (той самий патерн, без потреби у нових Npgsql-налаштуваннях). Значення в `attributes`
   ніколи не беруть участі в SQL WHERE/JOIN (лише читання/показ у формі) — тому втрата
   SQL-запитів по конкретних полях прийнятна: категорійний пошук/фільтр (якщо колись знадобиться)
   іде через `category`, не через вміст attributes.
4. **Довідник категорій і полів живе в backend (C# const/enum + shared DTO), не тільки в
   фронтенд-мапі.** `SupplierItemCategories` (`ShelfGuard.Domain.Constants`) — фіксований
   список ключів категорій (`food`, `auto_parts`, `medical`, `construction`) + для кожної:
   список полів з `{key, label, type, required}` — **backend є джерелом істини**, бо валідація
   обов'язкових полів (медикамент без терміну придатності — invalid) має відбуватись на
   сервері, а не тільки в React-формі. Ендпоінт `GET /api/marketplace/item-categories`
   (публічний, кешується на фронті) віддає цей довідник як DTO — фронтенд не хардкодить форму,
   а рендерить її з відповіді. Це трохи важче за "фронтенд-only мапу", але усуває клас багів
   (фронт і бек розходяться в тому, що обов'язково) і дає єдине місце для розширення категорій.
5. **Зворотна сумісність.** `category` і `attributes` — нові nullable-колонки, DEFAULT NULL.
   Existing `SupplierItem` (provider-created legacy, TASK-275, і вже створені кабінетом TASK-286)
   лишаються з `category = null` — трактуються фронтом як «без категорії» (стара форма
   customName/price/minQty/unit, без динамічних полів). Валідація обов'язкових
   категорійних полів застосовується **тільки** коли `category` заданий (create/update DTO);
   `category = null` — валідний стан назавжди, не тимчасова міграційна яма.
6. **DTO shape:** `AdminAddSupplierItemDto`/`AdminUpdateSupplierItemDto`/`SupplierItemDto`
   (Cabinet-варіанти теж) отримують `string? Category` + `Dictionary<string, object?>? Attributes`.
   Немає окремих DTO per категорія — один generic shape, валідація обов'язкових полів
   виконується сервісним методом `SupplierItemCategories.Validate(category, attributes)`,
   що повертає список помилок (400 з переліком відсутніх полів).

Consequences:
+ Нова категорія (наприклад «Текстиль») — тільки зміна в `SupplierItemCategories` (C#) +
  фронтенд рендерить нову форму автоматично через API-довідник, без міграції
+ Один generic DTO/контролер-шлях для всіх категорій — мінімум нового коду в MarketplaceService/SupplierCabinetService
+ Existing товари (без категорії) не ламаються, стара форма продовжує працювати
- Не можна ефективно фільтрувати/сортувати marketplace за конкретним атрибутом (напр. "OEM-номер X")
  без повного сканування JSONB — прийнятно, бо публічний пошук сьогодні йде по `ItemName`/`Region`, не по атрибутах
- Валідація обов'язкових полів існує тільки в коді (C# + дзеркальна перевірка у формі), не в БД CHECK constraint —
  узгоджено з існуючим правилом "Validate at boundaries only"
- Provider-панель `/provider` тепер має 3 таби замість 2 — трохи вищий когнітивний навантаження, без нового роутингу

## ADR-016: Supplier self-service — supplier як окремий tenant (business_type = "supplier")
Date: 2026-07-02
Status: accepted

Context: Потрібна роль «Постачальник», який сам наповнює маркетплейс (профіль, товари) і бачить свої відгуки/рейтинг. Сьогодні marketplace-постачальників створює провайдер вручну (TASK-275, `TenantId = Guid.Empty`). Entities `Supplier/SupplierProfile/SupplierItem/SupplierMetrics/SupplierReview` вже існують з RLS `tenant_isolation` + `provider_bypass`; публічний листинг читається через provider-level DB context (`app.role = 'provider'`) з фільтром `is_public = true`.

Decision:
1. **Supplier = окремий tenant** з `business_type = "supplier"` і default-модулем `["marketplace_supplier"]`. НЕ нова роль усередині клієнтського tenant. Rationale: існуючий RLS `tenant_isolation` автоматично дає постачальнику видимість ТІЛЬКИ своїх рядків (`Supplier.TenantId` = його власний tenant), а публічний cross-tenant read маркетплейсу вже працює через provider-context + `is_public` — нових RLS-механізмів не треба.
2. **Нова app-роль `supplier_admin`** (tenant-scoped, у `AppRoles` + `roles.ts`). Юзер постачальника — звичайний User з `TenantId` = supplier-tenant, `Role = supplier_admin`. Auth/JWT без змін.
3. **Онбординг — провайдер запрошує** через існуючий Admin tenant onboarding (`business_type = "supplier"`). При створенні такого tenant автоматично створюється пара `Supplier` + `SupplierProfile` (`IsPublic = false` до заповнення). Self-registration — фаза 2.
4. **Зв'язок User ↔ Supplier — через TenantId.** Нова колонка `supplier_profiles.IsOwnerManaged bool` + partial unique index на `TenantId WHERE IsOwnerManaged` — детермінований lookup «мій профіль» (suppliers-таблиця double-duty: локальний довідник клієнтів і marketplace-записи, тому unique по TenantId неможливий).
5. **Supplier cabinet** — новий `SupplierCabinetController` (`/api/supplier-cabinet/*`), `[RequireModule("marketplace_supplier")]` + роль supplier_admin: GET/PUT профіль (+ publish toggle), CRUD товарів, read-only відгуки/метрики. Реюз логіки `MarketplaceService` (Admin*-методи параметризуються supplierId, resolved by tenant).
6. **Відгуки:** лишають тільки клієнтські tenant-и (existing `POST /api/marketplace/suppliers/{id}/reviews`; unique (supplier_id, tenant_id) вже є). Guard від накруток: reviewer tenant ≠ supplier.TenantId і `business_type != "supplier"`. Rating у `SupplierMetrics.Rating` перераховується синхронно в `CreateReviewAsync` (AVG по відгуках). Додається публічний `GET /suppliers/{id}/reviews`.
7. Існуючі provider-created suppliers (`TenantId = Guid.Empty`) лишаються як є; кабінет для них недоступний, поки провайдер не привʼяже supplier-tenant.
   > **Amendment (BUG-012, 2026-07-03):** `Guid.Empty` ніколи не працював — FK `suppliers→tenants` існував завжди, тож admin-create завжди падав 500 і рядків з `TenantId = Guid.Empty` у prod немає. Provider-created suppliers тепер привʼязуються до системного tenant «Platform Marketplace» (slug `platform-marketplace`, `business_type = supplier`, inactive, без users), який створюється ліниво в `MarketplaceRepository.GetOrCreatePlatformTenantIdAsync`. Кабінет його не бачить: профілі мають `IsOwnerManaged = false`, а лукап кабінету фільтрує `IsOwnerManaged = true`.
   > **Amendment (TASK-305, 2026-07-05, план `calm-singing-marble.md`):** компроміс BUG-012 визнано остаточно проблемним — два шляхи створення постачальника (Admin/Провайдер vs Маркетплейс/Постачальники) дублювали функціонал і залишали "напівживі" записи. Рішення: **лишити тільки шлях через `CreateTenantWizard`** (Admin/Провайдер/Постачальники), а legacy-шлях (`MarketplaceAdminController.CreateSupplier`) видаляє backend-developer окремою задачею. Дані-міграція `MigrateOrphanSuppliersToTenants` (database-engineer) переносить кожного постачальника з `platform-marketplace` на власний реальний активний tenant (`IsOwnerManaged = true`), після чого провайдер додає керівника через уже існуючий `AddTenantUserModal`. Після підтвердження, що жоден рядок більше не вказує на `platform-marketplace`, сам системний tenant і `GetOrCreatePlatformTenantIdAsync`/`PlatformTenantSlug`/`PlatformTenantName` видаляються.
   > Заодно додана ієрархія кастомних ролей команди постачальника (`supplier_roles`, tenant-scoped — на відміну від глобального `provider_roles`, кожен supplier tenant керує своїми ролями незалежно) і нова окрема сутність дошки завдань `supplier_tasks` (не привʼязана до існуючих заявок/замовлень). Обидві таблиці — стандартний RLS `tenant_isolation` + `provider_bypass`. Деталі схеми: `database-schema.md` розділ "v4.1 — Supplier tenant migration + roles/tasks".
   > **Amendment (TASK-306, 2026-07-05, backend-developer):** `MarketplaceAdminController.CreateSupplier`, `MarketplaceService.AdminCreateSupplierAsync`, `AdminCreateSupplierDto` — видалені. `GetOrCreatePlatformTenantIdAsync`/`PlatformTenantSlug`/`PlatformTenantName` (`MarketplaceRepository.cs`) НЕ видалені — `TenantRepository.GetAllAsync` досі фільтрує провайдерський список тенантів за цим slug'ом, а `MarketplaceRepositoryPlatformTenantTests` досі покриває цю поведінку; видалення відкладено до підтвердження (наступна ітерація/QA), що жоден рядок `suppliers`/`supplier_profiles` більше не вказує на `platform-marketplace` в жодному оточенні. Додано `ISupplierRolesService`/`SupplierRolesService` + `ISupplierTaskService`/`SupplierTaskService` (Application/Marketplace), CRUD endpoints на `SupplierCabinetController` (`/api/supplier-cabinet/roles`, `/api/supplier-cabinet/tasks`). `SupplierCabinetService.InviteStaffAsync` тепер приймає опційний `SupplierRoleId` — резолвиться в `Dictionary<string,bool>` через `IUserRepository` (той самий підхід, що й `ProviderTeamService`), відсутність ролі = повний доступ (без змін).

Consequences:
+ Нуль нових RLS-механізмів; ізоляція та публічний read — існуючими політиками
+ Максимальний реюз: entities, MarketplaceService, marketplace UI-компоненти
+ Онбординг = існуючий tenant onboarding + один hook
- supplier-tenant «носить» повний tenant-каркас (stores, modules), хоча використовує лише кабінет
- Подвійна семантика suppliers-таблиці лишається (локальний довідник vs marketplace) — розділення відкладено

## ADR-015: Module-based tenant activation pattern
Date: 2026-06-15
Status: accepted

Context: v4-spec вимагає, щоб кожен тенант міг активувати тільки потрібні йому модулі (Inventory, Procurement, POS, AutoService, Production, Marketplace). Поле `modules` (JSONB) вже існує на таблиці tenants (додано в TASK-074). Потрібно визначити, як модулі активуються і як API захищає модульні ендпоінти.

Decision:
1. Ключі в `tenant.modules` JSONB відповідають ідентифікаторам модулів: `"inventory"`, `"procurement"`, `"pos"`, `"auto_service"`, `"production"`, `"marketplace"`. Значення `true` = активовано.
2. Default-набір модулів при онбордингу визначається полем `business_type` (ADR-014): retail → `{inventory, procurement, pos}`, auto_service → `{auto_service, procurement}`, restaurant → `{inventory, pos, production}` і т.д.
3. На рівні ASP.NET Core додається `[RequireModule("module_key")]` attribute + відповідний `IAsyncActionFilter`, який читає `ITenantContext.Modules` і повертає `403 { error: "Module not activated" }` якщо модуль вимкнений.
4. API для управління модулями: `GET /api/admin/tenants/{id}/modules`, `PATCH /api/admin/tenants/{id}/modules` (ProviderOnly), `GET /api/settings/modules` (enterprise_admin — власний тенант). Активація/деактивація модуля не видаляє дані — тільки приховує доступ.
5. Frontend: sidebar-групи показуються/ховаються за комбінацією RBAC (роль) + модуль (активований). Хук `useModules()` читає з `/api/settings/modules`.

Consequences:
+ Один механізм для всіх модулів — легко додати новий
+ Дані ніколи не видаляються при деактивації (безпечно)
+ Provider panel повністю контролює набір модулів тенанта
- На кожен запит потрібен доступ до tenant.modules (мінімізується через ITenantContext кеш у request scope)
- UI sidebar ускладнюється (подвійна умова: роль + модуль)

## ADR-014: Platform transformation — Universal Location/Item model
Date: 2026-06-15
Status: accepted

Context: v4-spec вимагає перетворити платформу з retail-специфічної (Store, Product) на universal Business Operations Platform (Location, Item). Поточна схема: `stores`, `catalog_products`, `store_manager` role, `store_inventory`. Трансформація зачіпає 15+ таблиць, RLS policies, усі шари (DB, Domain, Application, API, Frontend, Mobile).

Decision:
1. **DB rename** (через EF Core migration): `catalog_products` → `items` (+ `item_type` column), `stores` → `locations` (+ `location_type` column), `store_zones` → `location_zones`. Роль `store_manager` → `location_manager` в AppRoles enum (UI label змінюється, значення в DB теж — UPDATE users SET role='location_manager').
2. **Поетапна міграція** (не big bang): спочатку DB + Backend, потім Frontend, потім Mobile. На кожному етапі працює production.
3. **API routes** змінюються: `/api/stores` → `/api/locations`, `/api/catalog` → `/api/items`. Для зворотньої сумісності мобільного APK — тимчасові 301-редіректи зі старих маршрутів (протягом 1 спринту, потім видаляються).
4. **Entity rename у коді**: `Store` → `Location`, `StoreZone` → `LocationZone`, `CatalogProduct` → `Item`. POC `Products`/`Product` entity видаляється разом з legacy `Products` table (давно заплановано ADR-006).
5. **business_type** додається до `tenants` table як PostgreSQL enum: `retail` (default), `auto_service`, `warehouse`, `restaurant`, `production`, `distribution`.
6. **item_type** enum: `product`, `service`, `spare_part`, `consumable`, `raw_material`, `kit`. Default: `product`.
7. **location_type** enum: `retail_store`, `warehouse`, `auto_service`, `office`, `production`, `restaurant`. Default: `retail_store`.
8. **FEFO, RLS, batch_number/expiry_date rules незмінні** — трансформація виключно в іменуванні.

Consequences:
+ Платформа відкривається для нових індустрій без зміни архітектурних патернів
+ POC Products table нарешті видаляється (ADR-006 виконується)
+ item_type дозволяє Procurement і AutoService працювати з тим самим каталогом
- Великий обсяг rename-роботи (15+ файлів backend, 20+ frontend, mobile)
- 301-редіректи потрібно прибрати через 1 спринт щоб не залишати dead code
- Тести треба оновити (entity names)

## ADR-013: Per-tenant fiscal provider config in DB, env as fallback, per-tenant IFiscalService resolution
Date: 2026-06-12
Status: accepted

Context: ADR-012 point 5 configures the Checkbox provider via deployment-level env vars (`PRRO__*`), so one process = one fiscal provider for all tenants. ShelfGuard is multi-tenant: each tenant has its own cash register (license key, cashier creds, test vs prod environment). The Claude API key already solved the same problem (TASK-058/060): per-tenant `integration_configs` row (service='claude', JSONB config, RLS) managed via «Налаштування → Інтеграції», with env (`Claude:ApiKey`) as deployment-level fallback — see `ClaudeOrderAdvisor.ResolveAsync`.

Decision:
1. Fiscal provider config moves to the same mechanism: `integration_configs` row with `service='prro'`, JSONB shape `{provider, base_url, license_key, cashier_login, cashier_password, cashier_pin_code}`. `provider` is an extensible enum: `"checkbox"` now, `"disabled"` → NoopFiscalService; future providers (direct-ДПС etc.) are new enum values, no schema change.
2. Resolution order (same as Claude key): tenant's `integration_configs` (service='prro', IsEnabled, RLS-scoped) → fallback to `PRRO__*` env vars (current ADR-012 behavior, kept for single-tenant deployments and CI) → Noop if neither configured.
3. `IFiscalService` resolution becomes per-tenant: a scoped `IFiscalServiceFactory` (Infrastructure/Integrations/Prro) reads the tenant's settings through the RLS-scoped AppDbContext and returns the matching implementation. The startup-time DI switch on `PRRO:PROVIDER` (DependencyInjection.cs) is replaced by the factory; consumers (TASK-068 POS endpoints, TASK-069 retry job) resolve through the factory, never the concrete client. `CheckboxTokenStore` must key cached bearer tokens by tenant+license key, not globally.
4. Secrets are write-only in the API: GET returns masked values (e.g. `••••` + last 4); PUT treats a masked/empty secret field as "keep existing value". This rule applies to the generic integrations endpoint too (known gap: today GET /api/integrations/{service} returns raw credentials).

Consequences:
+ Each tenant connects its own Checkbox register from the web UI — no redeploy, no shared register
+ Same UX and code path as the Claude key — one pattern to learn and audit
+ Env fallback keeps existing prod deployment and live e2e tests working unchanged
- Factory adds a DB read on the fiscal path (mitigated by per-request scoping; config row is tiny)
- Token cache becomes per-tenant — more states to reason about on credential rotation

Extends: ADR-012 (point 5 becomes the fallback layer, not the primary source).

## ADR-012: Checkbox as fiscal provider behind IFiscalService
Date: 2026-06-12
Status: accepted

Context: ADR-011 planned direct integration with the ДПС fiscal server (fs.tax.gov.ua) with our own КЕП signing. КЕП + 1-ПРРО registration is still blocked on the user, which blocks any real fiscalization. The user registered a test cash register with Checkbox (checkbox.ua) — a Ukrainian SaaS ПРРО provider (фіскальний номер TEST582378, test mode). Checkbox handles КЕП signing server-side, fiscalization, offline numbering, and ДПС submission; we talk to its REST API. Auth model: `X-License-Key` header identifies the cash register; a cashier signs in (login/password or PIN) to obtain a bearer token; receipts and shifts go through that token.

Decision:
1. Checkbox becomes the fiscal provider. ADR-011's isolation rule stands: everything Checkbox-specific (HTTP client, DTOs, auth/token handling) lives in `ShelfGuard.Infrastructure/Integrations/Prro`; the Application layer sees only `IFiscalService` and never Checkbox shapes.
2. `IKepSigner` is NOT needed for the Checkbox path — Checkbox signs documents server-side with its own КЕП. The interface stays in the codebase only if/when a direct-ДПС provider is added.
3. The offline-first rule from ADR-011 stays unchanged: sale committed locally first (pos_transaction + items + FEFO write-down in one DB transaction), fiscalization is async with a retry job; `Status = 'pending_fiscalization'` until Checkbox returns a fiscal number.
4. Provider is pluggable behind `IFiscalService`: a future direct-ДПС client (with a real KEP signer) can be added via config switch without any flow changes in Application/API/worker.
5. Config via env (secrets only in `.env`, never committed): `PRRO__PROVIDER=checkbox`, `PRRO__BASEURL` (test: `https://dev-api.checkbox.in.ua/api/v1`, prod: `https://api.checkbox.ua/api/v1`), `PRRO__LICENSEKEY`, `PRRO__CASHIER__LOGIN` / `PRRO__CASHIER__PINCODE`. License key is stored in `.claude/private/access.md`.

Consequences:
+ No ПРРО certification / КЕП burden on our side — Checkbox is already certified with ДПС
+ Demo-able today: test cash register works without waiting for КЕП / 1-ПРРО registration
+ Checkbox handles offline numbering per ПРРО rules — we don't reimplement it
+ Flow (offline-first, async fiscalization, retry job) identical regardless of provider
- Vendor dependency + per-receipt cost on the production plan
- Cashier credentials (login/PIN) still pending from the user — token flow can't be live-tested end-to-end yet

Supersedes: ADR-011 points 2 (IKepSigner/StubKepSigner) for the Checkbox path; points 1, 3, 4 remain in force.

## ADR-011: PRRO fiscal integration — isolated client, pluggable signer, offline-first
Date: 2026-06-12
Status: accepted

Context: v3 Phase 4 needs integration with the ДПС fiscal server (ПРРО). Connectivity confirmed: POST fs.tax.gov.ua:8609/fs/cmd `{"Command":"ServerState"}` → 200 unsigned. All fiscal documents (checks, Z-reports, shift open/close) must be signed with КЕП, which is not yet available (user registering 1-ПРРО). Legal flow also requires offline mode (ПРРО must keep selling when ДПС is unreachable, with offline fiscal numbers).

Decision:
1. Fiscal client lives in `ShelfGuard.Infrastructure/Integrations/Prro` only (same isolation rule as Claude API). Application layer talks to `IFiscalService`; controllers never see ДПС shapes.
2. Signing behind `IKepSigner` (`SignAsync(byte[] document)`). Until КЕП arrives, `StubKepSigner` runs the pipeline in test mode: documents get local numbers, `FiscalNumber = null`, `Status = 'pending_fiscalization'`.
3. Offline-first: every sale is committed locally first (pos_transactions + stock_events + FEFO write-down in one DB transaction); fiscalization is a follow-up step that updates FiscalNumber. A BullMQ retry job re-submits unfiscalized documents.
4. POS UI = new screens in the existing Expo app (tablet layout), not a separate app. Same auth, same API client.

Consequences:
+ Sales never blocked by ДПС availability or missing КЕП — demo-able today
+ КЕП drop-in later: implement real signer + config, no flow changes
+ Single mobile codebase
- Fiscal numbers arrive asynchronously — receipt print/SMS must handle "fiscalization pending"
- Test mode receipts are legally non-fiscal — clearly marked in UI until КЕП configured

## ADR-010: MQTT ingestion lives in the Node worker
Date: 2026-06-12
Status: accepted

Context: v3 Phase 1 needs an MQTT consumer for weight/temperature sensors (v3-spec §1, §4). Options: (a) MQTT client hosted inside ASP.NET Core API; (b) a dedicated subscriber in the existing Node worker service.

Decision: The worker subscribes to Mosquitto (`mqtt` npm package, topic `shelfguard/{tenant_id}/{store_id}/#`) and owns the full ingestion path: validate device → write temperature_readings / weight_readings → derive stock_events → enqueue notifications via the existing BullMQ pipeline. The ASP.NET API never talks to MQTT; it only serves CRUD for iot_devices and read endpoints for readings. Mosquitto runs as a docker-compose service.

Consequences:
+ Reuses the worker's existing always-on process, pg pool, notification queue, and Telegram path (same pattern as telegram-listener)
+ API stays request/response only — no hosted background services
+ Ingestion can be scaled/restarted independently of the API
- Sensor business rules (confidence, alert thresholds) live in TypeScript, not C# — acceptable: they are stream-processing rules, not request-path domain logic
- Worker now requires MQTT_URL env; local dev needs Mosquitto up for IoT features

## ADR-009: IAnalyticsRepository in Application layer
Date: 2026-06-04
Status: accepted

Context: Analytics queries return DTO aggregates (ExpirySummaryDto, LossesDto etc.), not domain entities. The IRepository pattern in Domain requires returning entities; placing IAnalyticsRepository in Domain would create a Domain → Application circular reference.

Decision: IAnalyticsRepository is defined in ShelfGuard.Application.Features.Analytics (same namespace as IAnalyticsService). Infrastructure implements it. Domain is unaware of analytics contracts.

Consequences:
+ Avoids circular dependency
+ Analytics stays as a read-model concern, cleanly separated
- Minor inconsistency: most IRepository interfaces live in Domain.Interfaces; this one does not
- Future devs must know the exception exists (documented here)

## ADR-001: BullMQ with ASP.NET Core
Date: 2026-06-03
Status: accepted

Context: v1-spec requires BullMQ for background jobs. BullMQ is Node.js-only. Main API is ASP.NET Core.

Decision: Separate /worker Node.js service. API writes to Redis via StackExchange.Redis. Worker reads via BullMQ.

Consequences:
+ BullMQ used as specified; .NET remains primary business logic layer; worker scales independently
- Extra service to maintain; Redis required in infrastructure

---

## ADR-002: Modular Monolith over Turborepo
Date: 2026-06-03
Status: accepted

Context: v1-spec mentioned Turborepo monorepo.

Decision: Single ASP.NET Core solution with feature-based modules. No Turborepo. Frontend and mobile are separate npm projects.

Consequences: + Simpler deployment. - Less isolation between modules (mitigated by strict layer rules).

---

## ADR-003: Expo SDK 56 for Mobile
Date: 2026-06-03
Status: accepted

Decision: Expo SDK 56 with Expo Router, NativeWind v4 (spec said SDK 51+, updated to latest stable).

---

## ADR-004: Port Mapping (avoid local conflicts)
Date: 2026-06-03
Status: accepted

Decision:
- Docker PostgreSQL → port 5435 (avoids conflict with local 5432)
- Docker Redis → port 6380 (avoids conflict with local 6379)
- Connection string: `Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password`

---

## ADR-005: Worker scaffold in TASK-000
Date: 2026-06-03
Status: accepted

Decision: /worker scaffold created upfront (package.json, tsconfig, Dockerfile, job stubs). Real logic in TASK-008 / TASK-017.

---

## ADR-006: Separate catalog_products table (not replacing Products)
Date: 2026-06-04
Status: accepted

Context: TASK-002 (full schema) needed to add the v1 tenant-aware `products` table from the spec. The POC `Products` table (EF Core default name = "Products", no tenant_id) already exists and powers the catalog API.

Decision: Create new `catalog_products` table (EF entity `CatalogProduct`) for the v1 tenant-aware product catalog. Keep legacy `Products` table intact until TASK-003b migrates the catalog API.

Consequences:
+ No breaking change to existing catalog API
+ Full schema deployed without disrupting running dev environment
- Two product tables exist temporarily; devs must know which one to use
- `product_stock` references `catalog_products`, not legacy `Products`

Supersedes: nothing — this is additive.

---

## ADR-007: Dashboard data from POC Products (temporary proxy)
Date: 2026-06-04
Status: accepted (temporary)

Context: Dashboard stat cards (Safe/Warning/Critical/Expired) require real `product_stock` batch data with expiry dates. That endpoint does not exist yet.

Decision: Derive dashboard stats from POC `/api/products` using `stockQuantity vs reorderLevel` as proxy. Clearly documented as placeholder. "Expired" = stockQuantity is 0 (incorrect semantically, acceptable for demo).

Superseded by: TASK-011 + TASK-016 (real analytics endpoint from `product_stock`).

---

## ADR-008: RLS column names must be double-quoted
Date: 2026-06-04
Status: accepted

Context: EF Core creates columns with PascalCase names (e.g., `"TenantId"`). PostgreSQL folds unquoted identifiers to lowercase. Raw SQL in RLS policies using `tenant_id` (unquoted) throws `column "tenant_id" does not exist`.

Decision: All column references in manually-written RLS SQL must be double-quoted to match EF Core's PascalCase: `"TenantId"`, `"Id"`, `"StoreId"`, etc.

Rule: applies to all `migrationBuilder.Sql()` calls that reference column names.
