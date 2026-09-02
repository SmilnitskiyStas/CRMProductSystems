# Known Issues — Archive (resolved / historical)

Split out of `known-issues.md` on 2026-09-02. Full text of resolved issues; `grep` by KI-ID. Open issues stay in `known-issues.md`.

---

### KI-036: Session-level `SET app.role='provider'` in `MarketplaceRepository` leaked for the whole HTTP request — cross-tenant catalog disclosure + cross-tenant write vector in the B2B marketplace ✅ resolved + deployed (2026-08-30, TASK-641..645, commit `f14ea7f6`)
Severity: **critical** — confirmed cross-tenant data disclosure AND a confirmed cross-tenant write
vector, on real production data (TASK-642 verified live on prod that `items.provider_bypass` is
`FOR ALL` PERMISSIVE with `WITH CHECK = NULL`, so the leaked role grants cross-tenant read *and*
write; a fail-closed `tenant_isolation` cannot contain it because PERMISSIVE policies OR together).
Status: resolved by TASK-641..645 (2026-08-30) — implemented (TASK-643 + 643b remediation),
independently reviewed pre-impl (TASK-641) and post-impl (TASK-645: SHIP-WITH-CHANGES → C1/C2
remediation confirmed → final verdict **SHIP**), real-Postgres RLS regression coverage added
(TASK-644, leak proven to fail pre-fix). **Committed `f14ea7f6` and auto-deployed to production
2026-08-30** (CI green incl. "Deploy → production"; prod API verified serving post-deploy). Found
by the user at marketplace checkout; root-caused by the main session + 3 Explore agents + a Plan
agent, then TASK-641's threat model.
Exact repro: a client tenant whose own `Item` catalog is **empty** places a marketplace order from a
supplier whose `SupplierItem` has an EAN barcode that also exists in a **third** tenant's catalog.
At checkout the "Знайдено збіги штрихкодів" dialog appears and shows that third tenant's `Item` —
`id`, `name`, `imageUrl`, full barcode list — claiming the client's order lines already exist in
their catalog "under another name", even though the catalog is empty. Chain:
`POST /api/marketplace/suppliers/{id}/orders/conflicts` →
`MarketplaceOrderService.CheckCatalogConflictsAsync` → `GetSupplierTenantIdAsync` +
`GetSupplierItemsAsync` (both call `SetProviderRoleAsync` — leak starts) →
`_items.GetByAnyBarcodeAsync(barcodes)` runs under the leaked `provider` role → matches across every
tenant's catalog → `new MarketplaceOrderConflictingItemDto(match.Id, match.Name, match.ImageUrl, match.Barcodes)`.
Root cause: `MarketplaceRepository.SetProviderRoleAsync` (`MarketplaceRepository.cs:410-419`,
pre-fix) — `conn = _db.Database.GetDbConnection(); if (conn.State != Open) await conn.OpenAsync(ct);`
then `cmd.CommandText = "SET app.role = 'provider';"`, executed and **never reset**. Three
compounding defects: (1) `SET` not `SET LOCAL`, no enclosing transaction → the GUC persists for the
whole session; (2) the manual `conn.OpenAsync` makes EF treat the connection as externally-owned and
stop closing it after each query, so `TenantConnectionInterceptor.ConnectionOpenedAsync` never
re-fires to restore the caller's real role; (3) nothing resets it. Every subsequent statement in the
same HTTP request runs as `app.role='provider'`, and `items.provider_bypass` (PERMISSIVE `FOR ALL`,
`WITH CHECK` defaults to `USING`) OR-ed with `tenant_isolation` makes every row of every tenant
readable and writable. First confirmed live realization of the KI-028 risk class ("a code path that
runs SET ROLE / SET app.role").
Full blast radius (TASK-641 §1/§6, threat-model change R6):
- **(i) read disclosure** — foreign `Item` `id`/`name`/`imageUrl`/`barcodes` via
  `CheckCatalogConflictsAsync`. The disclosed `id` is also the primary key that arms (ii).
- **(ii) write vector (F2)** — `catalogAction:"link"` + the just-disclosed foreign `Item.Id` as
  `linkedItemId` → `PlanCatalogOutcomeAsync` resolves the foreign row (`_items.GetByIdAsync`, no
  app-level filter, `provider_bypass` `USING` = true), the barcode-intersection guard passes
  trivially (the id came from a barcode match), `ExecuteCatalogPlanAsync` sets `SourceSupplierItemId`
  and calls `_items.Update` → the UPDATE on the foreign tenant's row succeeds under
  `provider_bypass`'s defaulted `WITH CHECK`. Because `DbSet.Update` marks the whole loaded graph
  `Modified`, the flush also emits full-row UPDATEs against the foreign tenant's `categories` /
  `product_segments` / `suppliers` rows (`.Include`d by `GetByIdAsync`). Values are round-tripped so
  no field changes, but it is a genuine cross-tenant full-row-rewrite / lost-update primitive on
  4 tables. Preconditions are ordinary: one ACTIVE `SupplierAgreement` + a supplier item whose EAN
  also exists in a victim catalog. No id guessing — the attacker's own earlier API response supplies
  the target id.
- **(iii) F5 — cross-tenant Claude API-key consumption** on `POST /api/marketplace/ai-recommend`:
  `SupplierAdvisor.ResolveAsync` reads `integration_configs` with no `TenantId` filter and no
  `ORDER BY` *after* the leak has started (`SearchSuppliersAsync` at `:178`), so its
  `FirstOrDefaultAsync` can return **another tenant's** Claude `api_key`, which is then spent on a
  live outbound Anthropic call — billing/quota abuse, and secret material crosses a tenant boundary
  in-process. **Resolved by this fix** (Part A removes the leak that enables it) — recorded here so
  it is not later re-derived as a separate open issue.
- **(iv) C1 — `MP-{yyyy}-{NNN}` order numbers** were only sequential-per-supplier because the leaked
  `provider` role satisfied `marketplace_orders.provider_bypass` in `NextOrderNumberAsync`'s count;
  a customer-visible identifier scheme was unknowingly resting on the leak. Found and fixed during
  post-impl review (TASK-645) — the count now runs inside
  `ITenantSessionOverride.ExecuteAsync(supplierTenantId, …)`. No unique index on `OrderNumber`, so
  removing the leak without C1 would have silently produced duplicate order numbers across two
  clients of one supplier.
Affected surface: ~13 `MarketplaceRepository` provider-bypass methods reachable from ~27 endpoints —
see the endpoint table in
`.claude/logs/tasks/641_2026-08-30_marketplace-provider-rls-pre-review_security-reviewer.md` §2
rather than reproduced here. Only two downstream writes legitimately needed the bypass (W1 —
`supplier_metrics` rating recalc; W2 — `supplier_reviews` reply); every other downstream
`SaveChangesAsync` is own-tenant or sits on an OR-based `tenant_isolation` policy and keeps working.
Why the test suite missed it: the unit tests mock `IItemRepository`, so real RLS is never exercised.
The existing `CreateOrder_LinkAction_LinkedItemNotOwnedByTenant_ReturnsError` test even encoded the
disproved assumption in its own comment ("ambient RLS on GetByIdAsync resolves a foreign-tenant id
to null") and stubbed `GetByIdAsync → null` — it was rewritten, not extended. Same
"mocked-repository-hides-a-real-RLS-interaction-bug" shape as KI-030.
Fix (ADR-035): new `IProviderRlsOverride` primitive — `SET LOCAL app.role='provider'` inside a short
explicit transaction, auto-revert on commit/rollback/exception; `MarketplaceRepository`-only,
enforced by `ProviderRlsOverrideContainmentTests` (scans Application + Infrastructure + Api).
`SetProviderRoleAsync` and its `GetDbConnection()`/`OpenAsync()` deleted; 12 bypass reads wrapped in
one `ExecuteAsync` block each, the 13th (`GetReviewByIdAsync`) deleted as dead code; W1/W2 became
composite read+write repo methods `UpsertMetricsRatingAsync`/`SetReviewReplyAsync` so the write runs
inside the bypass transaction; Part B application-level JWT-derived `clientTenantId` filters at 3
`MarketplaceOrderService` sites + write-time re-validation before `_items.Update`; C1 order-number
fix. `'provider'` kept (not replaced with a dedicated sentinel) — a reasoned departure from ADR-028,
recorded there as deferred hardening; `provider_bypass` was on **107 tables measured 2026-08-30 and
growing with every new RLS table** (109 a day later), so `'provider'` is a whole-schema read+write
bypass, narrowed here only in *duration*.
Verification chain: TASK-641 (pre-impl threat model, opus — F1/F2 confirmed from source, 27-endpoint
sweep, "keep `'provider'`" ratified, R1–R7). TASK-642 (database-engineer — prod `items.tenant_isolation`
already fail-closed since the 2026-07-16 audit deploy, `database-schema.md:108` was stale, no
migration; F1 confirmed live on prod). TASK-643 + 643b (backend-developer, opus — implementation +
C1/C2 remediation; Release build 0 errors / 1 pre-existing warning, no EF1002). TASK-644 (qa-tester —
2 new real-Postgres RLS integration files, 10 facts; **proved the leak pre-fix** via a
targeted-pathspec `git stash` of the TASK-643 diff, verbatim recorded failure:
```
Assert.Empty() Failure: Collection was not empty
Collection: [MarketplaceOrderConflictDto { SupplierItemId = 70691801-…, ExistingItem =
  MarketplaceOrderConflictingItemDto { Id = dc436a5b-dbfb-4451-95c7-763f4feb2486,
  Name = Чужий товар (третій тенант), ImageUrl = https://example.test/foreign.jpg,
  Barcodes = System.Collections.Generic.List`1[System.String] } }]
 …
 app.role AFTER the call (never reset pre-fix) = 'provider'
