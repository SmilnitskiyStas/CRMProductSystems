# Known Issues

**Owner:** qa-tester
**Updated:** 2026-09-01 · reorganised 2026-09-02 (fully-resolved issues → `known-issues-archive.md`)

Resolved / historical issues live in [`known-issues-archive.md`](known-issues-archive.md).
`grep` either file by KI-ID.

## Active Issues

### KI-038: Supplier delivery/response metrics carry structural measurement limitations — per-region sparsity, receipt-finalization bias, answered-sessions-only response median
Severity: low (every number shown is honest as far as it goes; each limitation biases *toward* the
diligent supplier/client, never fabricates data — provided the UI shows "на основі N")
Status: open — accepted by design (plan `eventual-whistling-rabbit.md` risks 2/3/4, TASK-653,
ADR-036). Documented so a future reader does not treat a legitimately-sparse drill-down as a bug.
Description:
- **`SupplierMetricsDto.deliveryByRegion` stays sparse for months.** `MarketplaceOrder.DestinationRegionCode`
  is a point-in-time snapshot (ADR-036 Decision 2), written only at order creation from
  `Location.RegionCode`. Every order predating migration `20260831090731` has it NULL, and
  `Location.RegionCode` itself starts NULL and is set by hand. Those orders still feed the *overall*
  `avgDeliveryDays` (they have `ShippedAt`/`DeliveredAt`) but no per-region row — `deliveryByRegion`
  starts at n=0 and fills only as new orders accrue. Not backfilled (region-from-`Address` is
  unreliable and would feed the real statistics wrong data). Web/mobile must render "на основі N" /
  "ще недостатньо даних по регіонах"; without it the drill-down looks broken.
- **`avgDeliveryDays` = `DeliveredAt − ShippedAt`, and `DeliveredAt` only exists once the client
  finalizes a `MarketplaceOrderReceipt` (ADR-033).** Shipped-but-not-received orders are invisible
  to the metric → the average is biased toward diligent receiving clients. `MarketplaceOrder` has no
  `ConfirmedAt`, so "how fast did the supplier acknowledge the order" is unmeasurable — only chat
  first-reply latency exists.
- **`responseTimeHours` median counts only chat sessions where the supplier eventually replied.**
  A supplier who ignores half their threads looks identical to one who answers every one.
  `responseSampleSize` is the count of *answered* sessions; there is no "response rate" metric.
- `qualityScore` has no data source at all — always null, its tile renders "—".
Related: **KI-037** (the mobile fraction-tile fix, TASK-660) — the per-region breakdown and
`Час відповіді` tile added in the same task use correct scaling from the start.
Resolution: each is its own future initiative (a `Location.RegionCode` data-entry campaign; an
`order.ConfirmedAt` + supplier-ack flow; a response-rate denominator from all
`supplier_chat_sessions`). None blocks the feature.

### KI-039: `DeliveryRegions` → `DeliveryCoverage` backfill match rate is low; unmapped free text lands in `note`
Severity: low
Status: open — expected outcome of the TASK-661 one-shot tool
(`ShelfGuard.Tools.DeliveryCoverageBackfill`), not a defect.
Description: legacy `supplier_profiles.DeliveryRegions` is a free-text jsonb array.
`UkraineRegions.TryMatchFreeText` maps oblast/city names and a few aliases (`Київська`→UA-32,
`Дніпро`→UA-12, `АР Крим`→UA-43, …) to codes, but common entries like «Вся Україна» /
«по домовленості» / «Нова Пошта» don't resolve — they are preserved verbatim in
`DeliveryCoverage.note` as `"Також: …"` so nothing is lost, but they do not participate in the
structured `served`/`notServed` region filter. Affected suppliers may need to re-declare coverage
through the profile editor to appear under a region filter with a real match. A
`DeliveryCoverage IS NULL` profile still matches `GET /api/marketplace/suppliers?regionCode=` via a
`Region ILIKE` fallback, so a supplier does not vanish from search during the transition — but a
profile the backfill flips to note-only coverage loses that fallback (rare: needs an
entirely-unmappable `DeliveryRegions` *and* a `Region` string that would have ILIKE-matched).
Resolution: none required; the tool logs every unmatched value. `SupplierProfile.DeliveryRegions`
stays `[Obsolete]` (kept for audit) and is dropped by a later migration; the two
`#pragma warning disable CS0618` reads in `MarketplaceService`/`SupplierCabinetService` can be
removed once the tool has run in prod.

### KI-040: `backend/openapi.json` not regenerated for the delivery-coverage endpoints (pending chore)
Severity: low (the committed snapshot is a doc artifact, not runtime — nothing breaks; but
`docs/integration/MOBILE_API.md` regeneration reads from it, so it drifts)
Status: open — TASK-648..661 shipped without regenerating it (needs a live API + local dev Postgres
run, per `backend-structure.md` §"OpenAPI Contract").
Description: `GET /api/geo/regions`, `GET /api/marketplace/suppliers/{id}/coverage`, the
`region`→`regionCode` query-param rename on `GET /api/marketplace/suppliers` +
`POST /api/marketplace/search`, and the `deliveryCoverage` / `deliveryByRegion` /
`deliverySampleSize` / `responseSampleSize` / `aggregatesComputedAt` DTO fields are all absent from
the committed `backend/openapi.json`.
Resolution: run the regeneration command in `backend-structure.md` §"OpenAPI Contract"
(`dotnet build` + `dotnet tool run swagger tofile`, dev Postgres up) and commit the result.
Mechanical; no code change.

### KI-041: Existing dev/legacy supplier profiles may carry non-registry category strings; read-only display renders "—"
Severity: low (dev/legacy data only — not a bug in the current code, which validates categories
server-side against `SupplierItemCategories` at tenant creation; does not affect new suppliers)
Status: open — expected outcome, no action required
Description: before the 4-key `SupplierItemCategories` registry was defined (food, auto_parts,
medical, construction), supplier profiles could be created with arbitrary category strings
(e.g. `dairy`, `veterinary`, `sport`). The dev-DB seed still has some legacy entries. The
read-only single-category line in the profile form resolves the stored key to `labelUa` via the
`item-categories` registry; a non-registry key does not resolve, so the field falls back to
`categoryNone` ("—" / "Not specified") rather than showing the raw string. The provider can set a
valid key via `PUT /api/provider/tenants/{id}/supplier-category`. New suppliers (created after
TASK-665) always have a valid registry key or null.
Resolution: none required.

### KI-043: Supplier metric-history trend charts stay empty until ≥2 daily snapshots accrue (~2 days after deploy)
Severity: low (expected behavior during initial rollout, not a bug — metric-history feature depends on accumulated daily data)
Status: open — accepted by design (TASK-670..672, ADR-036 amendment)
Description: the nightly `supplier-metrics-recompute` worker job (TASK-671) writes one row per supplier to `supplier_metrics_snapshots` on each run. The buyer-facing `/marketplace/{id}/metrics` detail page (TASK-672) renders 7 metric sections, each with a Recharts trend chart — the chart shows as empty-state until the history endpoint returns ≥2 points (`supplier_metrics_snapshots` rows, at least 1 day apart). On the day TASK-670..672 ships, zero snapshots exist. After the first nightly job run, exactly 1 row per supplier exists — the chart still shows empty (needs 2 points for a trend). After the second run (~24 hours later), the chart renders with 2 points.

Also: `QualityScore` has no data source and is permanently null — the quality metric's section and chart remain in the UI but render "—" / empty-state permanently, a placeholder for future data-source wiring.

