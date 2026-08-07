# TASK-487: Security review — margin authorization (interactive analytics + margin plan)

**Agent:** security-reviewer
**Date:** 2026-08-07
**Status:** done — **verdict: SHIP.** No blocking findings. 1 LOW/informational note (pre-existing
architectural pattern, not introduced by this initiative, live-confirmed not currently exploitable).
Both QA-flagged items resolved — neither is a new finding.

## Context

Read the plan (`iterative-purring-sifakis.md`), ADR-027, TASK-480/481/482 logs, and
`.claude/logs/tasks/486_*.md` (QA, verdict SHIP) before reviewing. QA already verified the happy
path (margin null/present correctly per role, DOM + raw API, exact-cent arithmetic) — this pass
targets the authorization-bypass and isolation angle QA explicitly deferred: adversarial testing,
not re-confirmation of the functional behavior.

## Scope 1-6 verdicts

**1. `AnalyticsAuthorization.CanViewMargin` — OK.** `AtLeastNetworkManagerRoles =
[provider, enterprise_admin, network_manager]` (`AppPolicies.cs:111-112`) — correctly one tier
above the controller's own `CanViewAnalyticsRoles` (+store_manager) floor; store_manager's
exclusion is unit-tested and live-reconfirmed below. Shape matches
`MarketingAnalyticsAuthorization.CanExportPii` exactly (imperative in-body check, role-OR-capability,
same `TenantRoleAuthorization.HasCapability` helper reading only the JWT `capabilities` claim — no
header/query/body input anywhere in the chain). Traced `includeMargin` end to end
(`AnalyticsController.cs:153,321` → `AnalyticsService.cs` thin pass-throughs → `AnalyticsRepository.cs`
null/compute branch) — no `||`/`&&` inversion, no client-supplied override at any layer. `losses/
by-product` correctly has no `CanViewMargin` call and no margin fields in its DTO at all (by design,
ADR-027 §1). `TenantRoleCapabilities.AnalyticsViewMargin` appears exactly twice (its own `Groups`
entry, and `All`) — grep-confirmed no other policy/group references it, no scope creep (item 5, OK).

**2. Adversarial null enforcement — OK, live-tested against the running dev API** (`dotnet run
--project backend/ShelfGuard.Api`, real `manager@demo.local` store_manager JWT):
- Garbage `?includeMargin=true` on `pos/products/{id}/trend` and `by-category/products` → margin
  stayed `null` (no such parameter exists on either action signature; ASP.NET silently ignores
  unbound query keys).
- Spoofed `X-Role: network_manager` / `X-User-Role: network_manager` / `Role: enterprise_admin`
  headers alongside the legitimate store_manager token → margin stayed `null`.
- JWT payload hand-tampered (`store_manager` → `network_manager`, original signature kept) →
  rejected (400), not accepted with elevated access — signature validation holds.
- Positive control (real own-tenant product, same token) → 200, correct revenue/quantity data,
  margin `null` — confirms the endpoint isn't just failing closed for unrelated reasons.

**3. Tenant/store isolation — OK, live cross-tenant tested with real data (API-level, not UI).**
Dev DB actually has 20 tenants (not just "Свіжий Кут" — checked live). Used tenant "Loyalty
Concurrency Test 97c8c0230d7b4743b5a3b5eb35fc6f81" (`6de9d36b-…`, real Items/Locations/
PosTransactions) as tenant B, `manager@demo.local` (tenant A, store_manager) as the attacking
caller, via raw curl (bypasses the UI/frontend entirely):
- `pos/products/{tenantB-productId}/trend` → clean **404**, no data (`GetProductSalesTrendAsync`
  explicitly filters `Items` by `TenantId` on top of RLS, `AnalyticsRepository.cs:669-671`).
- `by-category/products?category_id=<tenantB-category>` (inserted a temp probe category row into
  tenant B for this test — dev DB has 0 rows in `categories` tenant-wide, pre-existing and
  unrelated) → `products: []`, `categoryName: "Unknown"` — **not** the tenant-B probe name, no
  leak. Row deleted after the test.