```
). TASK-645 (independent post-impl review, opus — 12/12 security criteria pass; found C1 + C2; after
remediation, final verdict **SHIP**). Full suite **2037/2037 passed, 0 skipped**. Full detail:
`.claude/logs/tasks/641..645_2026-08-30_marketplace-provider-rls-*.md`, ADR-035
(`.claude/docs/decisions.md`).
Cross-references: **KI-028** (the risk class this realizes — see its forward note), **KI-030** (same
mocked-repository test blind spot at the DB/RLS boundary).

### KI-035: Postgres connection-pool exhaustion (`53300: too many clients already`) in `ShelfGuard.Tests` integration suite — scattered failures across unrelated feature test classes ✅ resolved (2026-08-29, TASK-639)
Severity: medium (blocked CI's Test step / delayed deploy when it hit, but deploy was correctly
skipped rather than shipping bad code — the flakiness itself never corrupted data)
Status: ✅ resolved (2026-08-29, TASK-639) — found 2026-08-26 while pushing the
`AddConfigurableLoyaltyTierProgression` migration; root-caused and fixed in a dedicated
investigation.
Description: `backend-ci`'s Test step failed with `Npgsql.PostgresException: 53300: sorry, too many
clients already` across ~14-19 integration test classes spanning Loyalty, PriceSegments,
AudienceBuilder, MobileConfig, SupplierAgreement — no common feature, so not caused by any one
change. Reproduced exactly against a fresh empty `postgres:16-alpine` container with all migrations
applied by the real `dotnet ef` tool, confirming pre-existing test-infrastructure flakiness rather
than a migration or feature-correctness issue. Ruled out early and correctly: xUnit
test-collection parallelism. An `xunit.runner.json` with `parallelizeTestCollections: false` (fully
serialized) produced ~39-41 failures — worse and 2× slower; that change was reverted and must not be
reintroduced. This corrected at least 2 prior task logs (`.claude/logs/tasks/630_...md`,
`632_...md`) that called it "known flaky pool exhaustion" and blamed scheduling.
Root cause: a genuine, cumulative `NpgsqlDataSource` leak in the test fixtures — not a scheduling
artifact, which is exactly why serialization made it worse instead of better. Every
`NpgsqlDataSource` owns its **own** connection pool, and that pool's physical Postgres backends stay
open until the data source itself is disposed (or until the 300 s default `ConnectionIdleLifetime`
elapses — longer than a whole suite run). Fifteen integration-test classes built
`new NpgsqlDataSourceBuilder(cs).EnableDynamicJson().Build()` and **never disposed it**:
- 10 cached it in an *instance* field (`private DbContextOptions<AppDbContext>? _options` +
  `_options ??= ...`). xUnit constructs a fresh class instance **per `[Fact]`**, so this was one
  undisposed pool per TEST, not per class — the comments in those files claiming a per-class cache
  were wrong.
- 1 (`MarketingAnalyticsRepositoryIntegrationTests`) cached it in a `static` field — one pool, still
  never disposed.
- 4 (`LoyaltyRepositoryIntegrationTests`, `MobileConfigPublishConcurrencyIntegrationTests`,
  `Pos/PosConcurrencySalesIntegrationTests`, `Pos/LoyaltyConcurrencySalesIntegrationTests`) rebuilt
  a brand-new data source inside `NewContext()` on **every call**.
Summed over a full run that is ~100 stranded backends against a server whose `max_connections` is
100 — hence failures scattered across whichever unrelated tests happened to run once the budget ran
out, and hence total immunity to how many tests run concurrently. The 10 RLS classes tagged
`[Collection("TENANT_ISOLATION_TESTS")]` were **not** part of the leak: they store `_dataSource` in
a field and dispose it in `DisposeAsync()`, and their `InitializeAsync` cannot throw between
building and assigning it. They were victims, not causes — several of the observed failures were in
those classes.
Fix: `backend/ShelfGuard.Tests/Infrastructure/TestPostgres.cs` (new) — ONE process-wide pooled
`NpgsqlDataSource` per distinct connection string (`Lazy<T>` with
`LazyThreadSafetyMode.ExecutionAndPublication`, so a race cannot build and then silently discard a
second pool), `MaxPoolSize = 40`, `EnableDynamicJson()`, disposed on `ProcessExit`; plus the single
shared `DbContextOptions<AppDbContext>` built on it and a `NewContext(connectionString)` helper. All
15 leaking classes now have a one-line `private AppDbContext NewContext() =>
TestPostgres.NewContext(_connectionString);` and their `_options` fields are gone. A pool is
designed to be shared: it grows only to actual concurrent demand and recycles connections instead of
stranding one per test. Sharing one `DbContextOptions` also means the assembly now creates exactly
one EF internal service provider for these tests, structurally removing the cumulative
`ManyServiceProvidersCreatedWarning` pressure that `TestDbContextOptionsExtensions
.IgnoreManyServiceProvidersWarning()` had been papering over since 2026-08-19 (the helper is kept as
a guard for the RLS classes that still build their own data sources). The RLS classes were left
untouched: their private per-test pool is deliberate `SET ROLE` / session-GUC isolation.
Verification: `dotnet build` clean (same single pre-existing CS8602 warning in
`Marketplace/MarketplaceServiceTests.cs`, no new ones). Baseline on a fresh `postgres:16-alpine`
container with migrations applied by `dotnet ef database update`: **1999 tests, 1983 passed, 16
failed, 32 `53300` occurrences** (PriceSegments, AudienceBuilder ×5, MobileConfigDraftService ×2,
MobileConfigPublishedRead ×4, MobileTheme ×2, SupplierAgreementMarkSigned ×2). After the fix, three
consecutive runs, each against a **newly created and freshly migrated** container:
**1999/1999 passed, 0 failed, 0 `53300`** in all three. Test count unchanged (1999 → 1999), no
soft-skips (`grep "DB not available"` → 0), and the previously-failing integration tests show real
execution times (e.g. `PosConcurrencySalesIntegrationTests` 2 s). Peak concurrent backends sampled
during a run: **24** (was pinned at the 100 ceiling before).

### KI-033: `pos_transactions` `store_scope` RLS policy silently corrupts marketing-analytics results (store-migration + RFM overview) for store_manager/network_manager ✅ resolved (2026-08-11, TASK-508..511)
Severity: high (silent wrong data, not a crash/403 — the policy behaves exactly as designed for
most RLS-scoped queries in this app, but marketing-analytics' whole premise is a tenant-wide
comparison, so scoping it produces confidently wrong business math with no partial-data signal
anywhere in the response)
Status: ✅ resolved (2026-08-11) — found 2026-08-10 (TASK-504, QA of the store-migration
feature, TASK-501..503); fixed via TASK-508 (design, ADR-028) → TASK-509 (implementation) →
TASK-510 (security review, clean) → TASK-511 (independent QA re-verification, byte-identical
results for the originally-affected account).
Description: Tenant `8abfbbb5-3190-4de9-9f91-f4de59101bca` ("Свіжий Кут"), 4 locations.
`manager@demo.local` (store_manager) has `user_locations` grants for only 2 of them (the tenant's
original 2). Calling `GET /api/marketing-analytics/store-migration` (period=6m, no store filter)
as `ea@demo.local` (enterprise_admin, RLS-exempt) returns 3 flows, `migratedCustomerCount: 3`,
matching raw-SQL ground truth exactly. The same call as `manager@demo.local` (store_manager,
scoped to 2/4 locations) returns only 2 flows, `migratedCustomerCount: 2` — the
"Троєщина→Подільський" flow (customer "Loyal One") doesn't just disappear, it gets
**reclassified**: that customer's true earliest transaction was at a store `manager@demo.local`
isn't granted, so their *visible* earliest transaction shifts to a store that's also their latest
→ looks like "not migrated" when they truly migrated. The remaining visible flow
("Центральний→Подільський", customer "Champion Two") has its revenue/receipt count silently
undercounted (3004.25/21 receipts vs. the true 3124.25/22 — exactly the one transaction at the
ungranted location). No indication anywhere in the response that the data is partial. Confirmed
the aggregation logic itself is correct: after granting `manager@demo.local` the 2 missing
`user_locations` rows (SQL only, no code change), the store_manager's response became
byte-identical to enterprise_admin's. Also reproduces on the pre-existing RFM overview endpoint
(`GET /api/marketing-analytics/overview`, shipped TASK-406/409): store_manager's `periodRevenue`
was understated by exactly the transactions at the 2 ungranted locations — so this is debt the
whole `MarketingAnalyticsController` already had; store-migration is just the first place where
the consequence is an outright wrong classification instead of "just" a smaller total. Full repro
and evidence: `.claude/logs/tasks/504_2026-08-10_store-migration-qa_qa-tester.md`.
Root cause: `pos_transactions`' RESTRICTIVE `store_scope` policy (migration
`20260719193545_AddLocationStoreScopeRlsPolicies.cs`, TASK-393 decision) only admits rows whose
`LocationId` is in the caller's `user_locations`, unless the caller's role is
`provider`/`provider_admin`/`worker`/`enterprise_admin`. `network_manager` and `store_manager` are
NOT in that bypass list. The new store-migration repository methods
(`MarketingAnalyticsRepository.GetStoreMigrationFlowsAsync`/`GetStoreMigrationCustomersAsync`) run
through the caller's own RLS session like any other query on this connection, so they inherit this
scoping. Not the same issue as KI-031: KI-031 is `netmgr@demo.local` having **zero**
`user_locations` grants (a seed-data completeness gap for an under-seeded demo account). This is
different and more serious — it reproduces for any *normally provisioned* store_manager scoped to
their real subset of stores (the expected shape of that role), and for store-migration it's not
just undercounting, it silently changes a customer's migration classification. The frontend
already treats store_manager as a fully trusted user of this exact feature
(`canExportMarketingAnalyticsPii` lets store_manager+ export unmasked PII from it), so shipping
this as-is means the most commonly deployed privileged role for marketing-analytics gets
confidently wrong analytics with no error and no "partial data" signal.
Resolution (applied 2026-08-11): implemented option (a) from the original write-up above — the
marketing-analytics repository queries now run under a bypass role for this specific read path —
via a new dedicated RLS bypass role-value, `'marketing_analytics_bypass'`, added to
`pos_transactions`' `store_scope` policy IN-list (migration
`20260811110212_AddMarketingAnalyticsBypassToPosTransactionsStoreScope.cs`), activated only from
inside `MarketingAnalyticsRepository` via a new `IAnalyticsRlsOverride` primitive (`SET LOCAL
app.role = 'marketing_analytics_bypass'` inside a short-lived explicit transaction, one per
repository method call, mirroring `ITenantSessionOverride`). Full design reasoning — including why
a dedicated role value was chosen over reusing `enterprise_admin`, and why the override lives at
the repository layer rather than per service call site — is in **ADR-028**
(`.claude/docs/decisions.md`), not repeated here.

**Important nuance:** the fix is not conditioned on the caller's specific role — it applies
uniformly to every caller who passes `MarketingAnalyticsController`'s existing
`[Authorize(Policy = MarketingAnalyticsViewOrCapability)]` + `[RequireModule("marketing_analytics")]`
gates, because the trust boundary was already established once at the controller, not re-decided
per role inside the repository. One accurate, useful side effect of this: `network_manager`
accounts (the ones affected by the separate, still-open **KI-031** — zero `user_locations` grants)
now also get full, correct marketing-analytics data as an incidental consequence — TASK-511
live-confirmed `netmgr@demo.local` went from 0 rows to byte-identical-with-`ea@demo.local` on every
marketing-analytics endpoint. This is **not** "KI-033 fixed KI-031" — they are separate issues.
KI-031 itself (network_manager getting zero data tenant-wide on every *other* RLS-scoped module,
e.g. `/api/stock`) remains open and unaffected outside marketing-analytics; marketing-analytics
specifically is simply no longer affected by KI-031's symptom either, as a bonus of this fix.

Verification chain: TASK-509 (backend-developer) implemented the migration + `IAnalyticsRlsOverride`/
`AnalyticsRlsOverride`, wrapped all 13 `MarketingAnalyticsRepository` methods — `dotnet build` clean,
`dotnet test` 1400/1400. TASK-510 (security-reviewer) independently re-derived every claim from
source (blast radius across all 36 migrations referencing `app.role`, reachability, transaction-
scoping/rollback guarantee, call-site containment, controller trust-boundary, `tenant_isolation`
independence) — verdict **SHIP**, 0 blocking findings. TASK-511 (qa-tester) independently re-ran the
original TASK-504 repro against the real under-scoped `manager@demo.local` state (found and
corrected a setup drift first — `user_locations` had not actually been restored to the true 2/4
state, see task log) plus the drill-down/export endpoints and a live UI check: byte-identical to
`ea@demo.local` on `/store-migration` (6m/3m), `/overview`, `/store-migration/customers`, the
OR-semantics filter, and both masked/unmasked exports; cross-tenant isolation independently
re-verified at the Postgres level (not just cited from TASK-510); full regression pass (`dotnet test`
1400/1400, `tsc --noEmit` clean, 40–84ms latency, no concern). Full detail:
`.claude/logs/tasks/508_2026-08-10_ki033-fix-design_project-architect.md`,
`509_2026-08-11_ki033-fix-implementation_backend-developer.md`,
`510_2026-08-11_ki033-fix-security-review_security-reviewer.md`,
`511_2026-08-11_ki033-reverify_qa-tester.md`.

## Resolved Issues

### KI-012: Existing tenants have stale legacy module keys, not v4 module keys ✅ resolved (2026-06-16)
Resolution: TASK-210 added migration `V4ModulesBackfill` — a one-time, idempotent data migration that sets `Modules` to `Tenant.DefaultModulesForBusinessType(tenant.BusinessType)` for any tenant whose `Modules` doesn't already contain at least one v4 key. Applied locally; verified the demo tenant went from `["shelf_manager","crm","notifications"]` to `["inventory","procurement","pos"]`. Sidebar (TASK-210) now gates the Operations/Sales/Procurement groups on these keys via `useModules()`.

### KI-001: Backend uses CRM.* project names ✅ resolved (2026-06-03)
Resolution: All backend projects renamed to ShelfGuard.* as part of initial setup.

### KI-002: No authentication implemented ✅ resolved (2026-06-03)
Resolution: Full JWT auth with refresh tokens implemented in TASK-003 (AddAuth migration + AuthService + AuthController).

### KI-003: Full v1 schema not yet migrated ✅ resolved (2026-06-04)
Resolution: TASK-002 completed — 19 new tables, RLS on all tenant tables, FEFO index applied via FullSchema migration.


### KI-004: Duplicate `apiFetch` in feature API modules ✅ resolved (2026-07-15, confirmed during Block 13 pre-launch audit)
Resolution: Verified both `features/inventory/api/products.ts` and `features/dashboard/api/dashboard.ts` already `import { api } from "@/lib/api"` — no local `apiFetch` remains anywhere in `frontend/` (`grep -rn "function apiFetch\|const apiFetch"` matches only `lib/api.ts` itself). Not clear from git history exactly which prior task fixed this (products.ts/dashboard.ts have both been touched by several since KI-004 was filed); no code change was needed here, doc was just stale.

### KI-024: Every role-based UI gate in the mobile app used non-existent PascalCase role names ✅ resolved (2026-07-15, Block 14 mobile audit)
Severity: was critical
Description: real backend role strings are lowercase snake_case (`enterprise_admin`,
`network_manager`, `store_manager`, `merchandiser`, `storekeeper`, `cashier`, `provider`,
`provider_admin`, ...) per `UserService.ValidRoles` / `AppPolicies.cs` / web's
`frontend/lib/roles.ts`. Nine mobile screens independently declared PascalCase role arrays that
matched no real role string at all — e.g. `CASHIER_ROLES = ['Cashier', 'StoreManager',
'Director', 'Admin']`, `MANAGER_ROLES = ['StoreManager', 'Director', 'NetworkManager', 'Admin']`
('Director'/'Admin' aren't even real roles). Every `.includes(user.role)` check built from these
always evaluated `false` for real accounts: the POS tab was invisible to real cashiers
(`(app)/_layout.tsx`), and write-off/customer/transfer manager approve-reject actions, plus the
dashboard's manager summary, never appeared for real store/network managers. One file
(`features/dashboard/types.ts`) already had the correct lowercase strings, confirming this was
an accumulated per-screen mistake rather than an intentional convention.
Resolution: added `mobile/lib/roles.ts` (mirrors `frontend/lib/roles.ts` as the mobile app's
single source of truth) exporting `AppRoles`, `CAN_ACCESS_POS`, `AT_LEAST_STORE_MANAGER`,
`AT_LEAST_STORE_MANAGER_OR_PROVIDER`, and a `hasRole()` helper. Every ad hoc array in
`(app)/_layout.tsx`, `index.tsx` (dashboard), `write-offs/[id].tsx`, `customers/index.tsx`,
`customers/[id].tsx`, `transfers/[id].tsx`, `schedules/index.tsx`, `service-desk/index.tsx`,
`service-desk/[id].tsx` now imports from it. `npx tsc --noEmit` clean.

### KI-025: Mobile `user.locationId` was never populated + write-offs/transfers/stock location filters used the wrong query-param name ✅ resolved (2026-07-15, Block 14 mobile audit)
Severity: was critical
Description: two compounding wire-contract mismatches. (1) Backend's `AuthUserDto` still names
the assigned-store field `StoreId` (never renamed in the v4 Store→Location pass) → JSON key
`storeId`; mobile's `AuthUser` type expected `locationId` and `authApi.ts` returned the raw
response with no mapping, so `user.locationId` was `undefined` for every logged-in user. This
unconditionally blocked write-off creation (`write-offs/create.tsx`'s
`if (!user?.locationId) { ...return; }` guard always fired), transfer creation, production
order creation, and made the "incoming transfer confirm" button permanently invisible.
(2) Separately, `WriteOffsController`/`TransfersController`/`StockController`'s GET-list query
param is `store_id` (snake_case); mobile sent `location_id` (write-offs/transfers) or
`locationId` (stock) — none matching — so even with `user.locationId` populated, those three
lists would have stayed unfiltered across all locations. (`SchedulesController`/
`ProductionController` already use `locationId` camelCase — mobile already matched, no bug
there.)
Resolution: `authApi.ts` now maps wire `storeId` → `AuthUser.locationId` at the API boundary
(`mapAuthUser()`, used by both `login()` and `getMe()`); `writeOffApi.ts`/`transferApi.ts`/
`stockApi.ts` now send `store_id` on the wire while keeping their external `locationId`
parameter name (no call-site changes needed). Confirmed the real backend param names by reading
the five controllers directly rather than guessing. `npx tsc --noEmit` clean.

### KI-026: Mobile `user` was never restored after a cold app restart ✅ resolved (2026-07-15, Block 14 mobile audit)
Severity: was high
Description: `useAuthStore.loadToken()` (called once on boot in `app/_layout.tsx`) only
restored `accessToken` from `expo-secure-store` — `user` stayed `null` until the next real
login. Since nearly every role-gated screen reads `user` from the store (the KI-024 manager
gates, the POS tab, the KI-025 location checks), a cold restart (device reboot, OS memory
eviction, force-quit — all routine on mobile) with a still-valid token silently broke every
role-gated UI element until the user logged out and back in. `getMe()` (`GET /auth/me`) already
existed in `authApi.ts` but was dead code, never called anywhere.
Resolution: `app/_layout.tsx`'s boot effect now calls `getMe()` after `loadToken()` when a
token is present but `user` is still null, populating the store via a new `setUser()` action;
falls back to `clearAuth()` (clean redirect to login) if the token turns out to be
expired/invalid. `npx tsc --noEmit` clean.

### KI-037: Mobile supplier-metrics tiles rendered 0–1 fraction fields as `Math.round(x)%` → always showed 0% ✅ resolved (2026-08-31, TASK-660)
Severity: low (cosmetic; B2B marketplace supplier profile screen only)
Description: `mobile/app/(app)/marketplace/[id].tsx` rendered `orderAccuracy` and `qualityScore`
(both 0–1 fractions from `SupplierMetricsDto`, `decimal?`) as `Math.round(x)` with a `%` suffix,
so e.g. `0.87` displayed as `0%`. The web `SupplierMetrics.tsx` already scaled by ×100.
Resolution: TASK-660 changed both to `Math.round(x * 100)` with `%`. `qualityScore` has no
backend data source and is always null, so its tile renders `—`. The per-region breakdown and
`Час відповіді` tile added in the same task use correct scaling from the start.