Related: **KI-038** describes structural measurement limitations of the live metrics themselves (sparse per-region data, receipt-finalization bias, answered-sessions-only response latency) — KI-043 is only about the trend chart's initial warm-up period, not those metrics' own data quality.

Resolution: none required — normal accumulation as the job continues to run nightly.

### KI-042: `admin/CreateTenantModal` has no render site in the current app; component exists but is dead code
Severity: low (dead code only — the component is correctly implemented, just unreachable from the
active UI)
Status: open — no action needed; the component is maintained but not wired to a page
Description: `frontend/features/admin/components/CreateTenantModal.tsx` includes the supplier-category
selector (per TASK-667) and is functionally complete, but there is no admin page in `app/` that
renders it. The modal was built to spec alongside the provider `CreateTenantWizard` (which *is* in
use on `/provider`), but there is no admin tenant-creation page in the navigation or routing. The
component is kept in the codebase for future use / completeness (should a `/admin/tenants/create`
page be added later), but today it is inaccessible. Not a bug or gap — a placeholder kept in sync
with the provider-side flow.
Resolution: none required.

### KI-034: `/customer-support?customerId=` deep link filters client-side over a widened page instead of a true backend filter
Severity: low (functionally correct today at realistic ticket volumes — genuinely capped by the
backend's own 200-row page-size ceiling, not an oversight the frontend could work around; the
silent-miss risk only appears at a ticket volume this platform hasn't reached yet)
Status: open — flagged by qa-tester during TASK-622's end-to-end QA of the CRM/loyalty expansion
(2026-08-24), originally noted by frontend-developer during TASK-621's own implementation.
Description: `IConsumerSupportService.GetInboxAsync(tenantId, status, page, pageSize, ct)`
(TASK-616) has no `customerId` filter parameter. When the customer-detail drawer's Tickets tab
links to `/customer-support?customerId=<id>` (TASK-621), `TicketList.tsx` handles the deep link
entirely client-side: it fetches one widened page (`pageSize=200`, matching the backend's own
`PagedQuery.ClampedPageSize` ceiling — the frontend genuinely cannot ask for more) instead of the
normal 30-per-page, then filters the fetched array by `customerId` in the browser; pagination
controls are hidden in this mode. Confirmed correct for any realistic per-customer ticket volume
(QA re-verified the 200-row ceiling matches `Pagination.cs`'s `ClampedPageSize` exactly). The
theoretical gap: a customer whose own tickets are all older than the tenant's 200 newest tickets
*across every customer* (newest-first order) would have some of their own older tickets silently
missing from the filtered view, with no error or "more results" indicator — would require a single
tenant accumulating 200+ tickets ahead of that specific customer's oldest ticket.
Resolution: add a `customerId` parameter to `IConsumerSupportService.GetInboxAsync` and its
repository method, and have `TicketList.tsx` pass it as a real server-side filter instead of the
widened-page workaround — straightforward, not urgent at today's ticket volumes. Worth doing if/
when a tenant's support-ticket volume grows.

### KI-032: Dev/demo tenant has zero ADU-eligible products — `POST /api/adu/recalculate` always processes 0, blocks live QA of any ADU-dependent feature
Severity: low (dev/demo seed-data completeness gap only — not a bug in the ADU engine or in any
feature that consumes it; a real tenant with real `SupplySchedules` configured would not hit this)
Status: open — found 2026-08-07 (TASK-495, live QA of the analytics follow-up batch,
`daysOfStockRemaining`/TASK-491/494)
Description: `AduRepository.GetEligibleProductIdsAsync` (backend/ShelfGuard.Infrastructure/Data/
Repositories/AduRepository.cs:13) requires a product to be `ManagementType == "MTS"`, have a
`DefaultSupplierId`, and that supplier must have an `IsActive` `SupplySchedule` row into the
specific store — live-confirmed `POST /api/adu/recalculate` returns `productsProcessed: 0` for
**both** seed stores on the "Свіжий Кут" demo tenant, so no `product_adu` row can ever be produced
for this tenant via the normal recalculate path today. This blocked live verification of TASK-494's
days-of-stock-remaining "populated with a real number" case — worked around by inserting one
`product_adu` row directly via SQL for the test, then deleting it (see TASK-495's log); any future
QA/demo of ADU-dependent UI (days-of-stock-remaining, the v2 auto-order buffer engine itself) will
hit the same wall on this seed data.
Not caused by TASK-479..494 — ADU/`SupplySchedules`/`MTS` eligibility is pre-existing v2-spec
functionality, untouched by this or the prior analytics initiative.
Resolution: seed at least one active `SupplySchedule` for one of the demo tenant's suppliers into
each seed store, and ensure at least a few catalog items have `ManagementType = "MTS"` +
`DefaultSupplierId` set to that supplier — then `POST /api/adu/recalculate` will have a real
eligible-product pool to compute against (still separately gated on having enough `DailySale`/sales-
window history to produce a non-null `AduEffective`, per `AduCalculator`'s own day-count thresholds).

### KI-031: Seeded `netmgr@demo.local` has zero `user_locations` grants — blocks live QA/demo as this account
Severity: low (test/demo friction only — fails closed, not a security issue; a real tenant admin
granting a real network_manager proper `user_locations` rows would not hit this)
Status: open — found 2026-08-07 (TASK-486 QA pass on the interactive-analytics-margin initiative,
confirmed pre-existing/unrelated by TASK-487 security review)
Description: `manager@demo.local` (store_manager) has 2 `user_locations` rows (both seed stores);
`netmgr@demo.local` (network_manager) has 0 — confirmed live, the only rows ever inserted into
`user_locations` in the whole dev DB are `manager@demo.local`'s two, dated 2026-07-20 18:35:54,
granted by `ea@demo.local`. Since the Stage 3 `store_scope` RESTRICTIVE RLS policy
(`20260719193545_AddLocationStoreScopeRlsPolicies.cs`, bypass list `provider`/`provider_admin`/
`worker`/`enterprise_admin` — network_manager deliberately NOT included, a TASK-393 decision) fails
closed on zero grants, `netmgr@demo.local` sees zero stock/sales/write-off data tenant-wide despite
being a real, correctly-provisioned role otherwise. Blocked live UI verification of
network_manager-tier features during TASK-486 (worked around with `ea@demo.local`/enterprise_admin
instead, which the task's brief explicitly allowed).
Not caused by TASK-479..487 (verified: `user_locations`/`store_scope` predate that initiative by
~2.5 weeks; `git log` shows no commit touching `DbSeeder.cs` since; no task log in that series lists
seed data among files touched) — a pre-existing seed-data completeness gap only, not a regression
and not a security finding.
Resolution: one SQL insert (or a `DbSeeder.cs` addition) granting `netmgr@demo.local`
`user_locations` rows for both seed stores, mirroring `manager@demo.local`'s existing 2 grants — so
future QA/demo sessions needing a live network_manager account don't hit the same wall.

### KI-005: Hardcoded bcrypt hash in DbSeeder.cs
Severity: high
Status: resolved (2026-07-14)
Description: `DbSeeder.cs` contained a hardcoded bcrypt hash (`$2a$12$eump...`) committed to source control (git history too). Anyone with repo access knew the demo password.
Resolution: `DbSeeder.SeedAsync` now takes `IPasswordHasher hasher` + optional `IConfiguration config` and hashes the password at runtime — `config["Seed:DefaultPassword"]`, falling back to `"password"` only when that key is unset (dev-only default, documented in code). `Program.cs` resolves `IPasswordHasher` via `scope.ServiceProvider.GetRequiredService<...>()` and passes `app.Configuration` at the call site. No hardcoded hash remains in source; still gated to Development/`SEED_ON_START=true` by KI-006 so it never runs unattended in production. `dotnet build` clean, `dotnet test` 805/805 green (incl. new `UserServiceCrossTenantTests`).

### KI-006: Auto-migrate + seed runs in all environments
Severity: medium
Status: resolved (2026-07-14)
Description: `Program.cs` calls `MigrateAsync()` and `DbSeeder.SeedAsync()` unconditionally. In production this risks migration race conditions (multiple replicas) and seeds demo users.
Resolution: `MigrateAsync()` stays unconditional (deploy process depends on it). `DbSeeder.SeedAsync()` is now gated: `app.Environment.IsDevelopment() || SEED_ON_START == "true"` — always seeds in Development, seeds in staging via `SEED_ON_START=true` (set in `docker-compose.staging.yml`), never seeds in Production by default (not set in `docker-compose.production.yml`).

### KI-007: Dashboard stats derived from POC Products table (fake data)
Severity: medium
Status: open
Description: Dashboard Safe/Warning/Critical/Expired cards are computed from `stockQuantity` vs `reorderLevel` in the POC `Products` table — not from real `product_stock` batches with expiry dates. "Expired" = stockQuantity is 0, which is incorrect.
Resolution: Implement TASK-011 (`/api/stock` endpoint) and TASK-012 (seed real batches), then replace `dashboardApi` to call the real analytics endpoint.

### KI-008: No pagination on GET /api/products
Severity: medium
Status: resolved (2026-07-14, verified during Block 3 pre-launch audit)
Description: Returns all products in one response. Will degrade at 1000+ items.
Resolution: Already fixed by commit `206b2534` (2026-06-18, "perf(db): database
optimization"), predating this doc's last update. The old unauthenticated POC
`/api/products` (no `tenant_id`) described below no longer exists as a real
endpoint — `ProductsLegacyController` now only issues `RedirectPermanent` to
`/api/items/*` for every verb. The real catalog lives at `/api/items`
(`ItemsController`), which is `[Authorize(Policy = CanViewStock)]`, RLS-scoped
by tenant, and paginated: `GET /api/items?page=&pageSize=` →
`PagedResult<ItemDto>` (default page=1/pageSize=50, matches the standard
envelope in `api-contracts.md`). No code change was needed — this entry is
kept only as a paper trail; the "POC products endpoint" section further below
in this file is stale and superseded by the same fix.

### KI-009: `staleTime` missing on `useProducts` hook
Severity: low
Status: open
Description: Every component mount that uses `useProducts` triggers a refetch. Dashboard hooks have `staleTime: 60_000` but inventory hook does not.
Resolution: Add `staleTime: 60_000` to `useProducts` query options.

### KI-010: Store map zones are static placeholder data
Severity: low
Status: open
Description: `StoreMap` component on dashboard renders hardcoded zone data. Real zone data requires `/api/stores/:id/zones` endpoint (not yet implemented).
Resolution: Implement stores API (part of TASK-011 or separate task), then wire `StoreMap` to real data.

### KI-011: Sidebar links to unimplemented pages show "coming soon"
Severity: low
Status: open
Description: `/stock`, `/transfers`, `/write-offs`, `/analytics`, `/notifications`, `/settings` show a catch-all "in development" page. Not a bug — intended placeholder.
Resolution: Implement each page per sprint plan.

### KI-015: POS shift-open is scoped per tenant, not per store — blocks simultaneous multi-store POS
Severity: medium (real limitation for retail chains, invisible for single-store tenants)
Status: open — plan written, not implemented (2026-07-15, Block 6 pre-launch audit, TASK-356)
Description: `PosRepository.GetOpenShiftAsync(tenantId)` has no `StoreId` filter, so
`PosService.OpenShiftAsync`'s "already open" `409` check blocks opening a shift at Store B
while Store A (same tenant) still has one open — even though `PosShift` has a per-store
unique DB constraint suggesting per-store shifts were the original intent. Root cause:
`IFiscalServiceFactory.GetForTenantAsync` and the `integration_configs` schema
(`UNIQUE (TenantId, Service)`, no `StoreId` column) only support ONE Checkbox ПРРО
registration per tenant today — that's the actual constraint, not a Checkbox platform
limitation (Checkbox's `X-License-Key` identifies one cash register; nothing stops a
tenant from holding multiple license keys, one per store). A chain wanting POS running at
more than one location simultaneously cannot today.
Resolution: full migration plan (schema, `IFiscalServiceFactory`, `IPosRepository`,
`PrroSettingsController`, frontend store selector, rollout/back-compat strategy, risk
estimate) written in `.claude/logs/tasks/356_2026-07-15_pos-fiscalization-audit_backend-developer.md`
§"Per-store shift plan" — not implemented, needs a scope decision (worth the schema
migration + multi-register Checkbox setup vs. acceptable single-register-per-tenant
limitation for now).

### KI-014: Per-IP rate limiting is ineffective in production (client IPs not preserved)
Severity: medium
Status: open (root cause outside our stack)
Description: The API's per-IP rate limiter (TASK-329) works locally (verified: 10×401 + 5×429
on 15 parallel wrong logins) but never triggers in production — 15 parallel wrong logins all
return 401. The deployed build is confirmed current (new headers + 2FA endpoints live).
Root cause (most probable): the hosting provider's port-mapping layer (external 10054 → nginx
8443) terminates TCP and does not preserve client source IPs — each connection reaches nginx
from a different internal address, so per-IP partitions (API RateLimiter, nginx limit_req on
$binary_remote_addr) never accumulate. Verify via `docker logs shelfguard_api | grep "unknown email"`
(failed logins log the perceived IP) — if IPs vary per request/connection, this is confirmed.
Impact: volumetric/distributed brute force is not rate-limited per IP. Mitigations already live
and IP-independent: per-account lockout (5 fails → 15 min), password policy (12+ chars, blocklist),
opt-in 2FA TOTP. Fail2ban caveat: if SSH source IPs are also masked/shared, sshd bans could hit
a shared egress IP (self-DoS) — check `journalctl -u ssh` / auth.log source IPs before enabling.
Resolution options: ask the provider whether real client IPs can be preserved (PROXY protocol /
X-Forwarded-For from their edge → then trust it in nginx `set_real_ip_from`), or move TLS/edge
to a layer that preserves IPs (e.g., free Cloudflare in front).
**Live-reverified 2026-07-16 (Block 18 security audit):** the IP-independent mitigations actually
work end-to-end, not just in theory — 6 sequential wrong passwords against a real staging account
locked it out (5-fail threshold), the *correct* password was then also rejected with the same
generic error while locked, `LockoutUntil` in the DB was set ~15 min out, and a different account
logged in fine at the same time (confirms per-account, not a global outage). Also confirmed TOTP
brute-force is covered by the **same** account-lockout counter, not just the (IP-partitioned, thus
KI-014-affected) per-request rate limiter: enabled real TOTP 2FA on a test account, sent 5 wrong
codes to `/api/auth/2fa/verify`, then confirmed a subsequent login with the *correct* password was
rejected (account locked) before even reaching the 2FA prompt. This meaningfully narrows KI-014's
real-world impact — password and TOTP brute force are both stopped by the IP-independent lockout
regardless of whether per-IP partitioning works in prod.

### KI-016: weather-fetch/mqtt-listener still reference the pre-rename "StoreId" column
Severity: high (silent runtime crash, not caught by `tsc`/`dotnet test`)
Status: **resolved** (Block 11, TASK-362, 2026-07-15)
Description: Found in TASK-360 (Block 9 audit) while fixing the same bug class in
`expiry-check.job.ts`/`notification.job.ts`/`stock-snapshot.job.ts`. Confirmed live against the
dev DB (`\d` per table): `weather_data`, `iot_devices`, `temperature_readings`, and
`product_stock` all have their store-scoping column as `"LocationId"` (v4 Store→Location
rename); `stock_events`/`weight_readings` were genuinely never renamed and correctly kept
`"StoreId"`/no store column at all — confirmed against `AppDbContextModelSnapshot.cs` and a live
`\d stock_events`.
- `worker/src/jobs/weather-fetch.job.ts`'s `INSERT INTO weather_data (...)` — fixed
  `"StoreId"` → `"LocationId"` in both the column list and `ON CONFLICT`.
- `worker/src/jobs/mqtt-listener.ts` — fixed the `iot_devices` device lookup (2 places:
  `handleMessage`, `checkOfflineDevices`), the `temperature_readings` INSERT, and the
  `product_stock` FEFO write-down SELECT. Left `stock_events` inserts untouched (already
  correct — genuine `"StoreId"` column there).
- **Also found in this same investigation** (same bug class, one level deeper): `weather-fetch.job.ts`
  and `ai-order.job.ts` (both `db.connect()` blocks) never called `SET app.role = 'worker'` at
  all, and `notification.job.ts`'s `handleExpiryAlert`/`handleIotAlert` likewise never set it —
  under the Block 2 fail-closed RLS fix (`20260714180000_FixFailOpenTenantIsolationOnReset`),
  every one of these queries silently returns zero rows unless the pooled pg connection happened
  to inherit `app.role` from another job reusing the same physical connection (node-pg doesn't
  reset session state on release) — correctness was depending on connection-pool luck. Fixed by
  adding the explicit `SET app.role = 'worker'` each function was missing, matching the
  established pattern in every other worker job file.
Live-verified end-to-end on the dev stack (rebuilt + restarted the worker container): enqueued a
real `weather-fetch` job with a location's lat/long temporarily set → `weather_data` populated
(7 rows, correct `LocationId`); published real MQTT messages to a temp test device → 
`temperature_readings` written with correct `LocationId`, alert threshold correctly fired at
9.5°C (fridge profile, >8°C alert) and correctly filtered a garbage 9999°C reading (see the new
plausibility check below); the resulting `temp_alert` notification job found and logged all 3
real matching users (`store_manager`/`network_manager`/`enterprise_admin`) in `notification_queue`
— proof the `SET app.role='worker'` fix actually restores RLS visibility, not just that the SQL
parses. Test rows cleaned up after verification; no dev seed data left behind.
Also added (same investigation, v3-spec §4/§1 "чи є валідація діапазонів" ask): MQTT temperature
readings now have a sanity-bounds check (`isPlausibleTemperature`/`isPlausibleHumidity` in
`worker/src/services/iot-rules.ts`, -60..60°C) before insert — a broken/miswired sensor can no
longer write physically-impossible values into `temperature_readings` or trigger a false
`temp_violation` batch flag. Weight sensors already had equivalent protection via the existing
confidence-based `assessWeightDelta` (non-multiple deltas → confidence 60, never auto-applied).
Why not caught earlier: the local dev `docker-compose.yml` worker `DATABASE_URL` was itself
broken until TASK-360 — every worker DB job failed to even connect in dev, so none of these SQL/
RLS errors surfaced until an audit could watch real job runs against a real DB.

### KI-017: `needs_verification` status never triggers a notification from the hourly cron
Severity: low (data/UX gap, not a regression)
Status: open
Description: v1-spec §2.2 defines `last_checked_at > 90 днів → status = 'needs_verification' →
сповіщення без терміну`, and `NotificationService.ValidEventTypes` already lists
`stock.needs_verification` as a real event type (v1-spec §8.2 routes it to store_manager via
Telegram). Found in TASK-360 (Block 9 audit) while fixing `expiry-check.job.ts`'s threshold
bug: the cron never computes or transitions to `needs_verification` at all, and there's no
dedicated `NotifiedNeedsVerificationAt` column to dedupe a repeat notification if it did. The
backend's own `StockStatus.Compute` (used for every live read) already computes this status
correctly for display — only the cron-triggered notification side is missing.
Resolution: needs a `product_stock` schema migration (new notified-at column) plus a new
`notification.job.ts` payload/handler — small but non-trivial scope, deliberately left out of
this task (which focused on fixing the crash bugs + aligning existing warning/critical
thresholds). Candidate for a dedicated small task.

### KI-018: Auto Service spare-part FEFO write-down is tenant-wide, not location-scoped
Severity: medium (invisible for single-location auto-service tenants, real cross-location
stock leak for chains — v4-spec explicitly lists `location_type = auto_service`, so
multi-location auto-service tenants are a supported case)
Status: **planned** (2026-07-15) — full remediation plan written, no code changed yet, see
`.claude/logs/tasks/361_2026-07-15_autoservice-production-audit_backend-developer.md`
("Addendum — KI-018 remediation plan")
Description: Found in TASK-361 (Block 10 pre-launch audit). `AsCustomer`/`AsVehicle`/
`AsWorkOrder`/`AsWorkOrderLine` have no `LocationId`/`StoreId` at all, unlike `ProductionOrder`
(which correctly scopes FEFO consumption to `order.LocationId`, see `ProductionRepository
.GetFefoOrderedAsync`). `AutoServiceRepository.GetFefoOrderedAsync(itemId, ct)` and
`AutoServiceService.CompleteWorkOrderAsync` consume spare-part stock FEFO across ALL of the
tenant's locations — a work order created at Service Bay A can write down a spare-part batch
physically sitting at Bay B. Matches the same location-scoping gap class production doesn't
have, but auto-service does.
Resolution (planned, ~1 day total): additive migration — nullable `AsWorkOrder.LocationId`
uuid FK → `locations.Id` (RESTRICT), new `(TenantId, LocationId)` index; no RLS policy
changes needed (verified live that no RLS qual in this codebase ever filters on
`LocationId`, only `TenantId`). Backend: `IAutoServiceRepository.GetFefoOrderedAsync` gets a
`locationId` param mirroring the already-correct `IProductionRepository` signature;
`CreateWorkOrderAsync`/`CompleteWorkOrderAsync` thread it through;
`GetWorkOrdersAsync`/controller gain an optional `locationId` filter, matching
`ProductionOrder`'s existing shape exactly. Frontend: `CreateWorkOrderModal.tsx` sources the
value from the already-existing `useStoreContext`/`StoreSelector` (no new UI component
needed — same wiring KI-015 already identified for POS). Open product question not yet
resolved: whether pre-migration work orders with `LocationId = NULL` fall back to today's
tenant-wide FEFO (recommended — additive, no forced backfill) or get hard-blocked until a
location is set. See the task log for full file-by-file scope and effort breakdown.
Candidate for a dedicated implementation task once scheduled.

### KI-019: IoT/Weather/Events/Cannibalization (and most of v2/v3) have no `[RequireModule]` gate
Severity: medium (billing/entitlement gap, not a security/tenant-isolation leak — role-based
`[Authorize]` still applies, RLS still scopes by tenant; a tenant simply isn't blocked from
calling a module's API even when that module isn't in their `tenants.modules` set)
Status: open — needs a product decision, not fixed here
Description: Found in TASK-362 (Block 11 audit) while reviewing `IotController.cs`. CLAUDE.md's
architecture rule states "Module activation. Feature endpoints guarded by
`[RequireModule("module_key")]`", but `IotController`/`WeatherController`/`EventsController`/
`CannibalizationController` have no `[RequireModule]` attribute at all — only role-based
`[Authorize(Policy = ...)]`. Checked how widespread this is: `grep -c RequireModule
*Controller.cs` shows only 8 controllers use it at all (`AiAssistant`→`inventory`,
`AutoService`→`auto_service`, `Marketplace`/`MarketplaceChat`/`MarketplaceCooperation`→
`marketplace`, `Production`→`production`, `SupplierCabinet`/`SupplierCabinetCooperation`→
`marketplace_supplier`) — every other controller from v2 (`Orders`, `Adu`, `Buffer`,
`AiOrders`, `Weather`, `Events`, `Cannibalization`) and v3 (`Iot`, `Pos`) has none, despite
`"auto_order"`, `"iot"`, and `"pos"` all being defined, valid module keys in `Tenant.cs`'s
`UpdateModules` allowlist.
Why not fixed here: `Tenant.DefaultModulesForBusinessType` (v4, ADR-015) does **not** grant any
business type `"auto_order"`, `"iot"`, or `"pos"` by default — e.g. `"retail"` only gets
`["inventory", "procurement", "pos"]` minus the fact `"pos"` here is the string literal but POS
endpoints aren't gated either way. Naively adding `[RequireModule("iot")]`/
`[RequireModule("auto_order")]`/`[RequireModule("pos")]` now would immediately 403 every
currently-working tenant that hasn't been manually granted that module (unknown how many —
no query run, out of caution) — a real risk of breaking already-functioning features for
near-launch clients, not a safe "objective best practice" fix. This needs a product decision:
(a) should these modules be added to the relevant default sets going forward, (b) should
existing tenants be backfilled with them, (c) is per-endpoint module gating even the intended
model for v2/v3 features, or were they deliberately left role-gated-only. Candidate for a
dedicated task once the product question is answered — do not add `[RequireModule]` blind.
Partially addressed (TASK-674, ADR-037): the "Застосунок" section (LoyaltySettings/
LoyaltyTierSettings/Banners/Discounts/PromotionCampaigns/Mobile* controllers, `mobile_app`) and
the "Аналітика" reports section (`AnalyticsController`, per-action, `analytics`) are now gated.
Chosen model for those: new keys, **no backfill**, default-off (the product answer there was
"clients who didn't pay for it should lose it"). The v2/v3 controllers listed above are still
open — that answer does not generalise (breaking existing near-launch tenants on `pos`/`iot`/
`auto_order` is a different risk calculus).

### KI-020: No frontend error tracking (Sentry or equivalent)
Severity: medium (no visibility into production JS errors — a real error boundary was added in
Block 13 that catches and console.errors client crashes, but nothing ships that log anywhere
except the browser console; the dev has zero after-the-fact way to learn a user hit an error)
Status: open — cannot be closed without a real account/DSN the user must provision
Description: Confirmed via `grep -ri sentry frontend/` (zero matches) and `package.json` (no
`@sentry/*` dependency) — there is no error-tracking SDK anywhere in the frontend. `app/error.tsx`
and `app/global-error.tsx` (new, Block 13) log to `console.error` with a `TODO(KI-020)` marker at
the exact spot Sentry's `captureException` would go, but that's it — nothing is durably recorded.
Resolution (needs the user to do the account/DSN part first, code part is small once that
exists):
1. User creates a Sentry project (sentry.io, free tier is fine to start) and gets a DSN + org/
   project slug — this step needs real credentials only the user can create, not something an
   agent can provision.
2. `npm install @sentry/nextjs` in `frontend/`, run `npx @sentry/wizard@latest -i nextjs` (sets
   up `sentry.client.config.ts`/`sentry.server.config.ts`/`sentry.edge.config.ts` +
   `next.config.js` source-map upload wiring) — or wire manually if the wizard's CI/source-map
   step is unwanted.
3. `SENTRY_DSN` (public, safe client-side) → `.env`/`.env.production`; `SENTRY_AUTH_TOKEN` (for
   source-map upload at build time) → CI secret, never committed.
4. Replace the two `console.error` calls in `app/error.tsx`/`app/global-error.tsx` with
   `Sentry.captureException(error)` (keep the console.error too, harmless in dev).
5. Decide whether to also wrap `lib/api.ts`'s `ApiError` throws (probably not — most 4xx there
   are expected/handled by the calling mutation's `onError`, would be noisy; reserve Sentry for
   actually-unhandled crashes, matching what the two boundaries already catch).
Not attempted in Block 13 — explicitly out of scope per this task's brief (needs a DSN the agent
does not have).

### KI-021: Access token stored in `localStorage`, not just in-memory (XSS exposure)
Severity: medium (defense-in-depth gap, not a currently-exploited hole — no known XSS in this
codebase today; this is about blast radius if one is ever introduced)
Status: open — evaluated in Block 13, deliberately NOT changed (real architectural change, real
risk of breaking login-persistence-across-reload if done carelessly — see below)
Description: `lib/api.ts` keeps the JWT access token in a module-level `_token` variable AND
mirrors it into `localStorage` (`sg_token`) so it survives a full page reload (`getToken()` falls
back to `localStorage.getItem(TOKEN_KEY)` when `_token` is null). Any JS that runs on the page —
including an injected XSS payload — can read `localStorage` synchronously, so a successful XSS
anywhere in the app becomes a full session-token theft, not just a same-tab DOM-manipulation bug.
The refresh token is already the safer pattern (`HttpOnly` cookie, `credentials: "include"`,
never touched by JS) — only the short-lived access token is the exposed one.
Why not fixed in Block 13 (evaluated, not a "quick safe refactor"): removing the `localStorage`
mirror and relying purely on "silent refresh from the HttpOnly cookie on app load" requires code
that **does not exist today** — traced the actual boot sequence:
- `app/(dashboard)/layout.tsx` line ~60: `if (mounted && !getToken()) router.replace("/login")` —
  a synchronous, hard gate on a token existing in memory/localStorage *before* any network call.
  With `_token` reset to `null` on every fresh page load (in-memory only) and no localStorage
  fallback, this would fire and bounce every user to `/login` on every reload/new tab, even with
  a perfectly valid `HttpOnly` refresh cookie sitting right there unused.
- `features/auth/hooks/useAuth.ts`'s `useMe()` is gated `enabled: Boolean(getToken())` — same
  problem, the "am I logged in" query would never even fire without a token already present.
- `middleware.ts` (Edge) independently checks `request.cookies.has("sg_session") ||
  request.cookies.has("refreshToken")` for its own redirect — this one is fine as-is (doesn't
  read the access token), but shows auth state is threaded through three separate layers
  (Edge middleware cookie check, client layout `getToken()` check, `useMe()` gate) that would all
  need to agree on a new "always attempt silent refresh first, then decide" bootstrap sequence.
- No code anywhere today calls `POST /api/auth/refresh` on app mount — `tryRefresh()` in
  `lib/api.ts` is currently only reachable reactively, from inside a request that already got a
  401. Removing the localStorage token requires *adding* a real "attempt refresh once on mount,
  block rendering behind it, then fall through to `/login` only if that fails" bootstrap — new
  loading-state UX, new race-condition handling with the existing 2FA challenge flow
  (`useCompleteLogin`), and new interaction with `SessionExpiredNotice`.
This is real surface area across auth boot, not a same-file fix — matches this project's existing
bar for "needs a product/architecture decision" (same caution already applied to KI-015/KI-018/
KI-019 rather than guessing). Candidate mitigations short of a full rewrite, for the user to
choose from:
(a) do nothing extra beyon what's already true — this app has no known XSS today, and every
    dependency is npm-audited per TASK-350; accept the residual risk;
(b) add a Content-Security-Policy header (defense-in-depth even with localStorage still in use —
    blocks most injection vectors from ever running attacker JS at all, which matters more than
    where the token lives);
(c) do the full bootstrap-refresh rewrite described above and drop localStorage entirely — real
    effort (touches `lib/api.ts`, `app/(dashboard)/layout.tsx`, `useAuth.ts`, `middleware.ts`,
    needs manual regression pass on login/2FA/logout/session-expiry/multi-tab), not attempted
    here without an explicit go-ahead.

### KI-013: Npgsql 8.0+ requires EnableDynamicJson() for List<string>/JSONB fields
Severity: high (silent 500 in production)
Status: resolved (2026-06-27)
Description: Npgsql 8.0+ breaking change — `List<string>` та інші складні .NET типи більше не десеріалізуються з JSONB-колонок автоматично. Без `EnableDynamicJson()` API повертає `System.NotSupportedException` → 500 на всіх GET-ендпоінтах, що читають JSONB. Проявилось після деплою поля `Barcodes: List<string>`.
Resolution: У `backend/ShelfGuard.Infrastructure/DependencyInjection.cs` замінено `UseNpgsql(connectionString)` на `NpgsqlDataSourceBuilder(...).EnableDynamicJson().Build()`, результат передається у `UseNpgsql(dataSource)`.
Rule: **При кожному новому полі `List<T>` / JSONB** — перевіряти, що `EnableDynamicJson()` вже є у `DependencyInjection.cs`. Якщо у prod-логах з'явився `InvalidCastException` / `NotSupportedException` з текстом `jsonb` — перша підозра саме тут.

### KI-022: Mobile app has no offline support
Severity: medium-high (POS register / warehouse scanning routinely run on unstable wifi/cellular)
Status: resolved in code for existing mobile forms (2026-07-29, TASK-443/TASK-444); device acceptance pending
Description: no `NetInfo`, no offline queue, no local draft persistence anywhere in `mobile/` —
confirmed via `grep -ri "netinfo|offline|asyncstorage"`, zero matches, and `package.json` has
neither `@react-native-community/netinfo` nor `@react-native-async-storage/async-storage`. Every
mutation (POS sale, write-off, transfer, stock scan) requires a live connection at the moment of
submit; a dropped connection mid-action surfaces as a generic Axios error with no retry/resume,
and the in-progress draft (cart, scanned items) is not persisted anywhere durable — only in
React state, lost on any screen unmount/crash.
Resolution: TASK-443 added owner-scoped/versioned durable POS drafts, network status, single-flight
submit, and fail-closed ambiguous-timeout/conflict handling. It intentionally does not add an
offline mutation queue: `POST /api/pos/sales` has no idempotency/reconciliation key, so ambiguous
timeouts require shift reconciliation and explicit discard. Warehouse/production drafts and the
broader offline-first work remain open. Original design context: local queue + optimistic UI,
likely `@tanstack/query-async-storage-persister` + a `NetInfo`-driven `onlineManager`) is a
substantial dedicated effort, out of scope for a pre-launch audit pass. The POS concurrency
TASK-444 added the same owner-scoped/versioned durable protection to the existing mobile
write-off, transfer, and production-order forms, with stock/reference revalidation, explicit
discard, offline guards, and retained conflict/uncertain states. Mobile currently has no
receipt-create form or approved create DTO (only processing an existing receipt), so that
product/API contract gap is tracked in the TASK-444 handoff. Android force-close restoration
still awaits TASK-435. The POS concurrency work from Block 6 (optimistic locking, 409 on double-sell) at least makes *retrying* a failed
sale safe — it just isn't automatic. Needs a product decision on priority before scheduling.