- `losses/by-product?store_id=<tenantB-storeId>` → empty (`totalLoss: 0, products: []`), no leak.
- All three confirmed enforced by the query itself (explicit tenant filters + RLS), not by UI
  never constructing the URL, per the brief's explicit ask.

One **LOW/informational** note found while tracing this: `GetCategoryProductBreakdownAsync`'s
`CategoryName` lookup (`AnalyticsRepository.cs:389-394`) has **no explicit `TenantId` filter** —
unlike every other query in the same method (stock/sales sides both filter `TenantId` explicitly).
It relies solely on RLS. Live-tested above and confirmed **not currently exploitable** (the app
connects as the non-superuser `shelfguard_app_dev` role per KI-027's fix, so RLS is genuinely
enforced and the cross-tenant lookup correctly returns nothing). This is the same shape as the
already-tracked, already-accepted **KI-028** ("single-object `GetByIdAsync`-style reads have zero
app-level tenant filter — RLS is the sole isolation layer") — not a new pattern this initiative
introduced, and KI-028 already has a mitigation (startup RLS-bypass canary) covering this class of
risk codebase-wide. Not filing a new KI for it; flagging here so it's on record and can be swept
into a future KI-028 defense-in-depth pass if one is ever scheduled.

**4. Input validation / injection — OK, live-tested.**
- `reason=' OR '1'='1` and `reason=x'; DROP TABLE write_offs; --` on `losses/by-product` → both
  clean `200`, empty result, `write_offs` row count unchanged (3 before/after) — confirmed
  parameterized (grep: zero `FromSql`/`ExecuteSql` anywhere in `AnalyticsRepository.cs`, every
  query is plain LINQ-to-EF).
- Malformed GUIDs on `category_id`/`store_id` query params → clean `400` (`[ApiController]`
  automatic model-validation), never a 500/stack trace.
- Malformed GUID in the `productId` route segment → `404` (route's `:guid` constraint), no crash.
- Garbage `group_by` value (incl. an injection-shaped string) → silently degrades to `"day"`
  (`groupBy == "week" ? "week" : "day"`), correct data, `200`.
- 3000-char garbage `reason` → clean `200`, empty result.
- `items` row count unchanged (22 before/after) — no destructive side effect from any payload.

**5. Capability scope — OK.** Covered under item 1 above — no scope creep.

**6. RLS sanity on the 3 new repository methods — OK.** All plain LINQ-to-EF (grep-confirmed zero
raw SQL in `AnalyticsRepository.cs`) against the same `DbSet`s every pre-existing method in the file
already uses. `product_stock`/`pos_transactions`/`write_offs` carry the Stage 3 `store_scope`
RESTRICTIVE RLS policy (`20260719193545_AddLocationStoreScopeRlsPolicies.cs`); its own doc comment
confirms child tables (`pos_transaction_items`, `write_off_items`) inherit it automatically through
any join/subquery — exactly the access pattern the 3 new methods use, no bypass. `Items`/
`Categories` are correctly catalog-level (tenant_isolation only, no store dimension), consistent
with how the pre-existing `GetByCategoryAsync` already treats them.

## QA-flagged items (from TASK-486)

**F2 (netmgr@demo.local zero `user_locations` grants) — CONFIRMED pre-existing and unrelated to
TASK-479..486.** Evidence: `user_locations` table added by migration `20260719120844_AddUserLocations.cs`
(commit `516a4178`, TASK-393 Stage 1) and the `store_scope` RESTRICTIVE policy — whose bypass list
(`provider, provider_admin, worker, enterprise_admin`; **network_manager deliberately excluded**,
per that migration's own doc comment: "come directly from the brief agreed with project-architect")
— by commit `3d1b0462` (TASK-393 Stage 3), both ~2.5 weeks before this initiative's TASK-479
(2026-08-07). Live DB query: the *only* two rows ever inserted into `user_locations` are
`manager@demo.local`'s, dated `2026-07-20 18:35:54`, granted by `ea@demo.local` — never via
`DbSeeder.cs` (zero `UserLocation`/`user_locations` references in that file) and never touched
since (`git log --since=2026-08-06 -- DbSeeder.cs` → no commits; no TASK-479..486 log lists
`DbSeeder.cs` or seed data among files touched). `network_manager`'s exclusion from the bypass list
is an intentional, already-reviewed TASK-393 decision, orthogonal to `CanViewMargin`'s
network_manager+ floor — a real network_manager *with* proper grants would see their stores' data
exactly like store_manager does today, and would also clear the margin floor; both mechanisms work
correctly and independently. **Added `KI-031`** (`.claude/docs/known-issues.md`) documenting the
seed-data gap for future QA/demo sessions — low severity, not a security issue (fails closed).

**KI-030 cross-reference — CONFIRMED accurate, not a duplicate.** Independently re-read
`AnalyticsAuthorization.cs`/`TenantRoleAuthorization.HasCapability` — the capability branch reads
the JWT `"capabilities"` claim exclusively. Independently re-confirmed live: all 3 real logins
performed during this review (`manager@demo.local`, `ea@demo.local`, `netmgr@demo.local`) returned
`"capabilities":[]` in the raw response body, matching KI-030 exactly. The capability-widening half
of `CanViewMargin` is inert today for the same already-tracked root cause as every other
`RoleOrCapability` policy; the network_manager+ role-floor branch is the only live path and is
confirmed working throughout this review. Correctly not re-filed as new.

## Verification performed

- `dotnet build ShelfGuard.sln` — 0 errors, 0 warnings.
- `dotnet test ShelfGuard.sln` — **1333/1333 green**, matches TASK-486's own baseline exactly,
  independently reconfirmed (not just trusted).
- Live dev stack: `docker compose` services already up (postgres/redis/mosquitto/worker),
  `dotnet run --project backend/ShelfGuard.Api` on `:5000`. Real logins, raw `curl` against all 3
  new endpoints plus adversarial header/param/payload variants, direct `psql` queries for ground
  truth and cross-tenant test data.
- Cleanup: temporary probe category row deleted (verified `categories` back to 0 rows), backend
  process stopped (port released), no other data mutated — read-only pass except the one
  inserted-then-deleted test row.

## Overall verdict

**SHIP.** All 6 scope items pass, including live adversarial testing beyond what TASK-486 covered
(claim tampering, header/param spoofing, real cross-tenant IDOR with a second tenant's real data,
injection payloads). One LOW/informational note (category-name tenant filter relies on RLS only,
same accepted shape as KI-028, confirmed not exploitable today) — not blocking. Both QA-flagged
items resolved with evidence, neither changes the ship decision.

## Files reviewed

- `backend/ShelfGuard.Infrastructure/Authorization/AnalyticsAuthorization.cs`,
  `MarketingAnalyticsAuthorization.cs` (precedent), `TenantRoleAuthorization.cs`, `AppPolicies.cs`
- `backend/ShelfGuard.Api/Controllers/AnalyticsController.cs`
- `backend/ShelfGuard.Application/Features/Analytics/AnalyticsService.cs`, `IAnalyticsService.cs`,
  `IAnalyticsRepository.cs`, `Dtos/AnalyticsDtos.cs`, `Dtos/PosAnalyticsDtos.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/AnalyticsRepository.cs`
- `backend/ShelfGuard.Domain/Constants/TenantRoleCapabilities.cs`
- `backend/ShelfGuard.Tests/Authorization/AnalyticsAuthorizationTests.cs`
- `backend/ShelfGuard.Infrastructure/Migrations/20260719193545_AddLocationStoreScopeRlsPolicies.cs`,
  `20260719120844_AddUserLocations.cs`
- `backend/ShelfGuard.Infrastructure/Data/DbSeeder.cs` (grep only, for F2)
- `frontend/lib/roles.ts` (`canViewAnalyticsMargin`), `CategoryDetailPanel.tsx`,
  `PosProductTrendPanel.tsx`, `ProductAnalyticsTab.tsx` (spot-check only — UI gate consistency,
  not the enforcement boundary)
- `.claude/docs/decisions.md` (ADR-027), `.claude/docs/known-issues.md` (KI-027/028/030, +KI-031 added)
- `.claude/logs/tasks/480_*.md`, `481_*.md`, `482_*.md`, `486_*.md`

## Git

Not committed (repo convention — main session/user commits; docs-only log + KI/task-status updates).