### KI-023: Mobile login silently mishandled 2FA-enabled accounts — implementation resolved, device verification pending
Severity: medium
Status: resolved in code (2026-07-29, TASK-438); live device acceptance pending
Description: TASK-330/331 added opt-in TOTP 2FA on web. `POST /api/auth/login` returns
`{requiresTwoFactor: true, challengeToken}` (no tokens) for 2FA-enabled accounts, but mobile's
`login()` blindly destructured `{accessToken, user}` from every response — a 2FA-enabled user
logging in via mobile got `setAuth(undefined, undefined)` and was silently navigated into the
app with a broken token, no visible error.
Resolution: TASK-438 implements the existing challenge contract in mobile. Password login now
routes 2FA-enabled staff to a dedicated TOTP/recovery-code screen, verifies through
`POST /api/auth/2fa/verify`, stores the challenge only in memory, and clears it when leaving the
flow. Invalid verification `401` responses are excluded from authenticated refresh handling.
Automated contract/state/security checks pass. A live Android sign-in with real TOTP and recovery
codes remains part of blocked TASK-435 device QA; do not mark final acceptance complete until then.

### KI-027: Staging (and dev) Postgres connection role is a superuser — RLS is completely bypassed
Severity: critical for the validity of any live security/RLS test run against staging/dev; NOT
confirmed to be a live production issue (see KI-028 note below)
Status: ✅ resolved (2026-07-16, Block 18) on both staging and dev — user-authorized in chat.
Resolution: created a dedicated `NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS` role that OWNS all
84 public tables + sequences on each stack (`shelfguard_staging_app` on staging,
`shelfguard_app_dev` on dev), transferred ownership via a `DO $$ ... ALTER TABLE %I OWNER TO $$`
loop, `GRANT CONNECT` + `GRANT USAGE, CREATE ON SCHEMA public`, and repointed the app connection
strings at it (`.env.staging` `DATABASE_URL`/`WORKER_DATABASE_URL`; dev `appsettings.Development.json`
`DefaultConnection` + `docker-compose.yml` worker `DATABASE_URL`). The bootstrap superusers
(`shelfguard_staging` / `crm`, = `POSTGRES_USER`) stay ONLY for initdb/admin/psql and now own
nothing. Restarted api+worker on both stacks; verified live: as the app role, `rolsuper=f
rolbypassrls=f`; scoped to one tenant, `product_stock`/`items` show that tenant's rows with **0
cross-tenant leak**; with `app.tenant_id` unset (RESET) → **0 rows** (fail-closed); as `app.role =
'worker'` → sees all rows (worker_bypass intact for cron jobs). Dev API boots clean, dev worker
connects clean, `dotnet test` 854/854. **Known follow-up (not blocking):** the `DbSeeder` has only
ever run under a superuser; on a *fresh empty* DB, seeding tenant-scoped rows (e.g. `users`, FORCE
RLS) as the new non-superuser role would be blocked by the fail-closed `tenant_isolation` policy
with no tenant context set. Both current dev/staging DBs are already seeded so `DbSeeder`'s
`if (Tenants.AnyAsync()) return` short-circuits and this path is never hit — but a `docker compose
down -v` + fresh boot would fail at seeding until `DbSeeder` sets `SET app.role='provider'` (or
equivalent provider_bypass) around its inserts. Production never seeds (KI-006), so prod is
unaffected. Also worth baking the role-separation into any future environment-bootstrap script so
it can't be forgotten again (this is exactly how staging shipped without it in Block 0).
Description: Found while live-testing cross-tenant IDOR on staging for Block 18. Created a second
tenant via `POST /api/admin/tenants` and confirmed, with real HTTP requests, that its user could
read tenant 1's item/stock-batch/location records in full via `GET /api/items/{id}`,
`GET /api/stock/{id}`, `GET /api/locations/{id}` (all HTTP 200 with complete cross-tenant data) —
despite RLS `tenant_isolation` policies existing on every one of those tables. Root cause:
`docker exec shelfguard_staging_postgres psql -U shelfguard_staging -d shelfguard_staging -c
"SELECT rolname, rolsuper, rolbypassrls FROM pg_roles WHERE rolname='shelfguard_staging'"` shows
`rolsuper=t, rolbypassrls=t` — Postgres superusers bypass RLS unconditionally, `FORCE ROW LEVEL
SECURITY` notwithstanding (this is documented Postgres behavior, not a bug in the policies
themselves — same class of issue as the historical production incident in
`feedback-rls-superuser-bypass` memory, where the bootstrap user `shelfguard` was found to be a
superuser and fixed by creating a separate non-superuser `shelfguard_app` role + `ALTER TABLE ...
OWNER TO` for every table). `docker-compose.staging.yml`/`.env.staging` never repeated that fix
when Block 0 stood up the staging stack — `POSTGRES_USER=shelfguard_staging` was used directly as
the app's `DATABASE_URL`/`WORKER_DATABASE_URL` connection user, and Docker's postgres image always
makes the `POSTGRES_USER` value a cluster superuser at initdb time. All 84 tables in the staging DB
are owned by `shelfguard_staging`.
Why not fixed directly: applying the proven fix (create `shelfguard_staging_app` non-superuser
role, `ALTER TABLE ... OWNER TO` for all 84 tables, update `.env.staging`, restart api+worker) was
attempted live and blocked by the harness's auto-mode permission classifier as "a persistent
infrastructure change beyond the requested security-audit task, not explicitly authorized for this
session." Did not attempt a workaround — flagging for the user to explicitly authorize instead.
Impact while open: any further "live" IDOR/cross-tenant test against staging is meaningless (RLS
never runs) until this is fixed. Does not by itself prove a production vulnerability — see KI-028.
Resolution (ready to execute once authorized): same pattern as the documented production fix —
`CREATE ROLE shelfguard_staging_app WITH LOGIN PASSWORD '<new>' NOSUPERUSER NOCREATEDB NOCREATEROLE
NOBYPASSRLS;`, transfer ownership of all `public` tables (and sequences) via a `DO $$ ... ALTER
TABLE %I OWNER TO shelfguard_staging_app $$` loop, `GRANT CONNECT`/`GRANT USAGE, CREATE ON SCHEMA
public`, then update `DATABASE_URL`/`WORKER_DATABASE_URL` in `.env.staging` to the new user and
restart `shelfguard_staging_api`/`shelfguard_staging_worker`. Also worth a `devops-engineer`
follow-up: bake this into whatever script/compose file stands up a fresh Postgres cluster (staging
or any future environment) so it can't be forgotten again — nothing today automates or checks it.

### KI-028: Single-object `GetByIdAsync` repository methods have zero app-level tenant filter — RLS is the *sole* tenant-isolation layer for these reads
Severity: medium (architectural observation/hardening gap, not a currently-exploited hole in
production — see rationale below)
Status: ✅ mitigated (2026-07-16, Block 18) via option (b) — a startup canary, user-authorized in
chat. `Program.cs` now runs, right after `MigrateAsync`, a check of whether the connected role
bypasses RLS (`SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user`). The
decision policy lives in the pure, unit-tested `RlsRoleGuard.Evaluate(roleBypassesRls,
isDevelopment)` (`ShelfGuard.Infrastructure/Data/RlsRoleGuard.cs`, 4 tests in
`ShelfGuard.Tests/Infrastructure/RlsRoleGuardTests.cs`): a role that bypasses RLS **fails the app's
startup outside Development** (throws → refuses to boot) and **logs CRITICAL but allows boot in
Development** (a fresh clone / CI / not-yet-migrated local box may legitimately still be a superuser;
warned loudly, not blocked). This catches the exact KI-027 class of misconfiguration the moment any
environment boots, automatically, in any future stack — the root cause of KI-027 shipping unnoticed
in Block 0. The deeper defense-in-depth options below (explicit `&& TenantId==` filters in every
repo) were NOT done — the canary + KI-027's role fix are judged sufficient; left as an optional
future hardening decision. Original write-up retained below for context.
Original status: open — flagged, not fixed (would require touching ~15-20 repository methods across
modules, out of scope for an audit-only pass without a broader decision)
Description: Found while investigating KI-027. Read `ItemRepository.GetByIdAsync`,
`LocationRepository.GetByIdAsync`, `StockRepository.GetByIdAsync` (and this pattern repeats across
most `Get*ByIdAsync` methods in `ShelfGuard.Infrastructure/Data/Repositories/*`) — all three query
only `WHERE x.Id == id`, with **no** `&& x.TenantId == tenantId` clause at the application/LINQ
level. Tenant scoping for these reads depends 100% on the Postgres RLS policy (`app.tenant_id`
session variable set by `TenantConnectionInterceptor`) ever actually being enforced by the
connected role. This matches the codebase's documented intent (`CLAUDE.md`: "Tenant isolation via
RLS" — the app is deliberately not supposed to duplicate `WHERE TenantId=` in every repository
method, trusting the DB layer completely) and is a legitimate, common pattern for RLS-based
multi-tenancy — but it means there is no defense-in-depth second layer: if RLS is ever bypassed for
any reason (KI-027's superuser-role class of bug, a future migration mistake, a maintenance script
connecting as a different role, connection-pool misconfiguration), single-object reads leak
cross-tenant data completely silently, with no error, no log line, nothing to catch it.
Why this is currently believed NOT to be a live production issue: `feedback-rls-superuser-bypass`
memory documents that production already went through exactly this incident once and fixed it by
switching the real app connection to a dedicated non-superuser `shelfguard_app` role with
`ALTER TABLE ... OWNER TO` applied — i.e., production's `DATABASE_URL`/`WORKER_DATABASE_URL` are
believed to use `shelfguard_app`, not a superuser. This audit could not directly re-verify
production's current role (`.env.production` doesn't exist locally, and re-verifying via SSH was
out of scope — "прод не чіпаємо" for this block) — flagging this as the one open assumption behind
"production is fine."
Resolution options for the user to choose from, none executed here: (a) accept the risk as-is,
matching the codebase's existing "trust RLS completely" architecture, and treat KI-027's fix
(restoring proper role separation on staging) as sufficient going forward — cheapest; (b) add a
cheap canary: a startup health check that runs `SELECT usesuper FROM pg_user WHERE usename =
current_user` against the configured connection and refuses to start (or logs CRITICAL) if true —
catches this exact class of misconfiguration the moment a new environment is stood up, in any
environment, automatically; (c) add explicit `&& x.TenantId == tenantId` defense-in-depth filters
to the highest-risk single-object endpoints (items/stock/locations/customers/receipts/write-offs) —
real code change across many files, most thorough but not a quick fix. No code changed for this
finding.
**Update (2026-08-30) — KI-036 is the first confirmed live instance of the trigger this issue
hypothesizes.** "A code path that runs SET ROLE / SET app.role" was realized in production code:
`MarketplaceRepository.SetProviderRoleAsync` leaked a session-level `SET app.role='provider'` for
the rest of the HTTP request, and because `ItemRepository.GetByAnyBarcodeAsync`/`GetByIdAsync` carry
no app-level `TenantId` filter (exactly the gap described above), the result was silent cross-tenant
`Item` disclosure plus a cross-tenant write vector — not merely a theoretical exposure. This
strengthens the case for the still-unadopted defense-in-depth option (c). ADR-035 adopts a **scoped**
version of (c) — `&& TenantId == clientTenantId` filters on the one marketplace consumer that sits
next to that bypass — but explicitly not codebase-wide; the broader decision remains open.

### KI-029: A validating `ADD CONSTRAINT` FK migration on an already-populated column can crash the app on startup under RLS + a non-superuser connection
Severity: high (would have caused a production deploy outage, not just a local inconvenience)
Status: mitigated for its one current instance (2026-07-19, TASK-392); the general risk remains
for any future migration that adds this shape of constraint.
Description: Found while applying TASK-392's `FixUserLocationColumnMapping` migration
(`users.LocationId → locations.Id`) locally. `dotnet ef database update` failed with
`23503: violates foreign key constraint` even though the referenced rows were genuinely all
valid (confirmed via a `LEFT JOIN` orphan check). Root cause: production applies migrations via
`db.Database.MigrateAsync()` in `Program.cs:159-163`, on the app's own regular connection — the
same connection KI-028's canary later confirms is a non-superuser, `NOBYPASSRLS` role (see
KI-027/KI-028 above). Migrations run with no `app.tenant_id`/`app.role` session variable set (no
request context exists yet), so any table with `FORCE ROW LEVEL SECURITY` — like `locations` —
is invisible to that connection: `SELECT count(*) FROM locations` returns 0 under it, even though
rows exist. When a migration validates a new FK against such a table (the default behavior of
`migrationBuilder.AddForeignKey(...)` when the dependent column already has non-null data),
Postgres's row-by-row FK check sees zero matching rows and rejects every existing value as
orphaned — the migration throws, `MigrateAsync()` throws, and the container never finishes
starting. Since `deploy.sh` stops the old containers before starting new ones, a migration that
hits this would have produced real downtime (Bad Gateway) until the next successful deploy — not
merely a local dev annoyance.
Resolution (this instance): rewrote the FK as raw SQL with `NOT VALID` — a standard Postgres
zero-downtime pattern (`ALTER TABLE users ADD CONSTRAINT ... NOT VALID`). This enforces the FK
for all new/updated rows immediately but skips validating pre-existing rows at `ADD CONSTRAINT`
time, so there is nothing for the RLS-blind connection to check and the failure mode disappears
entirely. Re-verified by rolling back and reapplying all three TASK-392 migrations through the
actual restricted `shelfguard_app_dev` role (not the `crm` superuser) — succeeded; confirmed
`pg_constraint.convalidated = false` and that the constraint still rejects a bad FK value on a
live `UPDATE`. A `VALIDATE CONSTRAINT` follow-up (to flip `convalidated` to `true` once someone
confirms via a superuser/bypassrls connection that existing rows are clean) is left as a TODO
comment in the migration file — non-blocking, can run manually whenever convenient.
General risk (not fully closed): any future migration that adds a validating FK constraint
(`migrationBuilder.AddForeignKey`, or plain `ADD CONSTRAINT` without `NOT VALID`) referencing an
RLS-protected table, on a column that may already hold non-null data in any deployed environment,
can hit this exact failure — regardless of whether the data is actually clean. Recommendation for
future database-engineer work: default to `NOT VALID` + a follow-up `VALIDATE CONSTRAINT` TODO
for any FK added to a pre-existing, potentially-populated column, rather than relying on knowing
the column is "almost certainly empty everywhere."

### KI-030: `TenantRole` capabilities (ADR-020) never reach the JWT on login or refresh — the entire role-or-capability escape hatch is silently inert tenant-wide
Severity: high (not a tenant-isolation/security *leak* — the opposite: capability grants a tenant
admin believes they've made are silently never honored; a whole, previously-shipped authorization
feature is dead in production for every tenant)
Status: open — found 2026-08-06 (TASK-476, Фаза 4 post-campaign E2E acceptance), not fixed. Not a
Фаза 4 regression, not caused by TASK-471/472/473/474/477's own code — pre-existing, unrelated
platform bug, out of scope for that task to fix.
Description: Every user's JWT `capabilities`/`tabs` claims are always empty on every password
login and every token refresh, regardless of whether that user has a real `TenantRoleId` with a
non-empty `TenantRole.Capabilities` list. Root cause: `TenantConnectionInterceptor.GetSetSql()`
(`backend/ShelfGuard.Infrastructure/Interceptors/TenantConnectionInterceptor.cs:64-69`) correctly
`RESET`s `app.tenant_id` for unauthenticated requests (`/api/auth/login`, `/refresh`, 2FA verify —
needed for `users`' own RLS carve-out), but `AuthService.IssueTokensAsync`
(`backend/ShelfGuard.Application/Features/Auth/AuthService.cs:469-475,515-525`) also queries
`tenant_roles` via `BuildEffectiveCapabilitiesAsync`/`BuildEffectiveTabsAsync` on that same RESET
connection. `tenant_roles`' RLS policy has no equivalent carve-out (`TenantId =
(NULLIF(current_setting('app.tenant_id'), ''))::uuid` — the standard fail-closed guard, correctly
strict for every other unauthenticated context) — with `app.tenant_id` NULL, the table is
completely invisible mid-login, so the lookup always returns `null` and both Build* methods always
fall through to `[]`. Live-confirmed for 2 real users with real non-empty `TenantRole` grants on
the dev tenant "Свіжий Кут" — both get `"capabilities": []`/`"tabs": []` in the raw login response
body (not just a missing JWT claim). Contrast: `GetCurrentUserAsync` (`/api/auth/me`) calls the
identical `BuildEffectiveCapabilitiesAsync` but on an *authenticated* connection (`app.tenant_id`
already set), so it computes correctly — meaning the admin UI can show a `TenantRole`'s
capabilities correctly (reads are authenticated), while the JWT actually minted for that role's
assignee never carries them, for the token's entire lifetime. Impact: every controller using
`RoleOrCapabilityRequirement`/`RoleOrCapabilityHandler` (`AppPolicies.cs` lists 7+: Schedules,
Analytics, Integrations ×2, Orders, Suppliers, AiOrders ×2, Users, MarketingAnalytics) has its
capability-widening half silently dead — only the base-role branch ever actually grants access.
The documented, intended ADR-020 workflow ("delegate one capability to a lower-ranked role without
granting the full higher role") currently does nothing: the grant looks correct in the admin UI
but never unlocks anything for its assignee. Why the existing test suite missed it: every
capability test (`AuthServiceCapabilitiesTests.cs`) mocks `ITenantRoleRepository` entirely, so none
exercise the real EF query against the real RLS-guarded table on a RESET connection — the same
"mocked repository hides a real RLS-interaction bug" shape TASK-476 also found twice more in Фаза
4's own code that session (phone-matching, unknown-tokens export), suggesting a recurring blind
spot in this codebase's test strategy at exactly the DB/RLS boundary, not three unrelated
coincidences. Full repro, decoded-JWT evidence, and code citations:
`.claude/logs/reviews/bug-task476-tenantrole-capabilities-never-reach-jwt_2026-08-06.md`.
Resolution (not applied — needs a decision on fix shape before implementation, flagged for a
dedicated backend-developer + security-reviewer follow-up given the auth-boundary sensitivity):
(a) give `tenant_roles`' RLS policy the same NULL-`tenant_id`-passthrough carve-out `users` already
has — broadest fix, smallest diff, but widens `tenant_roles`' visibility during any unauthenticated
connection state tenant-wide, needs the same scrutiny this codebase's own KI-027/KI-028 history
already applies to RLS carve-outs; (b) narrower — have `IssueTokensAsync` run the `tenant_roles`
lookup inside a scoped `SET LOCAL app.role = 'provider'`-style bracket for just that one query,
mirroring the existing `worker`/`provider_bypass` pattern, without loosening the table's general
policy. Either way, add a real integration test that performs an actual login through a real
RLS-enabled Postgres connection for a user with a real `TenantRoleId` and asserts the resulting
capabilities/tabs are non-empty — the category of test currently missing that would have caught
this.

