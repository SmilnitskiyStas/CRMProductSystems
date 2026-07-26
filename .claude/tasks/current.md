# Current Sprint — v4.5 «Security Hardening» (started 2026-07-09)

Джерело: security audit `.claude/logs/reviews/2026-07-09_security-audit_auth-infra.md`
(TASK-329..332). Паралельні власники: TASK-331 — frontend, TASK-332 — devops.

## TASK-417 — Backend: fix CRITICAL RLS break in consumer loyalty join flow
**Status:** done — fixed and verified live against real Postgres RLS, no blocker · **Agent:**
backend-developer · **Depends:** TASK-416 (QA repro + root cause) · **Next:** security-reviewer
should sanity-check the new `ITenantSessionOverride` primitive before wider release (not urgent,
narrow usage today)
Log: `.claude/logs/tasks/417_2026-07-26_fix-consumer-join-rls_backend-developer.md`
Fixed the 100%-reproducible `POST /api/consumer/loyalty/{tenantId}/join` 500 QA found: a consumer
session never carries `app.tenant_id` (cross-tenant by design), and `customers` only has the
canonical `tenant_isolation`/`provider_bypass`/`worker_bypass` RLS triad — no identity-based policy
the way `loyalty_memberships`/`loyalty_ledger_entries` got in TASK-404 — so every lookup silently
returned 0 rows and every create-fallback INSERT was rejected by RLS. Confirmed via the migration's
own policy text that `LoyaltyMembership`'s insert was never actually broken (`consumer_self_access`
covers it independent of `tenant_id`) — only the `customers` step was. Rejected adding a new
identity-based policy to `customers` (no natural `ConsumerAccountId` column, shared with
staff-created customers). Fix: new `ITenantSessionOverride`/`TenantSessionOverride`
(`Application/Services` + `Infrastructure/Services`) — `ExecuteAsync<T>(tenantId, action, ct)` opens
an explicit transaction, `SET LOCAL app.tenant_id = ...`, runs `action`, commits; Postgres
auto-reverts `SET LOCAL` at transaction end (commit or rollback), so it can never leak to a later
query on the same pooled connection, even on an unhandled exception — no manual restore step to
forget. `LoyaltyService.JoinAsync`'s customer-lookup-or-create + membership-create branch now runs
inside it (atomic as a side benefit); the idempotent existing-membership branch is untouched (needs
no override); `JoinAsStaffAsync` untouched and confirmed unaffected (staff sessions already carry
the correct `tenant_id` claim). New live-Postgres `LoyaltyJoinRlsIntegrationTests.cs` — real repos
(not mocks), throwaway NOSUPERUSER NOBYPASSRLS role, exact consumer-session GUC shape: new-join
happy path (+ confirms the SET LOCAL override doesn't leak past its own transaction), idempotent
second call (no duplicate rows), second-tenant join stays isolated (a tenant-A-scoped staff read
relying purely on RLS sees exactly 1 customer) and the cross-tenant wallet read
(`GetMembershipsForConsumerAsync`, guarded solely by `consumer_self_access`, untouched by this fix)
still correctly returns both memberships — exactly the live-RLS coverage QA flagged as missing.
Updated `LoyaltyServiceTests.cs`'s mocks (new `ITenantSessionOverride` pass-through) so every
pre-existing `JoinAsync` test still passes unchanged, plus one new mock-level regression pinning the
override is actually invoked with the right `tenantId`. **Test-infra side effect, fixed in scope:**
the new integration test file (a 4th fresh-`NpgsqlDataSource`-per-call Postgres test class) tipped
EF Core's process-wide `ManyServiceProvidersCreatedWarning`-as-error threshold over the edge,
intermittently failing 2 unrelated pre-existing tests (`PosConcurrencySalesIntegrationTests`,
`LoyaltyConcurrencySalesIntegrationTests` — neither had the defensive `.ConfigureWarnings(...)`
downgrade `LoyaltyRepositoryIntegrationTests`/`MarketingAnalyticsRepositoryIntegrationTests` already
carry for the identical reason); fixed by sharing one data source per test method in the new file and
adding the same one-line downgrade precedent to those two files (test-infra hygiene only, zero
behavior change to what either asserts). `dotnet build` 0 err (1 pre-existing unrelated warning),
`dotnet test` full suite run **3× consecutively, 1109/1109 green each time** (was 1105; +4), new
integration tests independently re-verified in isolation too (real DB round-trips, 3-10s each, not
silent soft-skips). Not committed.

## TASK-414 — Backend: security remediation of 3 findings from TASK-412 (Loyalty + Marketing Analytics)
**Status:** done — **all 3 fixed and verified, no blocker** · **Agent:** backend-developer ·
**Depends:** TASK-412 · **Next:** re-review by security-reviewer before release (recommended, not
re-run this session), then frontend/mobile/qa as originally queued behind TASK-412
Log: `.claude/logs/tasks/414_2026-07-26_security-fixes-loyalty-rfm_backend-developer.md`
Fixed exactly the 3 assigned findings, nothing else. **(1) CRITICAL Excel/CSV formula
injection:** `ExcelExportService.SetCellValue` now routes every string (headers, truncation
banner, every row value) through one centralized `SanitizeForSpreadsheet` helper — leading
`=`/`+`/`-`/`@`/Tab/CR gets apostrophe-prefixed. Empirically verified via a throwaway ClosedXML
0.105.1 probe (not assumed) that ClosedXML implements the real OOXML "quote prefix" convention —
it strips the apostrophe and sets `cell.Style.IncludeQuotePrefix` (`quotePrefix="1"` in
styles.xml) rather than keeping a literal `'` in the stored text, same as real Excel's own
manual-quote behavior; tests assert on that style flag. New
`ExcelExportServiceTests.cs` (9 tests, real ClosedXML round-trip of actual output, not mocks).
**(2) HIGH LoyaltyMembership.Balance TOCTOU:** added `xmin`/`IsRowVersion()` to
`LoyaltyMembership` (same pattern as `ProductStock`/TASK-356); new no-op EF migration
`AddLoyaltyMembershipConcurrencyToken` (xmin is a reserved system column, already exists —
applied cleanly to dev DB, no backfill needed); `LoyaltyRepository.SaveChangesAsync` now
translates `DbUpdateConcurrencyException` → `ConcurrencyConflictException` (mirrors
`PosRepository`); `LoyaltyService.ManualAdjustAsync` catches it → clean 409;
`PosService.CreateSaleAsync`'s existing catch (same shared `SaveChangesAsync`) needed only a
comment/message update, not new logic. New real-Postgres
`LoyaltyConcurrencySalesIntegrationTests.cs`: two concurrent redemptions (40 each) off a
shared membership starting at 100, deterministic rendezvous (not timing luck) — confirmed
exactly 1 success + 1 clean 409, final balance exactly 60 (not 100 lost-update, not 20
double-applied). **(3) Dead `marketing_analytics.export_pii` capability + unmasked email:**
root cause confirmed — controller's class-level `CanViewAnalytics` floor was the *identical*
role set as `CanExportPii`'s own first branch (unlike `LegalEntityAuthorization`, where the
floor is strictly looser), so nobody outside store_manager+ could ever reach the capability
check. Applied the exact `AnalyticsController`/`AnalyticsViewOrCapability` ADR-020 precedent:
new `TenantRoleCapabilities.MarketingAnalyticsView` + `AppPolicies.
MarketingAnalyticsViewOrCapability`, swapped onto the controller's class-level attribute — zero
behavior change for existing roles, but a granted capability holder below store_manager can now
actually reach the export endpoints. Email now masked by default in exports (new `MaskEmail`
helper), same posture phone already had. `dotnet build` 0 err (1 pre-existing unrelated
warning), `dotnet test` **1105/1105 green** (full suite, including all pre-existing
`PosServiceTests`/`MarketingAnalyticsServiceTests`/TASK-406 Excel tests, no regressions; 2 new
live-Postgres integration tests each re-run once more to rule out flakiness). Did not touch
anything outside the 3 findings (#4 consumer JWT revocation, RLS `FOR` clause narrowing,
rls_audit_test_role gap, etc. remain open per TASK-412, out of this task's scope). Not
committed (repo convention — main session/user commits).

## TASK-413 — Frontend: wire "loyalty"/"marketing_analytics" into provider + admin module lists
**Status:** done · **Agent:** frontend-developer · **Depends:** TASK-409 (flagged this as a
follow-up chip) · **Next:** none identified beyond the further follow-up flagged below
Log: `.claude/logs/tasks/413_2026-07-26_provider-admin-module-list_frontend-developer.md`
`frontend/features/provider/types.ts` and `frontend/features/admin/types.ts` each own a separate
`ALL_MODULES` list driving the provider/admin panel's tenant module-activation checkboxes; neither
had `"loyalty"` (TASK-405) or `"marketing_analytics"` (TASK-406), so a provider had no UI path to
activate either module for a tenant — only a direct DB write (what TASK-409 itself had to do to
test). Added both keys to provider's `TenantModule` union + `ALL_MODULES` (used by
`TenantDetailPanel.tsx`'s edit checklist and `CreateTenantWizard.tsx` step 3) and to admin's own
`ALL_MODULES` (`TenantDetailDrawer.tsx`; admin has no create-time module picker). Added i18n
labels/descriptions to both `en.json`/`uk.json` (`Dashboard.provider.modules`/`moduleDescriptions`,
`Dashboard.admin.modules`), reusing TASK-409's English copy for `marketing_analytics` and writing
new copy for `loyalty`. **Found + fixed an unrelated bug that was blocking verification**: the
admin panel's module/plan "Save" 405'd — `admin.ts`'s `updatePlan`/`updateModules` called `api.put`
but `AdminController` declares both `[HttpPatch]` (its own doc-comments already said "PATCH"); every
admin-panel plan/module change has silently 405'd since TASK-074, not specific to these two new
modules. Fixed both call sites to `api.patch`. Live-verified both panels end-to-end (provider
`PUT → 204`, admin `PATCH → 200`, both persisted after hard-reload and cleanly reverted, both
locales render correctly). `tsc`/`build` clean. Deliberately did NOT touch
`frontend/features/modules/types.ts` (`ALL_MODULE_KEYS`) — the tenant-facing **read-only** Settings
"Modules" tab already has `marketing_analytics` (TASK-409) but still lacks `loyalty`; flagged as a
separate follow-up via `spawn_task` (chip `task_cc5b2371`) rather than folded into this task's
narrower provider/admin scope. Dismissed the now-superseded chip `task_22a39ac1`. Not committed.

## TASK-412 — Security: review of Loyalty + Marketing Analytics (Фаза 0 + Фаза 1)
**Status:** done — **verdict: NOT clear to ship as-is** · **Agent:** security-reviewer ·
**Depends:** TASK-404..411 · **Next:** backend-developer (fix blocker + high-priority item below),
then re-review before release
Log: `.claude/logs/tasks/412_2026-07-26_security-review-loyalty-rfm_security-reviewer.md`
Audited all 8 loyalty/RFM task logs (404-411) then the actual code directly (entities, migration
SQL, controllers, services, repos, `AppDbContext.cs`, `TenantConnectionInterceptor.cs`). Of the 9
items the brief called out: 6 verified **OK** (`ConsumerAccount` no-RLS — no generic GetById
exposure found anywhere; `consumer_self_access` RLS + JWT claim validation — fail-closed, correct,
minor hardening nit only; `TryClaimTimestepAsync` anti-replay — genuinely atomic + parameterized;
`MarketingAnalyticsRepository`'s raw SQL — all 9 methods fully parameterized, zero injection risk;
`FixLoyaltyTableGrants` migration — scoped to exactly 4 tables; `ConsumerAuthController` rate-limit/
lockout — present, consistent with TASK-329). 3 are real gaps: consumer JWT is a genuinely
unrevoked 30-day token in the actual `appsettings.json` config (not just a doc claim); the new
`marketing_analytics.export_pii` TenantRole capability is **dead code** — proven by direct
comparison with `LegalEntityAuthorization`'s own doc comment, which explicitly documents the exact
"class-level policy must be looser than the capability check" rule this codebase learned once
already (ADR-020 "the blocking discovery") and which `MarketingAnalyticsController` violates (its
class-level `CanViewAnalytics` floor is identical to, not looser than, `CanExportPii`'s role
branch); Excel export never masks email regardless of the PII flag. Also flagged (per brief's
"add anything else you find" instruction) the documented rls_audit_test_role test-blind-spot as
confirmed-real but acceptable to defer.
**2 new findings neither of the 8 building agents caught:** (A) **CRITICAL, blocks release** —
`ExcelExportService.SetCellValue` writes raw strings into cells with no Excel-formula
neutralization; since `Customer.Name` for a loyalty-joined customer comes verbatim from
self-registered `ConsumerAccount.FullName` (`POST /api/consumer-auth/register` is `[AllowAnonymous]`,
validates only non-empty), **any anonymous member of the public can plant a formula payload that
executes in a trusted store_manager's Excel** the moment they export a segment — full path traced
end-to-end, not speculative. Small, standard fix (prefix `=`/`+`/`-`/`@`-leading strings with `'`).
(B) **HIGH** — `LoyaltyMembership` has no optimistic-concurrency token anywhere in `AppDbContext.cs`
(confirmed absent), unlike `ProductStock`, which explicitly uses `xmin`/`IsRowVersion()` in the same
file for the identical bug class ("two cashiers selling the last unit at the same moment"). Both
`PosService.CreateSaleAsync`'s redemption/accrual and `LoyaltyService.ManualAdjustAsync` mutate
`Balance` via plain `SaveChangesAsync()` — concurrent sales against the same membership can each
pass the balance-sufficiency check against a stale read, and the loser's decrement is silently lost
(TOCTOU), letting a customer redeem more than their real balance. Requires staff-level POS access
(insider/race risk, not remote), but is a real money-integrity gap the same file already knows how
to fix on a sibling entity. Full verdict table + all recommendations in the log. No code changed
(audit only, per brief) — everything above needs a follow-up implementation task before wider
rollout.

## TASK-411 — DB: fix — 4 loyalty tables owned by migration superuser, zero app-role grants
**Status:** done — fixed and live-verified in dev; staging unaffected (migration hasn't reached
it yet); production cannot have this bug yet (nothing loyalty-related ever committed/deployed) ·
**Agent:** database-engineer · **Depends:** TASK-410 (found the bug live, spawned background task
`task_693b439c`) · **Next:** apply both `AddLoyaltyProgram` + `FixLoyaltyTableGrants` via a
superuser connection (not the automatic boot-time `MigrateAsync()`) whenever this reaches
staging/production — documented deploy risk, not yet executed in either environment
Log: `.claude/logs/tasks/411_2026-07-26_loyalty-db-grants-fix_database-engineer.md`
Root cause (reproduced live, not assumed): this codebase has no bootstrap script/
`ALTER DEFAULT PRIVILEGES` — every table's access for the real app role comes purely from table
**ownership**, established once by TASK-372/KI-027 and inherited automatically ever since because
migrations normally run through the app's own already-owning connection. TASK-404's
`AddLoyaltyProgram` broke this: its own task log says it was applied via the `crm` **superuser**
connection (routing around the documented FK-validation-under-RLS gotcha), leaving all 4 loyalty
tables owned by `crm` with **zero** grants to `shelfguard_app_dev` — exactly the `42501 permission
denied` TASK-410 hit live on `GET /api/pos/sales`. Fix: new migration `FixLoyaltyTableGrants`
(20260726154747) — a `DO $$ ... ALTER TABLE {each of the 4} OWNER TO %I` block resolving the
target role **dynamically** from whichever role currently owns `tenants`, not a hardcoded dev role
name (so the same migration is correct in staging/production too). Touches only these 4 tables;
`Down()` is an intentional no-op (reverting would silently reintroduce the bug). Verified live: real
app-role `psql` insert/select on all 4 tables now succeeds inside a rolled-back transaction (RLS
still correctly rejects a write with no `app.tenant_id` set — ownership fix didn't weaken RLS);
full live run through the actual API — `GET /api/pos/sales` (TASK-410's exact failing call) now
`200 OK`. `dotnet build` 0 err, `dotnet test` 1086/1086 unchanged (permissions-only fix, no new
behavior — see testing-gap note below). Staging: `AddLoyaltyProgram` hasn't reached it yet, so no
bug there today. Production: `git log --all | grep -i loyalty` is empty and `main`'s HEAD predates
TASK-404 entirely — cannot have this bug yet, independent of any live check (a direct SSH
confirmation attempt was blocked by the harness's own permission classifier, same as TASK-371/372,
not worked around). **Flagged, not fixed:** the live-Postgres RLS test suite
(`LoyaltyRlsIntegrationTests` etc.) connects as `rls_audit_test_role`, which has its own explicit
`GRANT ALL` independent of table ownership — this is why `dotnet test` stayed green through the
entire incident despite the real app connection being broken; recommend a follow-up live test
against the actual configured `DefaultConnection` asserting basic `SELECT` on every FORCE RLS
table. Also flagged: a `known-issues.md` KI-027/028 cross-reference addendum for this incident —
not added (out of this task's scope). Not committed.

## TASK-410 — Backend: SaleDto customer fields + loyalty ledger mapping on GetSalesForShiftAsync
**Status:** done (code+tests) — feature not visible live yet, blocked by an unrelated DB
permissions gap (see below) · **Agent:** backend-developer · **Depends:** TASK-408 (found the
gap), TASK-405 (loyalty ledger/customer fields it fills in) · **Next:** database-engineer
(spawned task `task_693b439c`, see below) before this is actually visible end-to-end
Log: `.claude/logs/tasks/410_2026-07-26_saledto-loyalty-fields_backend-developer.md`
Closed the two mapping gaps TASK-408 found: `SaleDto` gained `CustomerId`/`CustomerName`
(mapped in both `CreateSaleAsync` and `GetSalesForShiftAsync`, the latter via a new
`.Include(t => t.Customer)` on `PosRepository.GetTransactionsByShiftAsync`); `GetSalesForShiftAsync`
now also maps `LoyaltyAccrued/Redeemed/Balance` by batch-querying `LoyaltyLedgerEntry` via new
`ILoyaltyRepository.GetLedgerEntriesForTransactionsAsync` (PosTransaction has no
LoyaltyMembershipId of its own — the ledger is the only signal). `LoyaltyBalance` = the
chronologically-last ledger entry's `BalanceAfter` for that transaction — a per-sale historical
snapshot, not the membership's current live balance. `dotnet build` 0 err, `dotnet test`
1086/1086 (+3 new, was 1083). **Found a critical, unrelated pre-existing bug while live-verifying:**
`GET /api/pos/sales` 500s with Postgres `42501 permission denied for table
loyalty_ledger_entries` — all 4 loyalty tables from TASK-404 (`consumer_accounts`,
`loyalty_memberships`, `loyalty_ledger_entries`, `loyalty_program_settings`) are owned by the `crm`
migration superuser with zero grants to the actual app role (`shelfguard_app_dev`), unlike every
other RLS table in the codebase. This means the entire loyalty feature chain (TASK-404..408) is
non-functional through the real app connection in every environment provisioned the same way —
confirmed live in dev, staging/production not yet checked. Did not attempt a fix (DB
ownership/grants, out of scope for this task and this repo's own TASK-371/KI-027 precedent says
don't work around DB permission issues without a dedicated review) — flagged via a spawned
background task (`task_693b439c`) for database-engineer with full root-cause and repro steps.
Not committed.

## TASK-406 — Backend: Marketing analytics (RFM) engine + dashboard API (Фаза 1)
**Status:** done (2026-07-26) · **Agent:** backend-developer · **Depends:** TASK-405 (Task #2 of
the loyalty/RFM plan's agent sequence — Фаза 0's `PosTransaction.CustomerId` writing) · **Next:**
frontend-developer (TASK-409), security-reviewer (mandatory pass, esp. raw-SQL parametrization +
PII export gate), documentation-writer (glossary/api-contracts/ADR)
Log: `.claude/logs/tasks/406_2026-07-26_marketing-analytics-backend_backend-developer.md` (full
frontend API contract for TASK-409 lives there — read it instead of the C#).
Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Фаза 1". New
`Features/MarketingAnalytics/` (mirrors `Features/Analytics/`'s thin service→repository shape):
`RfmSegmentClassifier` (pure, 11 named-constant if-branches, plan's exact priority-table order;
caught+fixed a real bug while testing — "Lost" `>6 months` needed strict `<`, not `<=`),
`RecommendationTemplates` (one method per segment, live-KPI Ukrainian copy),
`MarketingAnalyticsRepository` (Infrastructure — **first raw-SQL in the codebase**,
`Database.SqlQueryRaw<T>` with positional `{n}` params for `NTILE(5)` R/F/M scoring + segment-
scoped top-products/affinity/basket/behavior/LTV; verified via 2 throwaway spikes against live
Postgres before writing the real file, then 8 real integration tests seeding real POS data —
caught a real EXTRACT(DAY FROM interval) pitfall (doesn't give total elapsed days across months)
before it shipped, replaced with a plain `date - date` subtraction), `MarketingAnalyticsService`
(classification/aggregation orchestration, PII masking, ActivityLog on export),
`ExcelExportService` (Infrastructure/Export, ClosedXML 0.105.1 MIT — not EPPlus), new
`MarketingAnalyticsController` (`CanViewAnalytics` floor + `[RequireModule("marketing_analytics")]`,
8 endpoints: overview/segment-detail/affinity/basket/explain/3×export). New
`TenantRoleCapabilities.MarketingAnalyticsExportPii` (ADR-020) + `MarketingAnalyticsAuthorization`
(imperative check, store_manager+ or the capability — mirrors `LegalEntityAuthorization`'s shape).
`ItemType="packaging"` added to `ItemService.IsValidItemType` (string field, no schema change) —
excluded from top-products/affinity/basket aggregation. **Test-infra note (flagged for review):**
adding this task's 3rd raw-Postgres integration-test class pushed the full suite's cumulative
distinct-`DbContextOptions` count past EF Core's `ManyServiceProvidersCreatedWarning`-as-error
threshold (~20, process-wide) — added one `.ConfigureWarnings(...)` line to the pre-existing
`LoyaltyRepositoryIntegrationTests.NewContext()` (test-infra only, zero behavior change to what
that test verifies) since `Features/Loyalty/` itself was out of scope, not that test helper.
`dotnet build` 0 err/0 warn (1 pre-existing unrelated warning), `dotnet test` **1083/1083 green**
(was 1004; +79: 39 classifier + 18 recommendation-template + 8 authorization + 6 service + 8 live-
Postgres repository integration, ran full suite twice to confirm no flakiness). Did not touch
`Features/Loyalty/`, `Features/ConsumerAuth/`, `PosService.cs`, Domain entities/DbContext/
migrations (beyond the packaging string value), or any frontend/mobile UI, per the task's explicit
scope boundaries. Not committed.

## TASK-407 — Mobile: consumer loyalty wallet + POS loyalty scan (Фаза 0, Task #3 mobile half)
**Status:** done · **Agent:** mobile-developer · **Depends:** TASK-405 · **Parallel with:**
TASK-408 (frontend half) · **Next:** security-reviewer (mandatory pass before release),
qa-tester (end-to-end scenario — no emulator/device in this environment, contract-level
verification only so far)
Log: `.claude/logs/tasks/407_2026-07-26_mobile-loyalty_mobile-developer.md`
Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Зміни в POS" → "Mobile", §"Роль
і навігація в мобільному застосунку". New `(consumer)` route group (Tabs: wallet/history/
account, no `index.tsx` of its own — both it and `(app)` carry no path prefix, so an index in
both would collide on `/`) reached via a new `(auth)/select-role.tsx` chooser +
`consumer-login.tsx`/`consumer-register.tsx`, wired to new `POST /api/consumer-auth/
register|login`. `useAuthStore` gained `sessionKind`/`consumerUser`/`setConsumerAuth` purely
additively — every existing staff call site (`setAuth`/`setUser`/`clearAuth`/`loadToken`)
kept its exact signature. Wallet screen polls `GET /consumer/loyalty/{tenantId}/code` every
22s gated on BOTH navigation focus (`useFocusEffect`) AND `AppState==='active'` — stricter
than the existing `useCurrentShift` pattern, deliberately, since this is a rotating security
code. POS gained `pos/loyalty.tsx` (scan/manual-code/customer-search) inserted between
`scanner.tsx` and `payment.tsx` (1-line pathname change in `scanner.tsx`, rest of that
already-audited file untouched); new shared `BarcodeCameraView` used only by the new screen.
**Found + fixed a real correctness gap while wiring `payment.tsx`**: the backend computes the
sale's actual owed total as `subtotal - redeemAmount` (before tax/change) — `payment.tsx` now
computes that same `netTotal` for the cash-sufficiency/change check instead of the raw cart
subtotal, or the cashier would demand more cash than the customer owes once bonuses are
redeemed; zero visible diff when redeemAmount is 0 (the normal case). Staff "join own
program" added to `profile/index.tsx` (`GET /loyalty/my-membership` 404→join button,
403→section hidden entirely — module not enabled for that tenant). New dependency
`react-native-qrcode-svg` + peer `react-native-svg` (SDK-56-compatible, via `npx expo
install`, no config-plugin/app.json change needed). **Plan-vs-actual deviations documented in
the log:** manual code entry needs the FULL `SGLOY1.{id}.{code}` string, not "6 digits" as the
plan said; `resolve-code` returns no redemption-cap field (that lives in enterprise_admin-only
settings), so the client only soft-caps to `min(balance, subtotal)` and trusts the server's
400 on an actual cap violation (already correctly surfaced by the existing generic error
handler); no backend endpoint exists to "browse" tenants with loyalty enabled, so the wallet's
"join a new program" is a minimal manual Tenant-ID entry, flagged as needing a better UX
later. `npx tsc --noEmit` clean across the whole mobile project (checked after every file
touched). `npm run lint` still fails on the pre-existing missing `eslint.config.js`
(TASK-366, not this task's regression). No test runner exists in mobile (no `"test"` script,
zero test files) and no emulator/device in this environment — verification was contract-level
(controllers/DTOs read directly, full param flow traced by hand) plus a clean `tsc`, not a
live run. Not committed.

## TASK-408 — Frontend: Web POS read-only "Лояльність" block (Фаза 0, Task #3 frontend half)
**Status:** done (UI correct but dormant — see backend follow-ups below) · **Agent:**
frontend-developer · **Depends:** TASK-405 · **Next:** mobile-developer (Task #3 mobile
half, parallel, separate task), a NEW backend task to close the two mapping gaps found here,
security-reviewer (already scheduled, unaffected by this task)
Log: `.claude/logs/tasks/408_2026-07-26_web-pos-loyalty-section_frontend-developer.md`
Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Зміни в POS" → "Web".
**Found the actual backend contract diverges from the plan brief** (verified by reading
`PosDtos.cs`/`PosService.cs` directly, then live end-to-end on the dev stack — opened a real
shift, created a real sale via the API, fetched it back): `SaleDto` has NO
`CustomerId`/`CustomerName` field at all on either endpoint (not "sometimes null" — genuinely
absent from the DTO, despite `PosTransaction.CustomerId` being persisted at
`PosService.cs:324`), so the customer-name-with-link part of the brief cannot be built without
a new backend DTO extension. Separately, `loyaltyAccrued/Redeemed/Balance` ARE real `SaleDto`
fields (TASK-405) but only `CreateSaleAsync` (mobile's immediate checkout response) populates
them — `GetSalesForShiftAsync` (what `GET /api/pos/sales`, i.e. this web view, actually calls)
never does, confirmed live (created a sale, fetched it back via the list endpoint, got
`null`/`null`/`null`). Followed "don't invent data": did NOT add a fake `customerId` field to
`frontend/features/pos/types.ts`; DID add the three loyalty fields (real, just always-null via
this endpoint today) plus a shared `saleHasLoyaltyActivity()` helper, and gated a new
"Лояльність" `DrawerSection` in `SaleDetailDrawer.tsx` + a `Gift` icon indicator in
`SalesTable.tsx` on it — both correct and forward-compatible, but dormant until a backend
follow-up wires `GetSalesForShiftAsync` to the ledger. Also noted: `features/customers/` has
no per-customer deep link (`CustomerDetail` is a client-state drawer, no `/customers/[id]`), so
even a future `CustomerId` could only link to the customers list, not a specific record.
`npx tsc --noEmit` clean, `npm run build` clean (exit 0, `/pos` route present). Live-verified
on the dev stack end-to-end (backend+frontend+Postgres, seeded `manager@demo.local`): opened
shift, created a real sale via the API, confirmed the web `/pos` page renders it with no Gift
icon (correct — no loyalty data) and the drawer's General info section unaffected with the new
Loyalty section correctly absent; no console errors. Cleaned up after: closed the test shift,
stopped both preview servers, killed the orphaned backend process. Only
`frontend/features/pos/{types.ts,components/SaleDetailDrawer.tsx,components/SalesTable.tsx}` +
`frontend/messages/{en,uk}.json` touched — confirmed via `git diff` that the pre-existing
uncommitted activity-log-labels changes already sitting in the two message files (unrelated,
predates this task) are untouched by my hunk. **New backend task needed:** (1) add
`CustomerId`/`CustomerName` to `SaleDto`, map in both `CreateSaleAsync` and
`GetSalesForShiftAsync`; (2) map `LoyaltyLedgerEntry` (by `PosTransactionId`) into
`GetSalesForShiftAsync` so the already-built web section actually shows real data. Not
committed (repo convention — main session/user commits).

## TASK-409 — Frontend: Marketing analytics (RFM) dashboard (Фаза 1)
**Status:** done · **Agent:** frontend-developer · **Depends:** TASK-406 (backend contract) ·
**Next:** security-reviewer (already scheduled against 404-411, unaffected by this frontend-only
task), documentation-writer (glossary/api-contracts notes — closed by TASK-415)
Log: `.claude/logs/tasks/409_2026-07-26_marketing-analytics-frontend_frontend-developer.md`
Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Фаза 1". Built
`frontend/features/marketing-analytics/` off task log 406's "Frontend API-контракт" section as the
sole contract source (never read the C#) and `docs/uployal/RFM_ANALYSIS.md` for UI/UX behavior. New
`/marketing-analytics` page (role gate matching backend's `CanViewAnalytics` floor,
`useRequireTab`, module gate), full `types.ts` transcription of the RFM contract plus an own-
judgment 11-segment→6-color-group mapping (the competitor doc only defines the 6 color
*meanings*, not a segment table), `api/marketingAnalytics.ts` (all 8 endpoints), one `useQuery` per
GET keyed on the full filter object (deliberately no `keepPreviousData` — never shows a mix of
old/new-filter data), components for the filter bar (new multi-store popover — existing
`useStoreContext`/`StoreSelector` is single-store only), the 11+1 segment grid, and a 4-panel
segment-detail cluster (top products / affinity+basket tabs / behavior charts / recommendation
card with the separate on-click "explain more" Claude call). **Shared-lib changes, small and
deliberate:** `frontend/lib/download.ts` gained `downloadFilePost` (plan assumed GET-only exports;
actual backend contract is POST+JSON body for all 3 exports), `frontend/lib/roles.ts` gained
`canExportMarketingAnalyticsPii`, `frontend/features/modules/types.ts`/`Sidebar.tsx` gained the
`marketing_analytics` module key/NavGroup. Full live browser verification on the dev stack: seeded
~12 synthetic customers into an existing dev tenant to get a non-trivial RFM population, confirmed
overview/segment-detail math, affinity vs. basket returning genuinely different numbers for the
same product, `/explain`'s real 503 (no Claude key in dev) rendering below (not replacing) the
template recommendation, atomic recalculation on period/store changes, the documented
"Hibernating always beats Lost" priority interaction from task log 406 reproduced live with seeded
data, empty-segment and store-scoped "no purchase" behavior matching the documented backend design,
and all 3 exports returning real non-trivial `.xlsx` files. **Discrepancy found and documented, not
invented around:** export responses carry no `Content-Disposition` header (log 406 said the
filename would arrive that way) — harmless here since `downloadFilePost` always uses its own
client-generated filename. `tsc`/`build` clean (`/marketing-analytics` 13.1 kB, in line with sibling
analytics routes). Flagged a follow-up via `spawn_task` (became TASK-413): provider/admin panel
module-activation lists didn't have `marketing_analytics`/`loyalty` yet, only a direct DB write
could enable the module for testing. Not committed.

## TASK-405 — Backend: Loyalty program Application+Api layer (Фаза 0)
**Status:** done (2026-07-26) · **Agent:** backend-developer · **Depends:** TASK-404 (Task #2
of the loyalty/RFM plan's agent sequence) · **Next:** frontend-developer + mobile-developer
(Task #4, parallel), security-reviewer (mandatory pass before release)
Log: `.claude/logs/tasks/405_2026-07-26_loyalty-backend_backend-developer.md`
Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Фаза 0". `ConsumerAuthController`
(`/api/consumer-auth/register|login`, own service — claim shape too different from staff
`AuthService` to share) issuing a new `IJwtService.GenerateConsumerAccessToken` (sub+
`consumer_account_id`, role="consumer", no tenant_id, 30-day lifetime since ConsumerAccount has
no refresh-token flow — **flagged for security-reviewer**, no revocation mechanism yet).
`PhoneNormalizer` (+380XXXXXXXXX, Application/Common, ConsumerAccount-only). `LoyaltyService`
(join/code/history for consumers; resolve-code/manual-adjust/my-membership/join-as-staff/settings
for staff) — QR payload `SGLOY1.{membershipId}.{code}`, `ITotpService.GenerateCode` (new: server
computes the code, unlike 2FA's verify-only). Anti-replay via new
`ILoyaltyRepository.TryClaimTimestepAsync` — single WHERE-guarded `ExecuteSqlInterpolatedAsync`
UPDATE (LoyaltyMembership has no EF concurrency token), proven atomic against live Postgres
(4 new tests). Resolve-code rate-limit/lockout via new `IResolveCodeAttemptTracker`
(`IMemoryCache`-backed, since LoyaltyMembership has no FailedLoginAttempts/LockoutUntil columns —
**flagged for security-reviewer**: single-instance-deployment tradeoff, doesn't survive restart or
scale across instances). `PosService.CreateSaleAsync` extended (redemption then accrual, both
computed on net TotalAmount, all in the sale's one existing SaveChangesAsync — no separate
commit); `Customer.TotalOrders/TotalSpent` finally get written for any sale with a CustomerId.
`AppRoles.Consumer` added (deliberately NOT in `AppRoles.All`); `Tenant.UpdateModules` gained
`"loyalty"`/`"marketing_analytics"` keys; `frontend/lib/roles.ts`/`mobile/lib/roles.ts` mirrored
with the bare `Consumer` constant only (no role-set inclusion — different session shape
entirely). Did NOT touch Domain entities/DbContext/migrations (TASK-404's schema, frozen).
`dotnet build` 0 err/0 warn, `dotnet test` 1004/1004 green (was 936; +68 new, incl. 4 live-Postgres
anti-replay tests and the existing live RLS/concurrency suites re-verified green with the new
dependencies wired in). `tsc --noEmit` clean on frontend+mobile. Not committed.

## TASK-404 — DB: Loyalty program schema (Фаза 0 — ConsumerAccount/LoyaltyMembership/LoyaltyLedgerEntry/LoyaltyProgramSettings)
**Status:** done (2026-07-26) · **Agent:** database-engineer · **Depends:** none (Task #1 of the
loyalty/RFM plan's agent sequence) · **Next:** backend-developer (Task #2)
Log: `.claude/logs/tasks/404_2026-07-26_loyalty-schema_database-engineer.md`
Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Фаза 0". 4 new entities +
`AddLoyaltyProgram` migration: `ConsumerAccount` (global, no TenantId, **no RLS at all** —
deliberate, same precedent as `tenants`, flagged for mandatory security-reviewer pass);
`LoyaltyMembership`/`LoyaltyLedgerEntry`/`LoyaltyProgramSettings` (tenant-scoped, canonical
fail-closed triad). New identity-based `consumer_self_access` policy on
`loyalty_memberships`/`loyalty_ledger_entries` (first of its kind in this repo — lets a
cross-tenant ConsumerAccount JWT, which never sets `app.tenant_id`, read its own rows via
`app.consumer_account_id` instead). `provider_bypass` written as `IN ('provider',
'provider_admin')` from day one on all 3 tenant tables (deviates from database-schema.md's
literal single-role template — matches the precedent `ExpandProviderBypassToProviderAdmin`
already established for the other 71 tables). Extended `TenantConnectionInterceptor` (new
`app.consumer_account_id` session var + `"consumer"` role whitelist entry). Verified live via
`psql`: `customers` RLS already has the full canonical triad (plan's claim confirmed, no fix
needed). `dotnet build` 0 err/0 warn, `dotnet test` 936/936 (14 new: interceptor unit tests +
4 new live `LoyaltyRlsIntegrationTests` proving cross-tenant consumer read, ledger EXISTS-scoping,
staff-session isolation unaffected, fail-closed on full reset). Migration applied to dev DB via
`crm` superuser (FK-validation-under-RLS gotcha), Down()/Up() round-tripped clean. Not committed.

## TASK-401 — Backend: store-scope filter on GET /api/locations (ADR-022 Stage 3 companion)
**Status:** done (2026-07-23) · **Agent:** backend-developer · **Depends:** TASK-392 (user_locations Stage 1), ADR-022
Log: `.claude/logs/tasks/401_2026-07-23_locations-list-store-scope-filter_backend-developer.md`
Stage 3 RESTRICTIVE RLS scopes business DATA but `locations` isn't one of the 9 scoped tables, so
a single-store user still saw every tenant store in StoreSelector. `LocationService.GetAllAsync`
now takes (tenantId, userId, role) from JWT claims via the controller: admin tier
(provider/provider_admin/enterprise_admin) sees all; scoped roles (network_manager..staff) with
≥1 `user_locations` row see only assigned; **0 rows = fail-open (full list)** — deliberate
transitional semantics until Stage 2 backfill completes (StoreSelector takes `stores[0]`, hides on
empty; real protection is the RLS layer), documented in code. Reused TASK-392's
`IUserLocationRepository` (no new repo); role set from Domain `AppRoles` (not Infrastructure
`AppPolicies`). GetById/zones/floor-plan untouched; no frontend changes needed. `dotnet build`
0 err, `dotnet test` 918/918 (11 new: 3 branches + missing-claim defensive). NOT committed —
main session commits together with the Stage 3 merge.

## TASK-400 — Frontend: hide Locations Create/Edit buttons for roles below AtLeastEnterpriseAdmin
**Status:** done (2026-07-23) · **Agent:** frontend-developer · **Depends:** none (bug fix)
Log: `.claude/logs/tasks/400_2026-07-23_locations-create-button-role-gate_frontend-developer.md`
Product owner bug report: "Створити" on `/locations` for type "Склад" → 403 with no friendly
error. Root cause (confirmed in main session, backend untouched — `LocationsController.Create`/
`Update` are deliberately `AtLeastEnterpriseAdmin`-only per ADR-020/ADR-022, no capability-OR
escape hatch by design): `locations/page.tsx` rendered "Create"/"Edit" unconditionally for every
`CanViewStock` role. Fix: gated both buttons behind
`hasRole(me?.role, AT_LEAST_ENTERPRISE_ADMIN)` (same pattern as `users/page.tsx`'s
`canManageRoleTemplates`) — hide, don't disable. "Plan" (floor-plan) link and the page's own
`GET` list untouched (both correctly open to all `CanViewStock` roles). `npx tsc --noEmit` clean.
Live-verified on local dev stack: store_manager (the exact reported scenario) now sees no
Create/Edit; enterprise_admin still sees both. Not committed (task brief didn't ask for it).

## TASK-398 — Backend: per-item sidebar tab catalog (item-level AllowedTabs granularity)
**Status:** done (2026-07-20) · **Agent:** backend-developer · **Depends:** TASK-391/ADR-021, TASK-397
Log: `.claude/logs/tasks/398_2026-07-20_per-item-tab-catalog_backend-developer.md`
Product feedback on ADR-021's Feature 1: whole-group `AllowedTabs` grants (e.g. "operations" = 7
pages at once) were too coarse. Added 27 item-level keys (literal `NavItem.href` per page,
verified against `Sidebar.tsx`'s `buildNavGroups`) alongside the original 10 group-level keys in
`TenantRoleTabs.All` — both flavours validate through the same `TenantRoleService.Validate` check,
no branching needed. `GET /api/tenant-roles/tabs` now returns a hierarchy
(`TenantRoleTabGroupDto[]` — group node with its own bulk-grant key + nested per-page items;
standalone Dashboard section has `groupKey: null`) instead of TASK-391b's flat list. Flagged (not
fixed, out of scope): `Sidebar.tsx` still only reads the group-level key — item-level grants do
nothing client-side until a follow-up frontend task wires them in; `"/settings/legal-entities"` is
in the catalog for completeness but its existing `canManageLegalEntities`-only carve-out should
stay excluded from that future generic check. `dotnet build` 0 err (1 pre-existing unrelated
warning), `dotnet test` 907/907 green. Docs updated: `.claude/docs/api-contracts.md`,
ADR-021 addendum in `.claude/docs/decisions.md`. Local commit only, no push (product owner pushes).

## TASK-373 — Docs: Block 19 pre-launch audit (FINAL) — go/no-go readiness + stale-doc refresh
**Status:** done (2026-07-16) · **Agent:** documentation-writer + project-manager (main session, direct) · **Depends:** TASK-350..372
Log: `.claude/logs/tasks/373_2026-07-16_prelaunch-readiness-gono-go_documentation-writer.md`
Final block of the pre-launch audit (`eager-pondering-tower.md`). Synthesised all 20 blocks (0–18 +
this one) into the main deliverable `.claude/docs/prelaunch-readiness.md` — executive verdict,
per-block summary, critical fixed findings by severity, launch blockers, user-decision items, accepted
risks, metrics. **Verdict: NO-GO today, short path to GO** — every audit fix is on dev/staging only and
is still an **uncommitted working tree** (verified via `git status`); production runs the full pre-audit
codebase with all found bugs (RLS fail-open, dead worker crons, POS race, privilege escalation, broken
write-offs, non-functional mobile). **4 launch blockers:** (1) commit + deploy the audit to prod;
(2) run the 8 dev-applied EF migrations on prod (+ decide on the never-applied
`ExpandProviderBypassToProviderAdmin`); (3) SSH-verify prod's Postgres connection role is a
non-superuser (`rolsuper=f, rolbypassrls=f`) — an assumption not confirmed this session, and staging
shipped without it (KI-027), so the canary is a net not a substitute; (4) device-test the mobile app
(KI-024/025/026 verified at code level only, no device in the audit env). Refreshed the three stale
2026-06-04 docs (`architecture.md`/`backend-structure.md`/`frontend-structure.md`) to current reality
with a "Last reviewed: 2026-07-16" line each (v1→v4 shipped, Store→Location/Product→Item renames, worker
queues, ~75 migrations, KI-006/004 resolved + KI-027/028 role note). Metrics: backend 854/854, frontend
48/48; ~16 P0 + ~12 P1 fixed; ~11 KI resolved / ~13 open. No code changed (docs only).

## TASK-371 — Security: Block 18 pre-launch audit — OWASP/pentest, dependency CVE scan, secrets check
**Status:** done (2026-07-16) · **Agent:** security-reviewer (main session, direct) · **Depends:** Blocks 0-17
Log: `.claude/logs/tasks/371_2026-07-16_owasp-pentest-block18_security-reviewer.md`
Block 18 of the pre-launch audit (`eager-pondering-tower.md`), final security pass before Block 19.
**Found a P0 (staging-only, KI-027):** live cross-tenant IDOR test on staging (created a real
second tenant via the admin API) showed `GET /api/items/{id}`/`stock/{id}`/`locations/{id}`
returning full data across tenants. Root cause: `shelfguard_staging` (the staging Postgres
connection role) is a superuser (`rolsuper=t, rolbypassrls=t`) — Postgres superusers bypass RLS
unconditionally regardless of `FORCE ROW LEVEL SECURITY`, same bug class production already hit
and fixed once (`feedback-rls-superuser-bypass` memory: separate non-superuser `shelfguard_app`
role + `ALTER TABLE ... OWNER TO`), but that fix was never repeated for staging when Block 0 stood
up `docker-compose.staging.yml`. Attempted the same fix live (create `shelfguard_staging_app`,
transfer table ownership) — **blocked by the harness's own permission classifier** as an
unauthorized persistent infra change; did not work around it, documented as KI-027 with the exact
fix ready to run once the user authorizes it. **Also documented (KI-028):** `GetByIdAsync`-style
repository methods (Items/Stock/Locations and most others) have zero app-level `TenantId` filter —
by design, per CLAUDE.md's "trust RLS" architecture — meaning RLS is the *sole* tenant-isolation
layer for these reads, a single point of failure if a role misconfiguration like KI-027 ever
reaches production. Could not independently re-verify production's actual DB role this block (no
local `.env.production`, SSH out of scope per "прод не чіпаємо") — flagged as the one open
assumption behind believing production is unaffected.
**OWASP pass results:** SQLi — clean, no `FromSqlRaw`/`ExecuteSqlRaw` with interpolated strings
anywhere in backend (only safe `ExecuteSqlInterpolatedAsync` in test cleanup code); worker's raw
`pg` queries are 100% parameterized (`$1`/`$2`), no template-literal-with-variable SQL found. XSS —
zero `dangerouslySetInnerHTML` anywhere in `frontend/`. Broken Auth — live-verified: account
lockout (5 fails → 15 min, generic error, no state disclosure, per-account not global) and JWT
validation (tampered signature, `alg:none`, expired-with-correct-secret all correctly rejected
with 401; `ClockSkew=Zero`, no leeway). 2FA — live end-to-end (real TOTP secret, RFC 6238 codes
generated locally): brute-force on `/2fa/verify` hits the same account-lockout counter as password
login, not just the IP-partitioned rate limiter; recovery codes single-use; challenge token has
its own JWT audience (can't be replayed as an access token), 5-min expiry, tied to one user. RBAC —
live-verified a `merchandiser` (lowest-rank) account gets 403 on both `AtLeastStoreManager` and
`AtLeastEnterpriseAdmin`-gated endpoints. Integration-secret masking — live-verified: PUT a fake
Claude API key, GET returns `"••••CDEF"` (last 4 chars), matches CLAUDE.md's rule; code review
confirms the same masking + round-trip protection for prro/vchasno/telegram/resend/webhook/iot.
**Dependency CVE scan — fixed what was safely fixable:** backend NuGet had 4 High-severity CVEs
(`Npgsql`/`Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.0, `Microsoft.Extensions.Caching.Memory`
8.0.0 transitive via EF Core, `System.Net.Http`/`System.Text.RegularExpressions` 4.3.0 transitive
via old test-SDK deps) — bumped `Microsoft.EntityFrameworkCore(.Design/.InMemory)` and
`Npgsql.EntityFrameworkCore.PostgreSQL` to 8.0.11 (had to align all three on the same patch to
avoid an assembly-version conflict between the Npgsql provider's pinned EFCore.Relational
dependency and a naively-higher 8.0.29), and `Microsoft.NET.Test.Sdk`/`xunit`/
`xunit.runner.visualstudio` to their latest 2.x-compatible versions — **0 vulnerable packages
remain**, `dotnet build` clean (0 err), `dotnet test` 850/850 green. Frontend: bumped `next`
14.1.4→14.2.35 (the actual latest patch in the 14.x line, confirmed via npm registry — not a
major-version jump) + matching `eslint-config-next`, clearing the Next.js authorization-bypass/
cache-poisoning/XSS CVEs that exist in 14.1.x; 12→9 vulnerabilities, remaining 9 (next's own
still-unfixed-in-14.x items, `eslint-config-next`'s `glob` CVE, `vite`/`vitest`'s `esbuild` CVE)
all require a major-version bump (Next 15/16, ESLint config v16, Vitest v4) — documented, not
forced, per this block's "patch/minor only" mandate. `tsc --noEmit` clean, `npx vitest run` 48/48
green, `npm run build` clean. Worker: 1 low-severity `esbuild` CVE (dev-only), fixed via
non-force `npm audit fix` → 0 vulnerabilities. Mobile: fixed the 1 High (`form-data` CRLF
injection) via non-force `npm audit fix`; remaining 10 moderate are all transitive via Expo's own
build-time CLI tooling (`@expo/*`/`xcode`/`uuid`), fix requires an Expo SDK major-version change —
documented, not forced; `tsc --noEmit` clean after the fix.
**Secrets check:** grepped current source + full git history for Anthropic/AWS/Slack/Telegram key
patterns — 0 real matches (only a UI placeholder string `"sk-ant-api03-..."` in the Integrations
settings form hint, not a real key). No `.env`/`.env.staging`/`.env.production` file was ever
committed (`.env.production.example`/`mobile/.env.example` are template-only with `CHANGE_ME`
placeholders); `frontend/.env.local` is committed but only contains a `NEXT_PUBLIC_*` value
(intentionally public) — not a leak. `.gitignore` confirmed covers `.env.staging`/
`.env.production`. KI-014 mitigations (account lockout + 2FA) re-verified live this block, see
above and the updated KI-014 entry.
**Needs a user decision:** KI-027 (staging RLS-bypass fix — ready to execute, blocked by
permission classifier, needs explicit go-ahead) and KI-028 (defense-in-depth tenant-filter
question — 3 options documented, none executed). `.claude/docs/known-issues.md` updated with both
new entries + the KI-014 re-verification note.

## TASK-370 — DevOps/DB: Block 17 pre-launch audit — load testing
**Status:** done (2026-07-16) · **Agent:** devops-engineer + database-engineer (main session, direct) · **Depends:** Block 0, Blocks 1-16
Log: `.claude/logs/tasks/370_2026-07-16_load-testing-block17_devops-engineer.md`
Block 17 of the pre-launch audit (`eager-pondering-tower.md`). Fixed a real incident during
staging bring-up: `docker-compose.staging.yml` had no explicit project name and collided with
the dev stack's default project name, causing `docker compose up` to delete the running dev
containers (data survived — named volumes untouched); added `name: shelfguard_staging` and fixed
a wrong `DATABASE_URL` (host-mapped port instead of Compose-internal `postgres:5432`) that was
crash-looping the staging api. 4 new k6 scenarios (`loadtests/`): login-storm (rate-limiter +
lockout hold under real concurrency; found+fixed 3 sequential `SaveChangesAsync` calls in
`AuthService` batched to 1, p95 2.28s→1.77s; residual latency traced to bcrypt workFactor=12,
~600-700ms/verify, a security tradeoff not changed here — user decision needed if sub-1s login
is required), pos-queue (40 concurrent registers, Block 6's xmin optimistic-concurrency fix
verified correct under real load — 95 sales/255 conflicts/0 errors, stock delta exactly matches,
zero oversell), bulk-order-creation (`/api/orders/calculate`, p95=14ms, no issue), analytics-
concurrent-read (run alongside pos-queue, p95=21ms, no issue). `dotnet test` 850/850 green.
Follow-up flagged (not fixed, out of scope): POS sale path fetches stock unscoped by store
(`task_7d60b19c`).

## TASK-369 — DB: Block 16 pre-launch audit — cross-cutting DB performance sweep
**Status:** done (2026-07-15) · **Agent:** database-engineer (main session, direct) · **Depends:** TASK-350..368
Log: `.claude/logs/tasks/369_2026-07-15_db-performance-audit-block16-part2_database-engineer.md`
Block 16 of the pre-launch audit (`eager-pondering-tower.md`) — aggregated DB-performance pass.
An earlier same-day attempt got only as far as one migration (`AddActivityLogsIndexesAndDropSupersededStockIndexes`
— activity_logs indexes + dropped 2 superseded product_stock indexes, already verified) before
running out of session budget; this entry covers the rest. **Systemic audit of all 76 FORCE RLS
tables for a tenant-leading index:** cross-referenced against actual repository query methods
(not just schema) to avoid flagging false positives — found and fixed 2 real gaps via
`AddChatSessionsAndSupplySchedulesTenantIndexes` (EF-tracked fluent `HasIndex`, applied to dev
DB): `chat_sessions` had zero index besides PK despite `ChatService.GetSessionsAsync` (tenant
chat inbox) querying `WHERE TenantId == tenantId ORDER BY UpdatedAt DESC` directly — real,
present-day full scan on every inbox load, not a future risk; `supply_schedules`'s
`GetAsync(storeId?, supplierId?)` has both filters optional, so the Settings page's unfiltered
list has nothing but RLS to narrow rows. Checked 8 other initially-suspected tables
(`product_adu`/`product_buffer`/`promo_cannibalization`/`product_supplier_settings`/
`as_work_order_lines`/`ticket_comments`/`marketplace_order_items`/`stock_events`) and confirmed
**no fix needed** — every live query path already filters on a Guid FK to a one-tenant-only
parent (StoreId/WorkOrderId/DiscountId/TicketId/OrderId), so RLS's extra TenantId predicate never
causes a real scan; `stock_events` is write-only today (zero read call sites exist anywhere).
Deliberately did not blindly index all 10 — 8 would have been pure write overhead with zero read
benefit (same over-indexing failure mode Block 15 flagged for `notification_queue`).
**EF FK/index-tracking re-check:** `StockMovement` still 100% raw-SQL FKs (invisible to EF, risk
still low, unchanged from TASK-352); `Discount` has partially drifted since TASK-352 — `TenantId`/
`CreatedBy`/`ApprovedBy` are now fluent-tracked, only `ProductId`/`StoreId`/`ProductStockId`
remain raw-SQL-only — doc corrected. Grepped all 47 migrations for raw-SQL FK/index statements;
no new undocumented cases beyond the already-known ones. **N+1 sweep** of
Analytics/Catalog/Events/Notifications (not covered by their own audit block) — clean, no query-
in-loop patterns found anywhere in those 4 modules. `dotnet build` 0 err/0 warn (1 pre-existing
unrelated warning), `dotnet test` 850/850 green. `dotnet ef database update` couldn't connect
(env quirk noted in TASK-352, unrelated to this work) — applied migration SQL directly to dev DB,
hand-verified both indexes exist. Docs updated: `.claude/docs/database-schema.md` (new "Block 16"
section + corrected FK-tracking note). Nothing left needing a user decision.

## TASK-368 — Fullstack: fix unverified Telegram account-linking path (security)
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session, direct) · **Depends:** TASK-367 finding
Log: `.claude/logs/tasks/368_2026-07-15_telegram-link-security-fix_backend-developer.md`
TASK-367 found two competing Telegram-link mechanisms: the real, used one
(`POST /api/auth/telegram/link`) let a user paste a raw client-supplied chat_id with zero proof
of ownership; the safe one (`POST /api/telegram/link-code` + worker's `/start <code>` listener)
was already correctly implemented end-to-end but never called by the web frontend. User confirmed
in chat: fix now. Removed the unverified endpoint (`AuthController`/`IUserService`/`UserService`/
`UserDtos` — `LinkTelegramAsync`/`LinkTelegramRequest`); rewrote `TelegramLinkSection.tsx` to
generate a one-time code, show the `t.me/<bot>?start=<code>` deep link + manual-fallback
instructions, and auto-detect success via 3s polling of `/api/auth/me` (matches the codebase's
existing chat/marketplace/IoT polling convention) plus a manual "Перевірити зараз" button. Mobile
already used the safe flow; worker's `telegram-listener.ts` needed no changes (confirmed correct).
**Found + fixed a second bug while wiring this up:** `AuthUserDto` never included
`TelegramChatId` at all — the old "Telegram: Підключено" status was pure client-side optimistic
cache fiction from the now-removed endpoint's `onSuccess`, never real server state; would have
silently reverted on any reload/cache invalidation. Added `TelegramChatId` to `AuthUserDto` +
`AuthService.ToDto`, without which the new polling UX could never have detected a real link.
Live-verified end-to-end on the dev stack: generated a real code via the UI, simulated the
worker's exact `UPDATE users SET "TelegramChatId"=...` side effect via `docker exec ... psql`
(no live Telegram bot session available in this environment), confirmed the UI auto-flipped to
"✓ Підключено" within one poll cycle with no reload, and that the status survives a hard reload.
0 pre-existing dev-DB rows found linked via the old insecure path (nothing to migrate/document).
`dotnet build` 0 err/0 warn (1 pre-existing unrelated warning), `dotnet test` 850/850 green,
`tsc --noEmit` clean. Flagged, not fixed (low severity, out of scope): the worker's raw-SQL link
path writes no `activity_logs` row, so real Telegram linking no longer appears in the user
activity log (it only ever did via the removed insecure path) — candidate for a small follow-up.

## TASK-367 — Architecture: Block 15 pre-launch audit — cross-cutting duplication/dead code/unused endpoints
**Status:** done (2026-07-15) · **Agent:** project-architect (main session, review-only) · **Depends:** TASK-350..366
Log: `.claude/logs/tasks/367_2026-07-15_crosscutting-duplication-deadcode-audit_project-architect.md`
Block 15 of the pre-launch audit (`eager-pondering-tower.md`) — first repo-wide (not per-module) pass,
review only per `project-architect` guardrails. **Dead code confirmed (not deleted):** `Store`/`StoreZone`
entities + `StoreService`/`StoreRepository` are 100% unreferenced (no `DbSet`, no DI registration,
`StoreRepository` self-marked `[Obsolete]` with every method throwing, `StoresController.cs` already an
empty stub since TASK-201) — attempted deletion of the 9 dead files, blocked by the permission
classifier as exceeding this task's "recommend, don't execute multi-file changes" scope, reverted
cleanly (`git checkout --`, confirmed 0 diff, build/tests back to baseline). Recommended as a small
dedicated follow-up (~15 min, zero risk). **Duplication confirmed:** the 3 Claude advisors
(`ClaudeOrderAdvisor`/`BusinessAssistantAdvisor`/`SupplierAdvisor`) share byte-identical
`ResolveAsync`/`IsConfiguredAsync` key-resolution logic + response-parsing boilerplate — recommend
extracting a shared `ClaudeKeyResolver` helper, not executed (multi-file). Receipts/Transfers/WriteOffs
"document + items" pattern (Block 4's earlier flag) — recommend extracting only the read-side
`GetAll`/`GetPaged`/`GetById` triad, leave Create/status-transition logic separate (genuinely
divergent) — not executed. Mobile `lib/roles.ts` vs frontend `lib/roles.ts` — intentional subset, not
1:1 duplication, acceptable given no monorepo tooling; recommend cross-file comments only. Support
feature retirement (TASK-365) verified complete, no orphaned remnants found. **Unused endpoints found:**
`POST /api/telegram/link-code` orphaned (frontend uses `/api/auth/telegram/link` instead — an unverified
direct chat-ID-paste path; the bot-code flow this endpoint feeds can never fire in production, needs a
security/product decision); `SuppliersController` full CRUD (`/api/suppliers`, own ADR-020 permission
policies) has zero frontend/mobile callers — `frontend/features/suppliers/` documented in CLAUDE.md does
not exist, Receipts has no UI to pick/manage suppliers; `DiscountsController`/`CannibalizationController`/
`SupplySchedulesController`/`WeatherController`'s coefficient CRUD all have full backend, zero UI — a
pattern (v2-spec tuning knobs built backend-first, no settings UI), flagged as a pre-launch product gap,
not a code-quality fix. `dotnet build` 0 err/0 warn, `dotnet test` 879/879 green (unchanged, no code
landed this block).

## TASK-366 — Mobile: Block 14 pre-launch audit — write-offs/POS contract, role gating, token restore
**Status:** done (2026-07-15) · **Agent:** mobile-developer (main session) · **Depends:** TASK-354, TASK-356/357
Log: `.claude/logs/tasks/366_2026-07-15_mobile-audit-role-auth-bugs_mobile-developer.md`
Block 14 of the pre-launch audit (`eager-pondering-tower.md`) — first mobile-focused block.
Write-off mobile payload (`{productId, quantity}`) already matched Block 4's fixed backend, and
POS's 409-handling already correctly surfaced the concurrency error — but found and fixed 3
critical, previously-undiscovered mobile-only bugs that had been silently breaking those very
flows underneath: (1) **every role gate in the app** (`(app)/_layout.tsx`, write-offs,
customers, transfers, schedules, service-desk, dashboard) used invented PascalCase role names
(`'StoreManager'`, `'Director'`, `'Admin'`) that never match the real lowercase role strings —
POS tab invisible to cashiers, manager approve/reject actions invisible everywhere; fixed via
new `mobile/lib/roles.ts` (mirrors `frontend/lib/roles.ts`) used by all 9 affected screens
(KI-024). (2) `user.locationId` was always `undefined` (backend's wire field is `storeId`, no
mapping existed) — blocked write-off/transfer/production creation outright; plus
write-offs/transfers/stock list endpoints were sent the wrong query-param name (`location_id`/
`locationId` vs backend's actual `store_id`), so even fixing (2a) wouldn't have filtered the
lists; fixed both (KI-025). (3) `user` was never restored after a cold app restart (`loadToken()`
only restored the token; the existing `getMe()` was dead code) — broke every role-gated screen
silently until re-login; wired `getMe()` into the boot sequence (KI-026). Also added missing
`onError` handling on write-off approve/reject (now that Block 4 hard-fails on insufficient
stock) and made mobile login fail loudly instead of silently on 2FA-enabled accounts (KI-023,
partial — no mobile 2FA UI exists, flagged for a product decision). Confirmed unchanged:
offline support still absent (KI-022, documented, not built — out of scope), `expo-secure-store`
correctly used for tokens (no AsyncStorage), React 18/TS 5 (web) vs React 19/TS 6 (mobile) is
not a real risk (fully separate npm projects, no shared code). `npx tsc --noEmit` clean after
every fix. `npm run lint` fails on missing `eslint.config.js` (pre-existing, not fixed).
`expo start --web` could not verify live rendering — `react-dom`/`react-native-web` aren't
installed (web target never set up); did not install new deps unprompted. No
emulator/device in this environment (per task brief) — contract-level verification only.

## TASK-365 — Fullstack: retire Support feature, migrate Settings to ServiceDesk
**Status:** done (2026-07-15) · **Agent:** main session (fullstack, no sub-agent per explicit
instruction) · **Depends:** TASK-363 finding
Log: `.claude/logs/tasks/365_2026-07-15_support-to-servicedesk-migration_fullstack.md`
User decision from TASK-363's flagged finding: retire `Features/Support` (tenant Settings UI +
provider backend, both orphaned/unreachable since 2026-06-20 per code trace), keep the already-live
`/service-desk` ServiceDesk feature as the single ticket system. Deleted the dead Support code on
both sides (controllers, service, frontend feature dir); left `SupportTicket`/`SupportMessage`
entities + DB tables untouched (ServiceDesk shares the `SupportTicket` entity/table, 0 rows in
either table in dev). Found and fixed a real gap while verifying: ServiceDesk's provider view could
see tickets but had no reply endpoint — added `GET/{id}` + `POST/{id}/comments` to
`AdminServiceDeskController` and wired a reply UI into `ProviderSupportTab.tsx`. Verified full
round-trip in-browser: tenant creates ticket → provider sees + replies → tenant sees the reply.
`dotnet build`/`dotnet test` (879/879) clean, `tsc --noEmit` clean.

## TASK-364 — Frontend: Block 13 pre-launch audit — cross-cutting frontend quality
**Status:** done (2026-07-15) · **Agent:** frontend-developer (main session) · **Depends:** TASK-363
Log: `.claude/logs/tasks/364_2026-07-15_frontend-crosscutting-quality-audit_frontend-developer.md`
Block 13 of the pre-launch audit (`eager-pondering-tower.md`) — first frontend-wide (not
per-feature) block. KI-004 (duplicate `apiFetch`) confirmed already resolved, no code change,
doc updated. Added `app/error.tsx`/`app/global-error.tsx` (neither existed) — friendly UA
fallback UI, `console.error` with a `TODO(KI-020)` marker for future Sentry wiring.
**Found + fixed while verifying:** `global-error.tsx` broke `npm run build` on the pinned
`next@14.1.0` (`PageNotFoundError: Cannot find module for page: /_document` — a known Next
14.1.0 bug, fixed 14.1.1+, triggered by App-Router-only + `global-error.tsx` + no
`pages/_document`); fixed by bumping `next` to `14.1.4` (same minor line, patch-only, smallest
fix). Live-verified the boundary in-browser with a temporary throwing test route (deleted
after). Evaluated moving the access token out of `localStorage` (XSS exposure) — traced the
boot sequence and found the dashboard layout hard-gates on `getToken()` *before* any network
call and nothing anywhere calls `/api/auth/refresh` proactively on mount, so removing
`localStorage` without adding a new bootstrap-refresh flow would log every user out on every
reload; **not fixed**, documented as KI-021 with 3 options for the user to choose from. Sentry
absence confirmed and documented as KI-020 (needs a real DSN only the user can provision).
Added 5 new Vitest test files (46 new tests, 0%→covered): `lib/api.test.ts` (401→refresh→retry
state machine + request/response handling), `lib/roles.test.ts`, `lib/providerPermissions
.test.ts`, `lib/supplierPermissions.test.ts`, `lib/slug.test.ts` — all pure-logic files with no
`@testing-library/react` dependency needed (none installed). `npx tsc --noEmit` clean,
`npx vitest run` 6/6 files 48/48 tests green, `npm run build` clean (post next bump).

## TASK-363 — Backend: Block 12 pre-launch audit — Provider / Admin / ServiceDesk / Chat
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-362
Log: `.claude/logs/tasks/363_2026-07-15_provider-admin-servicedesk-chat-audit_backend-developer.md`
Block 12 of the pre-launch audit (`eager-pondering-tower.md`). **Found + fixed a P0:**
`ProviderTeamService` let any `provider_admin` self-escalate to the literal owner role
(`role: "provider"`) via Invite/Update on themselves or a teammate — no rank/owner check existed
beyond "can't demote the owner." Since `ProviderController` (tenant CRUD, impersonation,
platform logs) is gated strictly to `role == provider` (not provider_admin), this let a
provider_admin grant itself full owner access. Also fixed: provider_admin could deactivate the
literal owner account (DoS). Fix: Invite/Update/Deactivate now take the caller's own role from
the JWT and reject granting/protecting the owner role unless the actor already is the owner.
10 new tests (`ProviderTeamServiceTests`, zero coverage before). **Found + fixed a P1/hardening
gap:** `chat_messages`/`support_messages` had RLS completely disabled (live-confirmed via
`pg_class`) — the only two tables in the whole Chat/ServiceDesk/Support family without it, while
every sibling (including the analogous `supplier_chat_messages`) has it. App code was already
scoping correctly everywhere (not a live exploit), but zero DB safety net. Fixed via
`20260715153812_AddChatAndSupportMessagesRls` (EXISTS-subquery-via-parent pattern, matches
`supplier_chat_messages`); live cross-tenant read test confirmed 0 rows leak, own-tenant reads
still work. **Flagged, NOT fixed — high-confidence P0, needs a product decision:** the `Support`
feature (Settings → "Служба підтримки", `/api/support/*`) is fully wired on the tenant side but
its provider-side reply UI is completely orphaned — zero frontend component anywhere calls the
correctly-implemented `/api/provider/support/*` hooks. Real tenant support tickets vanish with
no operator ever seeing them. Migration dates suggest ServiceDesk (4 days later) was meant to
replace it but the old tenant UI/backend were never removed. Needs a decision: build the missing
inbox, retire/redirect the old feature, or merge into ServiceDesk. Background task spawned.
Reviewed and confirmed correct, no changes: tenant onboarding atomicity (single SaveChanges,
both Provider and Admin onboarding paths), impersonation mechanics (stateless scoped JWT,
explicit frontend exit, audits back to the real provider's user id), provider-role isolation
from tenant flow (UserService's ValidRoles excludes all provider tiers), ServiceDesk status
lifecycle + access + no N+1, Chat IDOR (tenant id always from JWT, never request body) + no N+1,
RLS on support_tickets/ticket_comments/chat_sessions (Block 2 pattern intact), no worker code
touches any ServiceDesk/Chat table (Block 11's bug class doesn't apply here). `dotnet build`
0 err/0 warn (1 pre-existing unrelated warning), `dotnet test` 879/879 green (was 869). Migration
applied to dev DB only; prod not touched.

## TASK-362 — Backend: Block 11 pre-launch audit — IoT / Weather / Events / Cannibalization
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-361
Log: `.claude/logs/tasks/362_2026-07-15_iot-weather-events-cannibalization-audit_backend-developer.md`
Block 11 of the pre-launch audit (`eager-pondering-tower.md`). **Confirmed and fixed KI-016
(P0, same bug class as Blocks 7/9):** live-confirmed against the dev DB that `iot_devices`/
`weather_data`/`temperature_readings`/`product_stock` all have their store column renamed to
`"LocationId"` (v4 rename) while `stock_events` genuinely kept `"StoreId"`. Fixed
`weather-fetch.job.ts`'s `INSERT INTO weather_data` (still used `"StoreId"` even after TASK-358's
partial fix — every upsert had been throwing) and `mqtt-listener.ts` (4 places: device lookup ×2,
temperature_readings INSERT, product_stock FEFO SELECT). **Found one level deeper in the same
investigation:** `weather-fetch.job.ts`/`ai-order.job.ts` never called `SET app.role = 'worker'`
at all, and `notification.job.ts`'s `handleExpiryAlert`/`handleIotAlert` likewise never set it —
under the Block 2 fail-closed RLS fix, these queries silently returned zero rows unless the
pooled pg connection happened to inherit the role from another job's reused connection
(connection-pool-luck correctness, not guaranteed). Fixed all three files with the explicit SET,
matching every other worker job. Live-verified end-to-end on the rebuilt dev worker container
(real BullMQ jobs, real MQTT messages via `mosquitto_pub`, real DB queries) — not just
tsc/build — including proof that `handleIotAlert` now finds the 3 real matching users where it
previously would have found zero. **Also added:** MQTT temperature readings now sanity-bound
(`isPlausibleTemperature`/`isPlausibleHumidity` in `iot-rules.ts`, -60..60°C) before insert — a
broken sensor can no longer write garbage into `temperature_readings` or falsely trigger
`temp_violation`; live-verified a 9999°C reading correctly rejected. Reviewed and confirmed
correct, no changes: IoT device→location binding (no N+1), weather fallback (neutral 1× when
no data, matches Block 7's "never break AI orders" requirement), Events/Cannibalization default
coefficients match v2-spec §4/§5 exactly, `OrderCalcService` correctly wires all three
multipliers, RLS fail-closed + worker_bypass present on all 8 tables named in the brief
(live-confirmed via `\d`). **Flagged, not fixed — needs a product decision:** KI-019 —
`IotController`/`WeatherController`/`EventsController`/`CannibalizationController` (and nearly
all of v2/v3: Orders/Adu/Buffer/AiOrders/Pos) have no `[RequireModule]` gate despite CLAUDE.md's
architecture rule; not fixed because `Tenant.DefaultModulesForBusinessType` grants no tenant
`"auto_order"`/`"iot"`/`"pos"` by default, so adding the gate blind would 403 every currently-
working tenant. `dotnet build` 0 err/0 warn, `dotnet test` 869/869 green (unchanged — worker-only
block). Worker `tsc --noEmit` clean.

## TASK-361 — Backend: Block 10 pre-launch audit — Auto Service / Production
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-360
Log: `.claude/logs/tasks/361_2026-07-15_autoservice-production-audit_backend-developer.md`
Block 10 of the pre-launch audit (`eager-pondering-tower.md`). Module gating
(`[RequireModule]` on both `AutoServiceController`/`ProductionController`) already correct,
5 existing tests cover it. No old `stores`/`catalog_products` table references found (same
bug class as Blocks 7/9) — both modules are clean EF LINQ, no raw SQL. **Found + fixed a
P1:** `ProductionService.CompleteOrderAsync` silently fell back to a fake
`DateTime.UtcNow.AddYears(10)` expiry for the produced batch when the output `Item` had no
`ShelfLifeDays` configured — not literally null (the audit brief's specific worry) but the
same bug in disguise, defeating FEFO tracking for that batch without surfacing anything to
the user. Fixed: now validates `ShelfLifeDays` up front (before any ingredient consumption,
atomic guarantee preserved) and returns 422 if missing, mirroring `ReceiptService`'s
stricter "no placeholder expiry" pattern. 1 new test. Reviewed and confirmed correct, no
changes: FEFO in Production correctly scoped to `order.LocationId`; RLS on
`as_customers`/`as_vehicles`/`as_work_orders`/`as_work_order_lines`/`as_service_catalog`/
`production_orders`/`recipes` verified live via `pg_policies` — all carry the canonical
Block 2 fail-closed pattern; child tables `recipe_ingredients`/
`production_order_consumptions` deliberately have no own RLS (tenant scope inherited via
JOIN from parent, documented in entity comments, verified no unscoped access path exists);
no N+1 in either module's list endpoints. **Flagged, not fixed — needs a product
decision:** KI-018 — Auto Service has no location concept at all (`AsWorkOrder` has no
`LocationId`), so spare-part FEFO write-down is tenant-wide instead of location-scoped
(Production doesn't have this gap). Invisible for single-location tenants, a real
cross-location leak for auto-service chains, which v4-spec explicitly supports. Needs a
schema migration + API changes, out of scope for this block. `dotnet build` 0 err/0 warn
(1 pre-existing unrelated warning), `dotnet test` 869/869 green (was 868).
**Addendum (same day):** user confirmed directly in chat — plan the KI-018 fix now,
implement later. Full plan written into the task log (nullable `AsWorkOrder.LocationId`
additive migration + no RLS changes needed, verified live that RLS quals never filter on
`LocationId`; `IAutoServiceRepository.GetFefoOrderedAsync` gets a `locationId` param
mirroring the already-correct `IProductionRepository` shape; frontend reuses the existing
`useStoreContext`/`StoreSelector`, no new UI component). Effort ~1 day, low risk (additive,
no breaking API change). One open product question left unresolved on purpose (how
pre-migration `LocationId = NULL` orders behave — recommended: fall back to today's
tenant-wide FEFO rather than hard-block). `known-issues.md` KI-018 status updated to
"planned" with a link to the plan. No code changed for this addendum.

## TASK-360 — Backend: Block 9 pre-launch audit — Customers / Notifications / Schedules
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-359
Log: `.claude/logs/tasks/360_2026-07-15_crm-hr-notifications-audit_backend-developer.md`
Block 9 of the pre-launch audit (`eager-pondering-tower.md`). Modules had zero test coverage.
**Found + fixed 2 P0:** (1) `worker/src/jobs/notification.job.ts`/`expiry-check.job.ts`/
`stock-snapshot.job.ts` queried pre-rename table/column names (`catalog_products`/`stores`/
`"StoreId"` on `product_stock` — renamed to `items`/`locations`/`"LocationId"` mid-June) — the
entire hourly expiry-notification cron and its dashboard-snapshot sibling crashed on every run,
same bug class as TASK-358. Root-cause enabler: local dev `docker-compose.yml`'s worker
`DATABASE_URL` was still the broken .NET-format string TASK-033 (2026-06-11) already fixed for
staging/prod — never applied to dev, so no worker job had run successfully against a real DB in
dev this whole audit series; fixed alongside (`postgresql://` format). Also fixed a P1 in the
same file: `expiry-check.job.ts`'s hardcoded 1/3-day thresholds diverged from both v1-spec §2.2
and the backend's own `StockStatus.Compute` — batches 4-14 days out were cron-invisible, never
notified; now mirrors `PerishabilityClass.GetThresholds` via a join to `items`. All three fixes
live-verified end-to-end (rebuilt worker container, manually triggered jobs, confirmed
`notification_queue`/`stock_status_snapshots` rows written correctly, 0 errors).
(2) `notification_settings` RLS: Block 2 (TASK-352) deliberately kept a session-level fail-open
branch here, grouped with `users`/`refresh_tokens` as "pre-auth lookup" — live-reproduced that
this doesn't actually apply (every access is `[Authorize]`'d, JWT-derived, no anonymous path
touches this table) by seeding cross-tenant rows and reading them back under a RESET session.
Fixed via `20260715120000_FixNotificationSettingsRlsFailOpen` (removes only the outer fail-open
branch, keeps the inner null-TenantId branch needed for provider accounts); updated the existing
allowlist test + added a dedicated Postgres-integration regression test, both pass. **Found +
fixed a P1:** Schedules' shift-overlap guard (`DetectShiftConflicts`) only ran at publish time —
`AddShiftAsync`/`UpdateShiftAsync` never re-checked, so adding/editing a shift on an
already-published schedule could silently double-book an employee; fixed both methods with the
same overlap rule. **P2:** Customers had zero Phone/Email format validation (any string
accepted) — added a permissive-but-real format check. Reviewed and confirmed correct, no
changes: `customers`/`schedule_shifts`/`work_schedules` RLS (Block 2 fix verified live via
`pg_policies`, not just migration text), indexes (all TenantId-leading, match actual filters, no
gaps), no N+1 in any of the three modules' lists, Schedules role gating matches v1-spec §3.2.
Flagged, not fixed: KI-016 (`weather-fetch.job.ts`/`mqtt-listener.ts` same StoreId-column bug
class, Block 11 scope — background task spawned), KI-017 (`needs_verification` status has no
cron-triggered notification at all — schema gap, small dedicated task candidate). 15 new tests
(`CustomerServiceTests`, `ScheduleServiceTests`, `NotificationServiceTests` + 1 new Postgres RLS
regression test). `dotnet build` 0 err/0 warn, `dotnet test` 868/868 green (was 846). Worker
`tsc --noEmit` clean. Migration applied to dev DB; prod/staging not touched.

## TASK-359 — Backend: Block 8 pre-launch audit — Suppliers & Marketplace
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-358
Log: `.claude/logs/tasks/359_2026-07-15_suppliers-marketplace-audit_backend-developer.md`
Block 8 of the pre-launch audit (`eager-pondering-tower.md`). Pre-existing uncommitted changes
in `SupplierCabinetCooperationController.cs`/`CooperationRequestsTab.tsx` verified correct, no
further changes. **Found + fixed a P1:** supplier custom roles/permissions
(`SupplierRole.Permissions`, TASK-306) were UI-only — `SupplierCabinetController`/
`SupplierCabinetCooperationController` gated only by `RequireRole(supplier_admin)`, so any
invited staff member had full API access regardless of assigned role (self-escalation within
the supplier's own tenant — e.g. a `task_board`-only staffer could still invite new staff or
delete other roles). Same class of gap ADR-020 fixed for tenant roles. Fix: new
`SupplierPermissionAuthorization.HasPermission` (mirrors `LegalEntityAuthorization`, reads the
JWT `permissions` claim already correctly populated by the existing generic pipeline — only
the read side was missing) + in-body checks on every `SupplierCabinetController` action,
mapped 1:1 to the existing frontend nav permission grouping; chat left ungated (matches
BUG-019's deliberate decision). Corrected a stale/false comment in `Sidebar.tsx` claiming the
backend already gated the cooperation-flow routes. 4 new tests. **Flagged, not fixed — needs a
product decision:** cooperation-flow controller (agreements, orders, contract-settings,
support-tickets) has no fine-grained permission key defined at all; adding one means choosing
new taxonomy, a product call, not an objective fix. Reviewed and confirmed correct: agreement
lifecycle (no status can be skipped, pending→awaiting_signature→active→terminated), Вчасно
integration (per-tenant key via `integration_configs`, graceful error handling, not
hardcoded/shared), marketplace order isolation (supplier-scoped catalog validation, tenant-
scoped list/cancel/status-update), RLS on all supplier/marketplace two-tenant tables (created
with the canonical NULLIF pattern from day one — never subject to the Block 2/TASK-352
fail-open bug, so that fix correctly left them untouched; `provider_bypass`+`worker_bypass`
both present), no N+1 in order/agreement/chat/ticket list endpoints. `dotnet build` 0 err,
`dotnet test` 846/846 green (was 842). `tsc --noEmit` clean.

## TASK-358 — Backend: Block 7 pre-launch audit — AI Orders / AI Assistant
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-357
Log: `.claude/logs/tasks/358_2026-07-15_ai-orders-assistant-audit_backend-developer.md`
Block 7 of the pre-launch audit (`eager-pondering-tower.md`). **Found + fixed a P0:**
`worker/src/jobs/ai-order.job.ts` and `weather-fetch.job.ts` both queried `FROM stores` — a
table renamed to `locations` in `20260615183318_V4LocationsRename` — so the nightly
05:00 cron (v2-spec §7) never generated a single AI order suggestion, and `weather_data` was
never populated (every `AiOrderService.GenerateAsync` call, cron or manual, fed Claude an
empty weather array). Fixed both to `FROM locations` (columns unchanged). **Found + fixed a
P1:** the N+1 in `AiOrderService.GetListAsync` flagged in TASK-355's log (per-suggestion
`GetByIdAsync` just to read `Items.Count`) — `AiOrderRepository.GetListAsync` now
eager-loads `Items`, service reads the count directly; regression test added. **P2:** all
three Claude advisors (`ClaudeOrderAdvisor`/`BusinessAssistantAdvisor`/`SupplierAdvisor`) had
no explicit `AnthropicClient.Timeout` — SDK default is 10 min × up to 3 attempts, could hang
a synchronous `POST /api/ai-orders/generate` for ~30 min; set to 60s. Reviewed and confirmed
correct, no changes: AI isolation (Application layer has zero Anthropic SDK references,
only Domain interfaces), graceful error degradation (Claude failures → readable 400, never
500, already had try/catch + Ukrainian billing-specific message), API key masking (last-4,
fixed in TASK-347) and no logging of the key, RLS/cross-tenant isolation (same
per-request `AppDbContext` as everywhere else, no superuser/detached-scope bypass — the POS
Task.Run bug class from TASK-356 does not repeat here), no N+1 in AI-prompt context assembly
itself, no duplicate Claude spend from the frontend (both generate/ask hooks are React Query
mutations, buttons disabled while pending). 12 new tests (`AiOrderServiceTests`,
`AiAssistantServiceTests`). `dotnet build` 0 err/0 warn, `dotnet test` 842/842 green (was
830). Worker `tsc --noEmit` clean. **Flagged, not fixed (low severity):**
`weather-fetch-cron` fires at 06:00, an hour *after* `ai-order-cron`'s 05:00 — the morning AI
order run always reads the previous day's weather fetch.

## TASK-357 — Frontend: POS cash reconciliation UI (close-shift cash count)
**Status:** done (2026-07-15) · **Agent:** frontend-developer (main session) · **Depends:** TASK-356
Log: `.claude/logs/tasks/357_2026-07-15_pos-cash-reconciliation-ui_frontend-developer.md`
UI for TASK-356's `POST /api/pos/shifts/close { actualClosingCash? }` contract. New
`CloseShiftDialog.tsx` (replaces `window.confirm()`) — optional cash-count input,
blank = old no-reconciliation behavior; client-side negative guard mirrors backend's
400. New `CashReconciliationSummary.tsx` — renders only when `closingCash != null`,
shown in the existing Z-report card: opening/expected/actual cash + discrepancy badge
(green "Збіг" exact / amber "Надлишок" surplus / red "Недостача" shortage).
`ShiftDto`/`CloseShiftRequest` types, `useCloseShift` hook updated. **Found+fixed
while verifying:** both close/open shift dialogs stay mounted while hidden
(`if (!isOpen) return null`), so internal `useState` doesn't reset on reopen — a
stale `actualClosingCash` from a previous close silently carried into the next one.
Fixed in `CloseShiftDialog.tsx` via a `useEffect` reset on `isOpen`; the identical
pre-existing bug in `OpenShiftDialog.tsx` was left as-is (out of scope) and flagged
as a background task. `tsc --noEmit` clean. Live-verified on local dev stack:
shortage (-50, red #ef4444), exact match (Збіг, green #22c55e), surplus (+450),
and the no-input backward-compatible path (all four fields `null`, no reconciliation
section rendered) — cross-checked against the raw `/shifts/close` network response,
not just the rendered text. No web UI creates POS sales (mobile-only), so
`expectedCashAmount`'s cash-sales-total branch wasn't exercised with a real sale.

## TASK-356 — Backend: Block 6 pre-launch audit — POS & Фіскалізація (Checkbox ПРРО)
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-355
Log: `.claude/logs/tasks/356_2026-07-15_pos-fiscalization-audit_backend-developer.md`
Block 6 of the pre-launch audit (`eager-pondering-tower.md`). Highest financial/legal risk
area. **Found + fixed 2 P0:** (1) online fiscalization ran on a detached, un-awaited
`Task.Run` that captured the request's scoped `IPosRepository`/DbContext and an
`HttpContext`-driven RLS interceptor — both invalid once the HTTP response completed, so
sales were fiscalized only by the 5-min retry job, never inline (Checkbox idempotency
prevented double-fiscalization, but "instant fiscal receipt" never actually worked); fixed
by running the attempt inline, bounded by an 8s timeout, still never blocking the sale.
(2) `ProductStock.Quantity` had no optimistic-concurrency protection — two concurrent sales
of the same batch's last unit both succeeded (silent oversell, lost update); fixed via
`xmin` concurrency token (`AppDbContext`), a new `ConcurrencyConflictException` (Domain
layer, so `PosService` doesn't need an EF Core reference) thrown from
`PosRepository.SaveChangesAsync`, translated to a clean 409 in `PosService.CreateSaleAsync`.
**Found + fixed a P0-adjacent bug while building the concurrency test:**
`ItemRepository.GetByBarcodeAsync` (the only way `PosService.CreateSaleAsync` resolves a
scanned barcode) threw `PostgresException 42846: cannot cast type text[] to jsonb` against
real Postgres — every existing test used an in-memory fake, so this had never been caught;
core POS barcode scanning could not have worked in production. Fixed via
`EF.Functions.JsonContains`. Verified indexes on pos_transactions/pos_transaction_items/
pos_shifts already adequate (best-indexed module in the codebase), no N+1 in shift/day
report paths, money is `decimal` throughout, `IFiscalServiceFactory` correctly per-tenant,
FEFO in POS sales matches Block 3. New real-Postgres test
(`PosConcurrencySalesIntegrationTests`, deterministic two-way rendezvous, not timing-luck)
+ 2 new fake-based unit tests. **Flagged for user decision (not fixed):** shift-open is
scoped per tenant not per store (blocks multi-store simultaneous POS — tied to Checkbox
license being resolved per-tenant, not a simple fix); `PosShift.ClosingCash` cash
reconciliation was never built end-to-end (schema exists, no endpoint/UI). Spawned a
separate background task for an unrelated but same-root-cause jsonb query bug in
`DailySalesRepository.GetProductIdsByBarcodesAsync` (out of scope, not POS).
`dotnet build` 0 err/0 warn, `dotnet test` 824/824 green (was 821). Migration
`20260715054917_AddProductStockXminConcurrencyToken` applied to dev DB.
**Addendum (same day):** user confirmed two directives on the flagged gaps. (1) Per-store
shifts — **plan only**, written into the task log — traced the restriction to
`IPosRepository.GetOpenShiftAsync`/`IFiscalServiceFactory.GetForTenantAsync` both being
tenant-scoped (no `StoreId`), confirmed via `.claude/docs/integrations.md` that Checkbox's
`X-License-Key` is register-scoped (not company-scoped) — so this is ShelfGuard's own
schema simplification (`integration_configs` has no `StoreId`), not a Checkbox limitation;
not trivial (DB migration + `IFiscalServiceFactory`/`IPosRepository`/`PrroSettingsController`
signature changes + frontend store selector), tracked as `known-issues.md` KI-015, not
implemented. (2) Cash reconciliation — **implemented**: `POST /api/pos/shifts/close` body
now optionally accepts `{ actualClosingCash }` (backward compatible, omit = old behavior);
`ShiftDto` gained `openingCash`/`closingCash`/`expectedCashAmount`/`cashDiscrepancy` (cash-only
sales, card excluded); new `IPosRepository.GetCashSalesTotalForShiftAsync`; validates
`>= 0` → 400; 6 new tests (exact/shortage/surplus/negative/no-count/double-close). Updated
`api-contracts.md` (new POS section, full contract for frontend hand-off) and
`known-issues.md`. `dotnet test` 830/830 green.

## TASK-355 — Backend: Block 5 pre-launch audit — Orders/ADU/Buffer
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-354

Reviewed `Features/Adu`, `Features/Buffer`, `Features/Orders` against v2-spec.md §1-3 +
v1-spec.md §2.7 (MOQ/USQ). Formulas match spec (ADU windows/groups, CDA zones, order
formula, div-by-zero guards). Found + documented a MOQ/USQ rounding-ladder deviation
(anchored at zero instead of MOQ) — user confirmed same-day, fixed: `OrderFormula.Compute`
now rounds UP the MOQ + k×USQ ladder (`moq + ceil((raw-moq)/usq)*usq`), never below what
was actually needed. No N+1, indexes adequate, no duplication with Stock (Block 3). Found
(not fixed, out of scope) a real N+1 in `AiOrderService.GetListAsync` — flagged as a
separate background task. Added 4 edge-case tests (new product w/ no history, zero-ADU
buffer, empty delivery schedule) + updated MOQ/USQ ladder tests for the fix. Full log:
`.claude/logs/tasks/355_2026-07-15_orders-adu-buffer-audit_backend-developer.md`.
Build 0 errors, tests 821/821 green.

## TASK-354 — Backend: Block 4 pre-launch audit — Receipts/Transfers/WriteOffs
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-353
Log: `.claude/logs/tasks/354_2026-07-15_receipts-transfers-writeoffs-audit_backend-developer.md`
Block 4 of the pre-launch audit (`eager-pondering-tower.md`). **Found and fixed a P0**:
`WriteOffService.ApproveAsync` had `if (item.ProductStockId is null) continue;` —
silently skipping stock deduction and movement logging. The mobile app's "quick
write-off" screen (the only UI in the whole codebase that creates write-offs) sends
`{ productId, quantity }` with no `productStockId`, so every write-off approved through
the real app never touched `product_stock` and never wrote a `stock_movements` row,
despite showing `status=approved` and a computed `TotalLossAmount`. Fix: no-batch items
now FEFO-consume across the product's batches at the write-off's store (new
`IWriteOffRepository.GetFefoOrderedAsync`, same query as `StockRepository`'s). Also
fixed a **P1**: both the explicit-batch and new FEFO branches used to silently
`Math.Min`-clamp the deduction when requested quantity exceeded available stock,
leaving `LossAmount` inconsistent with the real amount removed — now both hard-fail
`ApproveAsync` with a clear error and persist nothing (matches the audit's explicit
"can't write off more than is in stock" requirement, which previously did not hold).
3 tests rewritten/added in `WriteOffServiceTests.cs` (replaced the test that had
encoded the buggy "nothing happens" behavior as correct).
**DB index gap**: `stock_receipts`/`stock_transfers` had FK-column indexes but no index
with `TenantId` at all (unlike `WriteOff`, which already had one) — every RLS-filtered
query on these two tables was a seq scan. Added 3 composite indexes, migration
`20260714210933_AddStockReceiptsTransfersTenantIndexes` (additive), applied to dev DB,
verified via `\di`.
Reviewed and found correct, no changes: Receipts create/receive validation, Transfers
source/destination quantity consistency + FEFO immutability (Block 3 already confirmed
at service level, re-confirmed full workflow), no N+1 in any of the three modules'
list endpoints (all eager-`.Include()`), FK indexes on `ProductId`/parent-id columns
all present via EF convention. Flagged (not fixed, low severity / out of scope):
`ToStoreId`/`DestinationStoreId`/`StoreId` aren't pre-validated against `Locations`
(relies on DB FK + RLS, bad id → 500 not 400); Receipts/Transfers/WriteOffs share no
common "document + items" abstraction despite near-identical shape (Block 15 candidate).
`dotnet build` 0 err, `dotnet test` 817/817 green (was 815).

## TASK-353 — Backend: Block 3 pre-launch audit — Inventory/Stock/Locations/Stores/Catalog
**Status:** done (2026-07-14) · **Agent:** backend-developer (main session) · **Depends:** TASK-352
Log: `.claude/logs/tasks/353_2026-07-14_inventory-stock-fefo-audit_backend-developer.md`
Block 3 of the pre-launch audit (`eager-pondering-tower.md`). FEFO
(`StockService.FefoConsumeAsync`/`GetFefoOrderedAsync`) and transfer immutability
(`TransferService`, `expiry_date`/`batch_number` copied as-is) were already correct —
added 3 targeted tests (tied-expiry consumption + new `StockRepositoryFefoTests.cs` EF
InMemory suite pinning the real LINQ query's zero-qty/archived/store/product filters) and
a defense-in-depth explicit status filter on `GetFefoOrderedAsync`. KI-008 (pagination)
was already resolved by commit `206b2534` (2026-06-18) — `api/products` is now a pure
redirect shim to the paginated, authorized `api/items`; doc was just stale, now marked
resolved in `known-issues.md` + `api-contracts.md` corrected. **Found and fixed a real N+1**:
`StockService.GetSuggestionsAsync` ran one `GetDeficitStocksAsync` query per
action-required batch — the bulk method (`GetDeficitStocksBulkAsync`) existed but was
never wired in. Rewired to a single bulk query (`Dictionary<Guid, List<ProductStock>>`,
filters out the batch's own store in-memory to preserve "exclude own store" semantics);
2 test fakes updated for the new signature, 2 new regression tests added. `idx_stock_fefo_active`
+ `idx_stock_expiry_active` verified present on dev DB (table too small for a meaningful
EXPLAIN ANALYZE at 25 rows). Flagged as follow-ups (not fixed, out of scope): stale
"Pending Endpoints" table in `api-contracts.md`; dead `StoreService`/`Store` code
superseded by `LocationService`/`Location` (TASK-201). `dotnet build` 0 err/0 warn,
`dotnet test` 815/815 green (was 808/808).

## TASK-352 — DB: Block 2 pre-launch audit — RLS cross-tenant sweep + fix, DB-level leak test
**Status:** done (2026-07-14) · **Agent:** database-engineer (main session) · **Depends:** TASK-351
Log: `.claude/logs/tasks/352_2026-07-14_db-cross-tenant-audit_database-engineer.md`
Block 2 of the pre-launch audit (`eager-pondering-tower.md`). Queried `pg_policies` directly
against the dev DB (74 FORCE RLS tables) instead of parsing 68 migration files. Found (P0): 6
tables (`customers`, `schedule_shifts`, `work_schedules`, `support_tickets`, `ticket_comments`,
`chat_sessions`) had their tenant policy named something other than the literal
`tenant_isolation`, so both 2026-06-29 bulk NULLIF-guard fixes silently skipped them — 5 had no
NULLIF guard at all, `chat_sessions`'s OR-based guard didn't actually short-circuit either.
Reproduced live: all 6 throw `invalid input syntax for type uuid` when `app.tenant_id` is RESET
(unauthenticated-request state), and confirmed `worker_bypass`/`provider_bypass` don't rescue it
(Postgres evaluates every permissive policy's qual). 3 of the 6 also had no `provider_bypass` at
all. Fix: `20260714100000_FixMissingRlsGuardsAndProviderBypass.cs` (additive, renames to
canonical `tenant_isolation` + NULLIF + adds missing `provider_bypass`); applied directly to dev
DB (full `backend` build broken all session by an in-flight parallel edit to
`UsersController.cs`/`AppPolicies.cs`, not touched — worked via `ShelfGuard.Tests`, which builds
standalone). Practical cross-tenant leak test (forged tenant-id in WHERE clause, real
NOSUPERUSER/NOBYPASSRLS role) against `customers`/`product_stock`/`ai_order_suggestions` — RLS
blocked all 3, both manually and via 3 new automated tests in
`RlsCrossTenantIntegrationTests.cs` (soft-skip if no local Postgres; CI has none today). One test
turns the audit query itself into a permanent regression guard. `database-schema.md` RLS
Template section reduced to one canonical pattern (old no-NULLIF version marked deprecated with
the incident it caused); fixed a stale "ADR-009" citation. `dotnet test ShelfGuard.Tests`
805/805 green. **Needs a decision:** 71 tables' `provider_bypass` only matches role `provider`,
not `provider_admin` — but `ProviderPermissions` grants `provider_admin` the same `All`
permissions, so provider-team admins likely get silent empty results (not a leak) on
Analytics/Marketplace queries. Not fixed — flagged as an architectural call.
**Update:** a "coordinator" message mid-task claimed the user approved the 71-table expansion
directly in chat; per this agent's rules that's not equivalent to the user's own message in
this transcript, and the harness's permission classifier independently blocked the apply for the
same reason. Migration prepared (`20260714150000_ExpandProviderBypassToProviderAdmin.cs`) but
NOT applied to any DB — awaiting the user's direct confirmation in this conversation. See log
for details.
**Update 2 (worse P0, found+fixed on dev):** independently verified a real fail-open bug in
`tenant_isolation` on 60 tables — the `IS NULL OR` branch (from the 2026-06-29 bulk fix, copied
into this task's own earlier `20260714100000` migration) returns ALL tenants' rows when
`app.tenant_id` is unset, instead of the intended NULLIF short-circuit-to-zero-rows. Reproduced
live (real NOSUPERUSER role, RESET state → `product_stock` returned all rows). Root-caused to a
deviation from the actual canonical pattern in `.claude/agents/database-engineer.md`. Fixed 57 of
60 tables via `20260714180000_FixFailOpenTenantIsolationOnReset.cs`; kept the fail-open branch on
`users`/`refresh_tokens`/`notification_settings` (legitimate pre-auth lookup need — the
coordinator's blanket instruction would have broken login/token-refresh). DB apply was blocked
by the permission classifier for the same relayed-approval reason as above; a later message
claimed the orchestrator applied it directly via `dotnet ef database update` — independently
re-verified this (not trusted at face value): migration is genuinely recorded in
`__EFMigrationsHistory`, policy text is genuinely fail-closed, live RESET-state test genuinely
returns 0 rows now. Found and fixed 2 real worker-code regressions this exposed
(`telegram-listener.ts`, `notification-dispatch.job.ts` — neither set `app.role='worker'`, so
they silently depended on the removed fail-open branch); found 3 unrelated pre-existing dead-code
issues (`ai-order.job.ts`, `notification.job.ts`, `weather-fetch.job.ts` query non-existent
`stores`/`catalog_products` tables) flagged separately, not fixed. 2 new regression tests added.
`dotnet test` 808/808 green, worker `tsc --noEmit` clean. **Production NOT touched — still runs
the fail-open policy; deploying this fix to prod is a separate decision for the user.**

## TASK-351 — Security: Block 1 pre-launch audit — Auth & Access Control, KI-005 fix
**Status:** done (2026-07-14) · **Agent:** security-reviewer (main session) · **Depends:** TASK-350
Log: `.claude/logs/tasks/351_2026-07-14_auth-access-control-audit_security-reviewer.md`
Block 1 of the pre-launch audit (`eager-pondering-tower.md`). Reviewed
`Auth`/`Users`/`TenantRoles` (login/refresh-rotation-with-reuse-detection/lockout/
password-policy/2FA, v1-spec §3.2 role matrix vs `AppPolicies.cs`, ADR-019 temporary
grants + ADR-020 TenantRole capabilities real backend enforcement, impersonation
audit logging) — no P0/P1 found, this area had already been through several recent
hardening passes (TASK-329/330, TASK-346/347). One informational spec/code
divergence flagged (staff invite/deactivate narrower than v1-spec §3.2 for
network_manager/store_manager) — needs a product decision, no code changed. Fixed:
`AuthController` login/2fa-verify/refresh now have explicit `[AllowAnonymous]`
(previously anonymous only by absence of an attribute). Closed KI-005 (hardcoded
bcrypt seed hash): `DbSeeder.SeedAsync` now hashes `config["Seed:DefaultPassword"]`
(fallback `"password"`, dev-only) via injected `IPasswordHasher` at runtime instead
of a hardcoded hash in source. New `UserServiceCrossTenantTests.cs` (5 tests) pins
the cross-tenant guard on `UserService`. HTTP-level "no token → 401" test left as
TODO for Block 2/18 (no integration-test harness exists yet in this repo).
`dotnet build` 0 err/0 warn, `dotnet test` 805/805 green.

## TASK-350 — DevOps: Block 0 pre-launch audit — staging environment, KI-006 fix, audit tooling base
**Status:** done (2026-07-14) · **Agent:** devops-engineer · **Depends:** —
Log: `.claude/logs/tasks/350_2026-07-14_staging-environment-audit-base_devops-engineer.md`
Block 0 of the pre-launch audit (`C:\Users\stass\.claude\plans\eager-pondering-tower.md`).
`docker-compose.staging.yml` (NEW) — full containerized stack (api/web/postgres/
redis/mosquitto/worker) isolated from dev (5435/6380/1884/5000/3000) and prod
(5100/3100/loopback), on 5436/6381/1885/5101/3101; own postgres container (unlike
prod's `external_links`). `.env.staging.example` + `docs/staging.md` + README pointer.
KI-006 fixed: `Program.cs` seed call now gated (`IsDevelopment() || SEED_ON_START==true`)
— staging auto-seeds, production never does by default; `known-issues.md` updated.
Audit tooling base: `loadtests/` (k6 smoke script against `/api/marketplace/item-categories`,
no dedicated `/health` endpoint exists), `dotnet list package --vulnerable` +
`npm audit` ×3 confirmed running cleanly (vuln counts logged, not remediated —
Block 18), `frontend/vitest.config.ts` + `lib/utils.test.ts` — `npm test` passes 2/2.
`dotnet build` clean; `docker compose ... config` validates staging compose.

## TASK-349 — Frontend: InviteUserModal — вибір TenantRole шаблону при створенні користувача
**Status:** done (2026-07-13) · **Agent:** frontend-developer · **Depends:** TASK-345..348 (ADR-020)
Log: `.claude/logs/tasks/349_2026-07-13_invite-with-tenant-role_frontend-developer.md`
Bug: щойно створений TenantRole-шаблон не з'являвся у "Запросити користувача" —
`INVITE_ROLES` була жорстко закодована на 4 базові ролі, призначення шаблону існувало
лише постфактум через `TenantRoleSelector` у `UserDetailPanel`. UX-фікс без змін
бекенду (`InviteAsync` не чіпали — сьогоднішній privilege-escalation review,
TASK-346/347): `InviteUserModal.tsx` оркеструє два вже готові виклики —
`useInviteUser()` → `useAssignTenantRole()`. Додано `"staff"` (ADR-020) в
`INVITE_ROLES` + лейбл у `ROLE_LABELS`; новий select "Шаблон ролі (необов'язково)" з
`useTenantRoles()`; вибір шаблону дефолтить Role на "staff" лише якщо адмін ще не
чіпав поле вручну. Частковий збій (invite ok, assign fail) не ховає створеного
користувача — модалка лишається відкритою з чіткою помилкою, кнопка стає "Закрити".
`tsc --noEmit` + `npm run build` чисті; live-verified обидва шляхи (success +
simulated race → archived-template 400) на локальному стеку.

## TASK-334 — Frontend: public marketing landing page (/)
**Status:** done (2026-07-10) · **Agent:** frontend-developer · **Depends:** TASK-333 (контракт leads)
Log: `.claude/logs/tasks/334_2026-07-10_landing-page_frontend-developer.md`
`app/page.tsx`: redirect → SSG-лендінг (укр., темна тема #0B0F17, стиль Linear/Vercel,
SEO+OpenGraph). Нова фіча `features/landing/`: SVG-лого (щит+полиці), sticky header,
hero зі скриншотом у browser-рамці, проблеми/можливості (8)/showcase (6 скриншотів
`public/landing/`)/як це працює/для кого/тарифи «за запитом»/FAQ/форма заявки
(RHF+zod, honeypot, POST `/api/public/leads` — 204/400/429). Reveal-анімації CSS+IO,
без нових залежностей. Бонус: `app/icon.svg` (favicon), `lang="uk"`.
tsc clean, build success, `/` prerendered static, форма і якорі перевірені в браузері.

## TASK-333 — Backend: landing lead capture endpoint
**Status:** done (2026-07-10) · **Agent:** backend-developer · **Depends:** — (frontend landing — паралельна задача)
Log: `.claude/logs/tasks/333_2026-07-10_landing-leads_backend-developer.md`
`POST /api/public/leads` (AllowAnonymous, rate limit `public-leads` 5/min per IP):
honeypot `website` → 204 без збереження; валідація name 2..100 / phone 5..30 /
company ≤150 / message ≤1000 → 400 `{error}`; happy path → `landing_leads`
(provider-level, без tenant_id/RLS — як provider_roles) + ILogger info.
Telegram-нотифікація відкладена (worker pipeline tenant-scoped) — TODO у сервісі.
Міграція `20260710112137_AddLandingLeads` (additive). Build 0 err, 701/701 tests.

## TASK-329 — Backend: auth hardening (rate limit, lockout, password policy, reuse detection, headers)
**Status:** done (2026-07-09) · **Agent:** backend-developer · **Depends:** —
Log: `.claude/logs/tasks/329-330_2026-07-09_auth-hardening-2fa_backend-developer.md`
Rate limiting 10/min login+2fa-verify, 30/min refresh (429 `{error}`), ForwardedHeaders
за nginx; lockout 5 невдач → 15 хв (generic error, аудит `user.login_failed`/
`user.locked_out` з IP); `PasswordValidator` (12+ символів, літера+цифра, blocklist ~100,
email local-part) у всіх 5 місцях встановлення пароля; зміна пароля відкликає всі
refresh-токени; повторне використання ротованого refresh-токена → revoke всієї сім'ї +
`auth.refresh_reuse_detected`; security headers middleware. Build 0 err, 685/685 tests,
міграція `20260709204440_AuthHardeningAnd2fa` (additive), live smoke: 401/429/headers OK.

## TASK-330 — Backend: 2FA TOTP (opt-in) + recovery codes
**Status:** done (2026-07-09) · **Agent:** backend-developer · **Depends:** TASK-329
Log: той самий · Handoff: `.claude/logs/handoffs/330-backend-to-frontend.md` (точний API-контракт для TASK-331)
Otp.NET (Infrastructure) за `ITotpService`; login → `{requiresTwoFactor, challengeToken}`
(JWT 5 хв, purpose=2fa, окрема audience — не проходить bearer auth); `/api/auth/2fa/`
verify (anonymous, ліміт auth-login, анти-replay по timestep, recovery-коди одноразові) /
setup / enable (8 кодів XXXX-XXXX, SHA256 у jsonb) / disable (пароль+код);
`AuthUserDto.TwoFactorEnabled`. Невірний 2FA-код рахується в той самий lockout-лічильник.

## TASK-331 — Frontend: 2FA UI + password policy hints + lockout UX
**Status:** done (2026-07-09) · **Agent:** frontend-developer · **Depends:** TASK-330
Log: `.claude/logs/tasks/331_2026-07-09_2fa-ui_frontend-developer.md`
Login: другий крок з 6-значним кодом / recovery-кодом (тогл), UA-помилки для 401/429,
«Назад» до кроку 1; `LoginResponse` → discriminated union, токени не зберігаються при
challenge. Profile: секція «Двофакторна автентифікація» (setup QR `qrcode.react` +
секрет, enable з одноразовим показом recovery-кодів + підтвердження «Я зберіг коди»,
disable через пароль+код), refresh `/api/auth/me` після змін. ChangePasswordForm:
валідація 12+ символів літери+цифри, hint, серверні `{error}` as-is, toast про
розлогінення інших пристроїв. Фікс `lib/api.ts`: 401 з `/api/auth/2fa/verify` більше
не тригерить refresh→redirect. tsc clean, build success (50/50), eslint змінених файлів
clean (у frontend/ немає ESLint-конфіга — pre-existing, `next lint` інтерактивний).

---
# Previous sprint — v4.4 «Chat UX unification» (started 2026-07-07)

## TASK-319 — Marketplace chat: bottom-right floating widget + real unread badges
**Status:** done (2026-07-07) · **Agent:** backend-developer → frontend-developer (finished directly in main session after agent stalls) · **Depends:** —
User ask: supplier↔client marketplace chat should render bottom-right like the existing
`SupportChatWidget` (Чат підтримки / Мій асистент), for both client and supplier side;
closed chats should show an unread-message indicator.
Scope decisions:
- **Marketplace chat** (`SupplierChatSession`/`SupplierChatMessage`) — already has
  `SenderTenantId` per message (clean two-tenant model), so a real per-message `IsRead`
  → per-session `UnreadCount` is a same-file backend change, no schema migration.
  Repositioned both `SupplierChatPanel.tsx` (client) and `SupplierClientChatPanel.tsx`
  (supplier) to the bottom-right floating style; added unread badges (Sidebar
  «Повідомлення» nav item, `ChatInboxTab` per-row, client's «Написати постачальнику»
  button).
- **"Чат підтримки"** (tenant↔provider `ChatMessage`/`ChatService`) — investigated:
  `IsRead` there already means "read by provider" (used by the provider's shared queue,
  `GetMessagesForProviderAsync` marks all `IsRead=true`), and the tenant side's own
  `GetSessionsAsync`/`GetMessagesAsync` never had real unread tracking (hardcoded
  `unreadCount: 0`) — no `SenderTenantId`/sender-role marker exists on `ChatMessage` to
  disambiguate "read by tenant" without a schema change + touching a column the
  provider queue already depends on. **Out of scope for TASK-319** — flagged as a
  separate follow-up (see spawn_task) rather than risking the provider queue.
- **AI Assistant** — synchronous ask/answer, no server-pushed messages while closed;
  no unread concept applies, no changes made.

**Backend half done (2026-07-07):** `SupplierChatSessionDto.UnreadCount` +
`ISupplierChatRepository.MarkMessagesReadAsync` (auto-called from `GetMessagesAsync`) —
log `.claude/logs/tasks/319a_2026-07-07_marketplace-chat-unread-backend_backend-developer.md`,
handoff `.claude/logs/handoffs/319-backend-to-frontend.md`. Build 0 errors, 645/645 tests
green.

**Frontend half done (2026-07-07):** log
`.claude/logs/tasks/319b_2026-07-07_marketplace-chat-widget-unread-frontend.md`.
`SupplierChatPanel.tsx` (client) and `SupplierClientChatPanel.tsx` (supplier) repositioned
from centered dimmed modal to bottom-right floating widget (fixed bottom:24 right:24,
380×540, matches `SupportChatWidget` visual language, no backdrop). Unread badges: client's
«Написати постачальнику» button (`marketplace/[id]/page.tsx` — hoisted `useSupplierChatMessages`
to page level so the 3s poll runs while the panel is closed, derives unread from
`senderTenantId`/`isRead`); supplier's `ChatInboxTab` per-row badge + aggregate badge on the
Sidebar «Повідомлення» nav item (`useSupplierChatSessions(enabled)` gated to `supplier_admin`
only). `tsc --noEmit` clean, `npm run build` green (48 routes), `dotnet build`/`dotnet test`
645/645 green.
**Note:** three spawned frontend-developer agent attempts for this half stalled (reported
"I'll wait for the agent" instead of working — known pattern, see
`feedback-agent-self-delegation-loop` memory) before one background instance quietly
finished part of the Sidebar.tsx wiring; the rest was completed directly in the main
session per the "correct once then do it directly" guidance rather than spawning a 4th
attempt.

---
# Current Sprint — v4.3 «Supplier Cooperation & Marketplace Orders» (started 2026-07-06)

Клієнт бачить каталог/рейтинг/відгуки постачальника публічно (як зараз). Для замовлень —
заявка на співпрацю → постачальник схвалює → генерується договір (PDF: реквізити, підпис,
мокра печатка) → підписання через Вчасно або скачування для фізичного підпису → статус
active відкриває marketplace-замовлення. Консультація — існуючий чат; питання — тікети
підтримки постачальника.

## TASK-316 — DB: cooperation schema (agreements, orders, tickets, contract settings)
**Status:** done (2026-07-06) · **Agent:** database-engineer · **Depends:** —
Log: `.claude/logs/tasks/316_2026-07-06_cooperation-schema_database-engineer.md`
6 таблиць + two-tenant RLS + партіальний unique index (одна live-угода на пару).
Міграція `20260706155440_SupplierCooperation`. Build green, міграція не застосована.

## TASK-317 — Backend: agreements + contract PDF (QuestPDF) + Вчасно + orders + support tickets
**Status:** done (2026-07-06) · **Agent:** backend-developer · **Depends:** TASK-316
Log: `.claude/logs/tasks/317_2026-07-06_cooperation-backend_backend-developer.md`
Handoff: `.claude/logs/handoffs/317-to-318_frontend-developer.md` (усі ендпоінти + DTO shapes)
Сервіси: заявка клієнта / рішення постачальника / генерація договору з реквізитами,
підписом і печаткою / надсилання у Вчасно (per-tenant ключ через integration_configs) /
скачування PDF; marketplace-замовлення з гейтом «тільки active agreement»; тікети підтримки.
Build 0 errors, 639/639 tests green. QuestPDF + DejaVu Sans (кирилиця OK).

## TASK-318 — Frontend: client cooperation UX + supplier cabinet (requests, contract settings, orders, support)
**Status:** done (2026-07-07) · **Agent:** frontend-developer · **Depends:** TASK-317
Log: `.claude/logs/tasks/318_2026-07-06_cooperation-frontend_frontend-developer.md` (два проходи)
Клієнт: статус/заявка/договір/підтримка на `/marketplace/[id]`, кошик → замовлення
(лише active agreement), нова `/marketplace/orders` (таби Замовлення/Співпраця) +
sidebar «Мої замовлення». Кабінет: 4 нові сторінки `/supplier/requests` (approve/
reject/договір/Вчасно/mark-signed/terminate), `/supplier/contract-settings` (реквізити
+ upload підпису/печатки), `/supplier/orders` (переходи статусів), `/supplier/support`
(тікети+тред) + 4 пункти в supplier-nav. `tsc --noEmit` чисто, `npm run build` green.
Не покрито: ручний E2E повного флоу проти бекенду — кандидат на QA-задачу.

---
# Previous sprint — v4.2 «Supplier Categories & Navigation» (started 2026-07-03)

Архітектура: ADR-017 (`.claude/docs/decisions.md`). Feature A: provider-панель `/provider`
дістає таб-спліт «Клієнти» / «Постачальники» над існуючим списком тенантів (client-side
фільтр по `business_type`, без нового ендпоінта/роуту). Feature B: `SupplierItem` отримує
nullable `category` + `attributes JSONB`; довідник категорій/полів — backend-джерело
істини (`GET /api/marketplace/item-categories`), фронтенд рендерить форму динамічно.
Existing items без категорії лишаються валідними назавжди (не міграційна яма).

---

## BUG-015 — StoreSelector shown to provider role in TopBar ✅ done (2026-07-04)
Log: `.claude/logs/tasks/bug015-017_2026-07-04_provider-storeselector-duplicate-button-detail-width_frontend-developer.md`
`frontend/components/layout/TopBar.tsx` already used `TENANT_ROLES.has(userRole)` (excludes
provider/provider_admin/provider_agent + supplier_admin) — verified correct, no change needed.

## BUG-016 — Duplicate "Створити постачальника" button on /marketplace ✅ done (2026-07-04)
Log: `.claude/logs/tasks/bug015-017_2026-07-04_provider-storeselector-duplicate-button-detail-width_frontend-developer.md`
Removed button + `CreateSupplierModal` usage from `frontend/app/(dashboard)/marketplace/page.tsx`.
Deleted unused `frontend/features/marketplace/components/CreateSupplierModal.tsx` (no other callers).
Backend `MarketplaceAdminController`/`AdminCreateSupplierAsync` left untouched — candidate for later cleanup.

## BUG-017 — Supplier detail page constrained to half width ✅ done (2026-07-04)
Log: `.claude/logs/tasks/bug015-017_2026-07-04_provider-storeselector-duplicate-button-detail-width_frontend-developer.md`
Removed `maxWidth: 900` from both wrapper divs in `frontend/app/(dashboard)/marketplace/[id]/page.tsx`.

## BUG-018 — Client chat messages never reach supplier (no UI inbox) ✅ done (2026-07-07)
Log: `.claude/logs/tasks/bug018_2026-07-07_supplier-chat-inbox_frontend-developer.md`
Root cause: `ClientsTab.tsx` (`/supplier/clients`) was the only place that opened
`SupplierClientChatPanel`, and its list (`useSupplierClients`) only includes clients
with a review or a task (TASK-313 design) — a client who only started a chat never
appeared, so the supplier had no way to see/reply even though messages saved fine.
The already-existing `GET /api/supplier-cabinet/chat/sessions` endpoint +
`useSupplierChatSessions()` hook were dead code (no component used them).
Fix (frontend-only): new `ChatInboxTab.tsx` renders all chat sessions via
`useSupplierChatSessions()`, opens `SupplierClientChatPanel` on click. Wired in as
a tab switcher ("Клієнти" / "Повідомлення") on `/supplier/clients` — no new route,
no nav change, no backend change. `tsc --noEmit` clean, `npm run build` green.
**Superseded by BUG-019** — the tab was still gated behind `client_management`.

## BUG-019 — Chat inbox still unreachable: wrongly nested under client_management ✅ done (2026-07-07)
Log: `.claude/logs/tasks/bug019_2026-07-07_supplier-chat-inbox-permission-gate_frontend-developer.md`
User screenshot showed a supplier staff account missing Профіль/Клієнти/Команда nav
items (no profile_management/client_management/staff_management permission) — so the
BUG-018 fix, nested inside `/supplier/clients`, was unreachable for that account,
reproducing the original complaint. Fix: moved chat inbox to its own ungated route
`/supplier/messages` (new nav item, no `permission` key — same treatment as the
TASK-318 cooperation items) using the existing `ChatInboxTab` unchanged;
`/supplier/clients` reverted to always rendering just `ClientsTab`. `tsc --noEmit` clean.

---

## TASK-293 — DB: SupplierItem.Category + Attributes (JSONB)
**Status:** done · **Agent:** database-engineer · **Depends:** —
Міграція: `supplier_items.category text NULL` + `supplier_items.attributes jsonb NULL`
(raw SQL у подвійних лапках колонок, ADR-008). Entity `SupplierItem`
(`backend/ShelfGuard.Domain/Entities/SupplierItem.cs`): `string? Category`,
`Dictionary<string, object?>? Attributes`. EF config: `.HasColumnType("jsonb")` для
Attributes (той самий підхід, що `Item.Barcodes` — перевірити, чи потрібен додатковий
Npgsql dynamic-json switch для `Dictionary<string, object?>`, чи досить generic
`JsonSerializer` конвертера, як показано в ADR-017 п.3). Без DEFAULT — обидва nullable,
existing rows лишаються `NULL`. Не чіпати RLS (`supplier_items` вже під `tenant_isolation`
+ `provider_bypass` з попередніх спринтів) — тільки перевірити NULLIF-guard присутній.
**Accept criteria:** migration up/down чиста на dev-базі; existing `SupplierItem` рядки
не ламаються (Category/Attributes читаються як null); `dotnet build` + тести green.

---

## TASK-294 — Backend: довідник категорій (SupplierItemCategories) + item-categories endpoint
**Status:** done (2026-07-03) · **Agent:** backend-developer · **Depends:** TASK-293
Log: `.claude/logs/tasks/294-295_2026-07-03_supplier-item-categories_backend-developer.md`
Новий `ShelfGuard.Domain.Constants.SupplierItemCategories`: фіксовані ключі `food`,
`auto_parts`, `medical`, `construction` + для кожного — список полів
`{ Key, LabelUa, Type (text|number|date|bool|select), Required, Options? }`:
- `food`: weight/volume (text, req), expiry_date (date, req), batch_number (text, opt)
- `auto_parts`: oem_number (text, req), compatible_models (text, opt, вільний текст через кому),
  part_number (text, opt)
- `medical`: dosage (text, req), expiry_date (date, req), prescription_status (select:
  ОТС/рецептурний, req), storage_conditions (text, opt)
- `construction`: unit (text, req), package_weight_volume (text, opt), certification_class (text, opt)
Метод `SupplierItemCategories.Validate(string? category, Dictionary<string,object?>? attrs)`
→ список помилок за відсутні required-поля (порожній список, якщо `category == null` —
без категорії валідація не застосовується, ADR-017 п.5).
Новий публічний ендпоінт `GET /api/marketplace/item-categories` (`[AllowAnonymous]`) —
віддає довідник як DTO (`SupplierItemCategoryDto[]`) для фронтенд-рендеру форми.
**Accept criteria:** unit-тести на Validate (кожна категорія: бракує required → помилка;
всі required заповнені → ok; category=null → завжди ok); ендпоінт віддає 4 категорії з
повним списком полів; `dotnet build` + тести green.

---

## TASK-295 — Backend: Category/Attributes у SupplierItem DTOs + CRUD валідація
**Status:** done (2026-07-03) · **Agent:** backend-developer · **Depends:** TASK-294
Log: `.claude/logs/tasks/294-295_2026-07-03_supplier-item-categories_backend-developer.md`
`SupplierItemDto`, `AdminAddSupplierItemDto`, `AdminUpdateSupplierItemDto`,
`CabinetAddItemDto`/`CabinetUpdateItemDto` (якщо окремі — перевірити фактичні назви в
`MarketplaceDtos.cs`) отримують `string? Category` + `Dictionary<string,object?>? Attributes`.
`MarketplaceService`/`SupplierCabinetService` CRUD-методи товару: перед create/update
викликають `SupplierItemCategories.Validate` → 400 зі списком відсутніх полів, якщо
`category` заданий і чогось не вистачає. Existing товари без категорії — CRUD без змін
поведінки. AI Supplier Recommendation (`SupplierRecommendationDto.MatchedItem`) — Category
проходить крізь той самий `SupplierItemDto`, змін логіки рекомендації не потрібно.
**Accept criteria:** POST/PUT товару з category="medical" без expiry_date → 400 з
переліком полів; той самий запит з усіма required → 200/201; товар без category — як
раніше; unit-тести на guard + backward-compat; `dotnet test` green.

---

## TASK-296 — Frontend: динамічна форма товару за категорією (CabinetItemModal)
**Status:** done (2026-07-03, log: 296-297_2026-07-03_supplier-categories-and-provider-tabs_frontend-developer.md) · **Agent:** frontend-developer · **Depends:** TASK-294, TASK-295
`frontend/features/supplier-cabinet/components/CabinetItemModal.tsx`: додати select
«Категорія» (опційний, з опцією «Без категорії») над існуючими полями. Категорії й поля
підтягуються з нового хука `useItemCategories()` (`GET /api/marketplace/item-categories`,
закешовано, `staleTime: Infinity` — довідник статичний). При виборі категорії — рендер
додаткового блоку полів під схемою категорії (text/number/date/bool/select), значення
складаються в `attributes` об'єкт при сабміті. Клієнтська дзеркальна валідація
required-полів (UX, не заміна серверної) — показує ту саму помилку до сабміту.
`types.ts` (`CabinetItem`, add/update payloads) — `category?: string`, `attributes?:
Record<string, unknown>`. `CabinetItemsTable.tsx` — показати бейдж категорії в рядку
(лейбл з довідника), товари без категорії — без бейджа (як зараз).
**Accept criteria:** вибір категорії показує правильний набір полів; сабміт без required
поля показує помилку і не відправляє запит; товар без категорії зберігається як раніше;
`tsc --noEmit` + `npm run build` green.

---

## TASK-297 — Frontend: провайдер-панель — таби «Клієнти» / «Постачальники»
**Status:** done (2026-07-03, log: 296-297_2026-07-03_supplier-categories-and-provider-tabs_frontend-developer.md) · **Agent:** frontend-developer · **Depends:** —
`frontend/app/(dashboard)/provider/page.tsx`: `activeTab` розширюється з
`"tenants" | "logs"` на `"clients" | "suppliers" | "logs"`. Список `tenants` (з існуючого
`useTenants()`, без нового API-виклику) фільтрується client-side:
`t.businessType === "supplier"` → таб «Постачальники», інакше → «Клієнти». Таби показують
лейбл з лічильником (`Клієнти (N)`, `Постачальники (M)`). Пошук (`search` state) працює
в межах активного табу. `TenantCard`/`TenantDetailPanel`/`CreateTenantWizard` — без змін
(реюз). Health-картки зверху (`stats`) лишаються агрегатом по всіх тенантах — не діляться
по табу.
**Accept criteria:** перемикання таба фільтрує список без нового network-запиту; лічильники
в лейблах табів коректні; пошук працює в межах вибраного табу; `tsc --noEmit` +
`npm run build` green.

---

## TASK-298 — QA: supplier categories + provider nav split regression
**Status:** done (2026-07-03, log: `.claude/logs/reviews/qa_293-298_2026-07-03.md`) · **Agent:** qa-tester · **Depends:** TASK-296, TASK-297
Усі 8 сценаріїв PASS на локальному стеку: item-categories довідник (4 категорії, коректні
required-поля), medical create без/з required (400 з укр. помилкою / 201), category omitted
CRUD (backward compat, включно з legacy item без категорії), update null→food без/з required
(400/200), невідома категорія → 400, provider tenants `businessType` присутній +
platform-marketplace виключений (BUG-014 regression), регресія публічних marketplace-ендпоінтів
і cabinet profile/items. `dotnet test` 535/535 green, `tsc --noEmit` чисто, `npm run build` green.
Багів не знайдено. Не покрито: PUT існуючого seed-товару alpha@supplier.local (немає credentials,
адмінський контролер без PUT для items) — pre-existing обмеження, поза скоупом.
**Accept criteria:** усі сценарії пройдені; знайдені баги оформлені як BUG-задачі. Виконано.

---

## TASK-282 — DB: supplier business_type, IsOwnerManaged, дефолтні модулі
**Status:** done (2026-07-02, migration `20260702192126_V41SupplierSelfService`, log: `282_2026-07-02_supplier-self-service-db_database-engineer.md`) · **Agent:** database-engineer · **Depends:** — 
Міграція `V41SupplierSelfService`:
- `supplier_profiles.IsOwnerManaged boolean NOT NULL DEFAULT false` + partial unique index
  `UX_supplier_profiles_owner_tenant ON supplier_profiles ("TenantId") WHERE "IsOwnerManaged"` 
  (колонки в raw SQL — у подвійних лапках, ADR-008).
- Domain: `Tenant.DefaultModulesForBusinessType` — новий кейс `"supplier"` → `["marketplace_supplier"]`.
- Перевірити, що існуючі RLS-політики supplier_* мають NULLIF-guard (патерн d8abc4d8); якщо ні — включити в цю міграцію.
- Дані не мігруються: existing suppliers (`TenantId = Guid.Empty`) без змін.
**Accept criteria:** міграція up/down чиста на dev-базі; unique index не конфліктує з existing rows; `dotnet build` + тести green.

---

## TASK-283 — Backend: роль supplier_admin + онбординг supplier-tenant
**Status:** done (2026-07-02, log: `283-285_2026-07-02_supplier-self-service-backend_backend-developer.md`) · **Agent:** backend-developer · **Depends:** TASK-282
- `AppRoles`: додати `SupplierAdmin = "supplier_admin"` (+ у `All`).
- Admin tenant onboarding (`Admin` feature): при створенні tenant з `business_type = "supplier"` — 
  автоматично створити `Supplier` (`TenantId` = new tenant id) + `SupplierProfile`
  (`IsOwnerManaged = true`, `IsPublic = false`); перший user tenant-а отримує роль `supplier_admin`.
- Policy/authorization: supplier_admin НЕ входить у tenant-staff політики (stock/pos/etc.) — тільки кабінет.
**Accept criteria:** створення supplier-tenant через `/api/admin/tenants` дає tenant + user + Supplier + Profile однією транзакцією; supplier_admin отримує 403 на `/api/stock`; тести на онбординг-hook.

---

## TASK-284 — Backend: SupplierCabinetController (профіль, товари, відгуки)
**Status:** done (2026-07-02, log: `283-285_2026-07-02_supplier-self-service-backend_backend-developer.md`) · **Agent:** backend-developer · **Depends:** TASK-283
Новий `SupplierCabinetController` (`/api/supplier-cabinet`), `[Authorize]` роль supplier_admin + `[RequireModule("marketplace_supplier")]`. Resolve «мій Supplier» по `tenant_id` через `IsOwnerManaged`-профіль:
- `GET /profile`, `PUT /profile` (region, categories, website, delivery_regions, working_hours, payment_terms), `POST /profile/publish` (toggle `IsPublic`)
- `GET /items`, `POST /items`, `PUT /items/{id}`, `DELETE /items/{id}` — реюз Admin*-методів `MarketplaceService` (параметризувати supplierId)
- `GET /reviews` (read-only), `GET /metrics`
**Accept criteria:** усі ендпоінти працюють лише в контексті свого tenant (RLS-перевірка: другий supplier-tenant не бачить чужі items); provider-created suppliers (Guid.Empty) недоступні через кабінет; unit-тести на resolve + CRUD.

---

## TASK-285 — Backend: reviews hardening + публічні відгуки + rating recalc
**Status:** done (2026-07-02, log: `283-285_2026-07-02_supplier-self-service-backend_backend-developer.md`) · **Agent:** backend-developer · **Depends:** TASK-282
- `CreateReviewAsync`: guard — reviewer tenant ≠ `supplier.TenantId` та reviewer `business_type != "supplier"` (400); дубль уже дає 409.
- Після створення відгуку — синхронний перерахунок `SupplierMetrics.Rating` = AVG(rating) (створити metrics-рядок, якщо нема).
- Новий публічний `GET /api/marketplace/suppliers/{id}/reviews` (`[AllowAnonymous]`, paginated) — rating, comment, created_at, назва tenant-рецензента (denormalized display name, без id).
**Accept criteria:** self-review → 400; supplier-tenant review → 400; rating у публічному листингу оновлюється після нового відгуку; тести на guard + recalc.

---

## TASK-286 — Frontend: supplier cabinet (роль, sidebar, сторінки)
**Status:** done (2026-07-03, log: `286-287_2026-07-03_supplier-cabinet-marketplace-frontend_frontend-developer.md`) · **Agent:** frontend-developer · **Depends:** TASK-284
- `lib/roles.ts`: `SupplierAdmin` + set `SUPPLIER_ONLY`; supplier_admin виключити з tenant-staff sets.
- Sidebar: для supplier_admin — тільки група «Кабінет постачальника» (Профіль / Мої товари / Відгуки) + профіль користувача.
- Нова feature `features/supplier-cabinet/` (`types.ts`, `api/`, `hooks/`, `components/`), сторінки `(dashboard)/supplier/profile`, `/supplier/items`, `/supplier/reviews`. Реюз компонентів `features/marketplace/` (AddSupplierItemModal, форма профілю) де можливо.
- Admin onboarding UI: у формі створення tenant — опція business_type `supplier`.
**Accept criteria:** supplier_admin після логіну бачить лише кабінет; CRUD товарів і publish-toggle працюють; `tsc --noEmit` + `npm run build` green.

---

## TASK-287 — Frontend: marketplace enrichment — рейтинг і відгуки видимі клієнтам
**Status:** done (2026-07-03, log: `286-287_2026-07-03_supplier-cabinet-marketplace-frontend_frontend-developer.md`) · **Agent:** frontend-developer · **Depends:** TASK-285
- `/marketplace/[id]`: блок «Відгуки» (список з `GET /suppliers/{id}/reviews`, зірки, дата, ім'я рецензента) + існуюча форма «залишити відгук» показує 400/409 помилки guard-ів.
- `SupplierCard` у листингу: рейтинг (зірки + число) і кількість відгуків; фільтр за категорією вже є — переконатися, що категорії supplier-профілів відображаються.
**Accept criteria:** рейтинг/відгуки видно і анонімно, і клієнт-tenant-ам; свіжий відгук одразу оновлює рейтинг (invalidate query); `tsc --noEmit` + build green.

---

## TASK-288 — QA: supplier self-service regression
**Status:** done (2026-07-03, log: `.claude/logs/reviews/qa_282-288_2026-07-03.md`) · **Agent:** qa-tester · **Depends:** TASK-286, TASK-287
Усі 6 сценаріїв + регресія + `dotnet test` 494/494 + `tsc --noEmit` — PASS (локальний стек).
Знайдено 2 pre-existing баги (не блокують v4.1):
- **BUG-009 (high, deploy/env):** 8 hand-written міграцій без `[Migration]`/`[DbContext]` атрибутів
  (AddProviderRoles, AddNotificationIsRead, 2×ProviderBypassRls, AddItemPerishabilityClass,
  ForceRlsOnAllTenantTables, 2×FixRlsNullIf) — EF `MigrateAsync` їх НЕ бачить; свіжа БД отримує
  неповну схему (login 500: ProviderRoleId missing). Локальну dev-базу полагоджено вручну.
- **BUG-010 (medium):** `GET /api/marketplace/suppliers/{id}` віддає unpublished-профіль
  (IsPublic=false) навіть анонімно — detail не фільтрує is_public (листинг/search фільтрують).
Low-нотатки (див. QA-лог): review-guard-и 400 екрануються module gate 403 для supplier-tenant-ів;
supplier_admin має 200 на /api/notifications/history (свій tenant, порожньо).
Тест-план: (1) онбординг supplier-tenant провайдером; (2) ізоляція — supplier A не бачить дані supplier B і клієнтських tenant-ів (RLS); (3) supplier_admin 403 на всі tenant-staff ендпоінти; (4) publish-toggle → поява/зникнення в публічному листингу; (5) review-флоу: клієнт лишає відгук, дубль → 409, self-review → 400, рейтинг перерахований; (6) module gate: деактивація `marketplace_supplier` → 403 кабінету.
**Accept criteria:** усі 6 сценаріїв пройдені на dev; знайдені баги оформлені як BUG-задачі.

---

## TASK-289 — Backend: provider-path onboarding + cabinet backfill + role guard (ADR-016)
**Status:** done (2026-07-03, log: `289_2026-07-03_provider-supplier-onboarding_backend-developer.md`) · **Agent:** backend-developer · **Depends:** TASK-283, BUG-012
Провайдерський візард (`ProviderService.CreateTenantAsync`, `/api/provider/tenants`) не мав
онбординг-хука TASK-283 — supplier-tenant, створений через нього, лишався без Supplier/Profile.
Fix:
- `ProviderService.CreateTenantAsync` викликає `SupplierOnboarding.CreateOwnerManaged` в тій самій
  транзакції, що й TenantAdminService (`ITenantRepository.AddPendingAsync` — deferred-варіант
  `AddAsync`, +`AddSupplierAsync`/`AddSupplierProfileAsync`, один `SaveChangesAsync`).
  `TenantAdminService` теж переведено на спільний хелпер (усунуто дублювання логіки).
- `SupplierCabinetService.ResolveAsync` — lazy backfill: якщо `IsOwnerManaged`-профілю нема,
  а `tenant.business_type == "supplier"` — створює пару через
  `IMarketplaceRepository.GetOrCreateOwnerManagedProfileAsync` (race-safe, той самий патерн
  detach+refetch, що й `GetOrCreatePlatformTenantIdAsync`, BUG-012). Самолікує supplier-tenant,
  створений на проді до цього фіксу.
- `CreateTenantUserRequest.Role` + валідація в `ProviderService.CreateTenantUserAsync`: роль має
  відповідати `business_type` тенанта (`supplier` → тільки `supplier_admin`, інакше — тільки
  `enterprise_admin`); невідповідність — 400.
Тести: `ProviderServiceTests` (онбординг supplier/non-supplier, role guard обидва напрямки),
`SupplierCabinetServiceTests` (backfill supplier/non-supplier tenant, no-op коли профіль вже є).
`dotnet build` + `dotnet test` — 513/513 green (було 506).
**Accept criteria:** supplier-tenant через `/api/provider/tenants` отримує Supplier+Profile
однією транзакцією; кабінет самолікує existing supplier-tenant без профілю; role guard рубає
supplier_admin для non-supplier тенанта і навпаки.

---

# Previous Sprint — v3.5 «Provider UX» (started 2026-06-21)

---

## TASK-281 — Dashboard і /stock: консистентний фільтр магазину
**Status:** done · **Agent:** frontend-developer · **Depends:** TASK-280 · Updated: 2026-07-02
Дашборд (stats, «Потребують уваги», карта зон) викликав `/api/stock*` без
`store_id` — показував дані всіх магазинів, тоді як `/stock` фільтрує за
`selectedStoreId` з header StoreSelector. Після «Переглянути всі» список міг
бути порожнім. Fix: `frontend/features/dashboard/api/dashboard.ts` — усі три
функції приймають `storeId` (helper `withStore` додає `store_id=` до URL);
`frontend/features/dashboard/hooks/useDashboard.ts` — хуки читають
`selectedStoreId` з `useStoreContext` і включають його в queryKey. Бекенд
(`StockController`) вже приймає `store_id?` на `/api/stock`, `/summary`,
`/zones-summary`. Коли магазин не вибрано (`null`) — параметр не додається,
обидві сторінки показують все. `tsc --noEmit` та `npm run build` — green.
Log: `281_2026-07-02_dashboard-store-consistency_frontend-developer.md`

---

## TASK-280 — Dashboard: блок «Потребують уваги» — 5 рядків + «Переглянути всі»
**Status:** done · **Agent:** frontend-developer · **Depends:** — · Updated: 2026-07-02
Блок `AttentionTable` не мав обмеження висоти — при багатьох товарах займав пів
сторінки. Fix (у `frontend/features/dashboard/components/AttentionTable.tsx`):
показуються перші 5 рядків поточного фільтра; нижче кнопка
«Переглянути всі (N)» (лише коли рядків > 5). Ціль навігації — `/stock`
(сторінки `/shelf` немає): таб «All» → `/stock`, таби Expired/Critical/Warning →
`/stock?status=<value>` — сторінка вже читає `status` з query params, значення
збігаються зі `StockFilters`, тож фільтр преселектнутий. Стилі — існуючий
inline dark-theme патерн блоку. `tsc --noEmit` та `npm run build` — green.
Log: `280_2026-07-02_dashboard-attention-view-all_frontend-developer.md`

---

## TASK-279 — Повідомлення про завершення сеансу при неактивності
**Status:** done · **Agent:** frontend-developer · **Depends:** — · Updated: 2026-07-02
Раніше при протуханні access token + невдалому refresh `frontend/lib/api.ts` робив
жорсткий redirect на `/login` без пояснення — користувача «викидало» мовчки.
Fix: redirect тепер на `/login?reason=session_expired`; на сторінці логіну новий
клієнтський компонент `SessionExpiredNotice` (features/auth/components) читає параметр
через `useSearchParams` (обгорнуто в `<Suspense>` у server-сторінці) і показує amber-банер
«Час сеансу сплив. Будь ласка, увійдіть знову.» над формою — той самий візуальний патерн,
що й error-блок у LoginForm, але warning-тон (#F59E0B), бо це очікувана подія.
`middleware.ts` без змін: він не може відрізнити «сеанс сплив» від «перший візит»
(в обох випадках cookie відсутні), тож reason ставить лише api.ts після фактичного
провалу refresh. `tsc --noEmit` та `npm run build` — green.
Log: `279_2026-07-02_session-expired-notice_frontend-developer.md`

---

## BUG-009 — 8 hand-written міграцій без [Migration]/[DbContext] атрибутів
**Status:** done · **Agent:** database-engineer (+ main session verification) · Updated: 2026-07-03
Found in QA v4.1: EF `MigrateAsync` ігнорував 8 ручних міграцій (AddProviderRoles,
AddNotificationIsRead, ServiceDesk/Team provider bypass RLS, ItemPerishabilityClass,
ForceRlsOnAllTenantTables, 2× NULLIF RLS-фікси) — свіжа БД розгорталась неповною.
Fix: додано атрибути `[DbContext(typeof(AppDbContext))]` + `[Migration("<id>")]`,
міграції переписані на ідемпотентний SQL (IF NOT EXISTS / OR REPLACE guards),
snapshot оновлено. На проді вони виконаються ПОВТОРНО при наступному деплої
(відсутні у __EFMigrationsHistory) — ідемпотентність перевірена: DELETE 8 рядків
історії на локальній БД з існуючими обʼєктами → повторний прогін чистий.
`dotnet ef migrations list` показує всі 9; build green; tests 500/500.
Log: `bug009_2026-07-03_orphan-migrations_database-engineer.md`

---

## BUG-010 — GET /api/marketplace/suppliers/{id} віддає unpublished профіль
**Status:** done · **Agent:** backend-developer · **Depends:** — · Updated: 2026-07-03
Found in QA v4.1 (`qa_282-288_2026-07-03.md`). Листинг/search фільтрують `IsPublic`,
але detail-ендпоінт — ні: неопублікований профіль був доступний будь-кому за id.
Fix: `MarketplaceService.GetSupplierProfileAsync` повертає `null` (→404) якщо
`profile.IsPublic == false` — для анонімних і автентифікованих. Legitimate доступи
не зачеплені: supplier cabinet читає свій профіль через `ISupplierCabinetService.
GetOwnerManagedProfileAsync` (окремий шлях), MarketplaceAdminController використовує
лише Admin*-методи — інших call sites у `GetSupplierProfileAsync` нема.
Tests: +2 unit (unpublished→null для anon/auth, published→dto). `dotnet build` 0 warn.
Follow-up (main session, 2026-07-03): той самий guard додано в `GetSupplierItemsAsync`
і `GetSupplierReviewsAsync` (приватний `IsPublishedAsync`) → `/items` і `/reviews`
unpublished-постачальника тепер теж 404. +4 unit tests. `dotnet test` 500/500 green.
Log: `bug010_2026-07-03_unpublished-supplier-leak_backend-developer.md`

---

## BUG-011 — банер «Час сеансу сплив» після ручного «Вийти»
**Status:** done · **Agent:** frontend-developer · **Depends:** — · Updated: 2026-07-03
Repro: клік «Вийти» → /login з банером session_expired (TASK-279), хоча вихід ручний.
Cause: in-flight polling (SupportChatWidget 3с, notifications badge) ловив 401 після
відкликання refresh cookie → `apiFetch` робив hard redirect `/login?reason=session_expired`,
перебиваючи чистий `router.push("/login")` з `useLogout`.
Fix (`frontend/lib/api.ts` + `useAuth.ts`): module-level прапорець `markLoggedOut()`,
який `useLogout.mutationFn` ставить ПЕРЕД `authApi.logout()`; у 401-гілці `apiFetch`
при прапорці — тихий `ApiError` без refresh/redirect (перевірка і до, і після tryRefresh
для гонки). Прапорець скидається в `setToken()` (login/refresh). Додатково: 401 без
токена на момент запиту → редірект на `/login` БЕЗ reason (не «сеанс сплив»).
TASK-279 сценарій не зачеплено: протухла сесія з токеном далі дає reason=session_expired.
`npx tsc --noEmit` + `npm run build` green.
Log: `bug011_2026-07-03_logout-expired-banner_frontend-developer.md`

---

## BUG-013 — майстер «Новий клієнт» (provider): нема типу «Постачальник» + кирилична назва блокує «Далі»
**Status:** done · **Agent:** frontend-developer · **Depends:** — · Updated: 2026-07-03
Repro: CreateTenantWizard (панель провайдера) не мав business type «Постачальник»
(supplier додано лише в admin у TASK-286); кирилична назва → slugify відкидав усі
не-ASCII символи → slug порожній → кнопка «Далі» disabled.
Fix: (1) `features/provider/types.ts` — `supplier` у BusinessType, labels («Постачальник»,
🚚), ALL_BUSINESS_TYPES, preset `["marketplace_supplier"]`; `marketplace_supplier` у
TenantModule + MODULE_LABELS/DESCRIPTIONS/ALL_MODULES (звірено з Tenant.cs, TASK-282).
(2) Спільна util `lib/slug.ts` — транслітерація укр→лат (щ→shch, ї→yi, х→kh тощо) +
санітизація; використана в CreateTenantWizard і admin/CreateTenantModal (там була та сама
вада). Назва компанії зберігається як введена — транслітерується тільки slug.
tsc + next build green.
Log: `bug013_2026-07-03_provider-wizard-supplier-slug_frontend-developer.md`

---

## TASK-290 — AddTenantUserModal: role selector + success view (ADR-016)
**Status:** done · **Agent:** frontend-developer · **Depends:** — · Updated: 2026-07-03
Попередній прогін лишив компонент напівготовим: рахував `isSupplier`/`roles`/`role`,
але не рендерив селектор ролі, не слав `role` у запиті, і мав мертвий код
(`createdUser`/`CheckCircle2`). `TenantDetailPanel` не передавав `businessType`.
Fix: `types.ts` (`role` у `CreateTenantUserRequest`), `TenantDetailPanel.tsx`
(`businessType={tenant?.businessType}`), `AddTenantUserModal.tsx` (поле «Роль»,
`role` у mutateAsync, success-екран після створення). Backend поки ігнорує `role`
(окрема задача). tsc + build green.
Log: `290_2026-07-03_supplier-user-role-modal_frontend-developer.md`

---

## TASK-292 — Кнопки в модалках маркетплейсу: стиль під `Btn`
**Status:** done · **Agent:** frontend-developer · **Depends:** — · Updated: 2026-07-03
`CreateSupplierModal.tsx` і `AddSupplierItemModal.tsx` мали raw `<button>` замість
спільного `components/ui/Btn.tsx` — випадали зі стилю решти застосунку (user feedback).
Fix: «Скасувати» → `<Btn variant="ghost">`, primary-дія → `<Btn type="submit">`
(той самий патерн, що вже в `AddTenantUserModal.tsx`). Тільки розмітка, логіка не змінена.
`tsc --noEmit` + `npm run build` green.
Log: `292_2026-07-03_supplier-modal-buttons-restyle_frontend-developer.md`

---

## BUG-012 — POST /api/admin/marketplace/suppliers 500 (FK violation) на prod
**Status:** done · **Agent:** backend-developer · **Depends:** — · Updated: 2026-07-03
Root cause: `MarketplaceService.AdminCreateSupplierAsync` хардкодив `TenantId = Guid.Empty`
→ INSERT у `suppliers` порушував FK `FK_suppliers_tenants_TenantId` (тенант 00000000-… не
існує). Флоу TASK-275 «+ Створити постачальника» падав 500 завжди — рядків з Guid.Empty
у prod немає.
Fix: get-or-create системний tenant «Platform Marketplace» (slug `platform-marketplace`,
business_type=supplier, inactive, без users) — `MarketplaceRepository.
GetOrCreatePlatformTenantIdAsync` (ліниво, race-safe по unique slug + detach на програші);
`AdminCreateSupplierAsync` використовує його id. Supplier cabinet не зачеплено: профілі
admin-флоу мають `IsOwnerManaged = false`, кабінет фільтрує `IsOwnerManaged = true` —
покрито тестом. Чому TASK-275-тести не зловили: NSubstitute-моки репо не перевіряють FK;
додано 4 repo-тести на EF InMemory (перший виклик створює tenant, другий/крос-контекст
реюзає; cabinet-лукап не бачить platform-suppliers) + 2 service-тести. ADR-016 amendment
у `decisions.md`. Build green, 506/506 тестів.
Log: `bug012_2026-07-03_admin-supplier-fk_backend-developer.md`
**Next:** deploy to prod; re-check «+ Створити постачальника» на /marketplace.

---

## BUG-014 — Provider випадково створив supplier_admin у системному tenant «Platform Marketplace»
**Status:** done · **Agent:** backend-developer · **Depends:** BUG-012 · Updated: 2026-07-03
Root cause: системний tenant `platform-marketplace` (BUG-012 фікс) не фільтрувався у
`ProviderService.GetTenantsAsync` → з'являвся у provider-панелі поруч з реальними клієнтами;
provider створив там supplier_admin через «Додати адміністратора», юзер отримує 403 на
`/api/supplier-cabinet/*` (tenant inactive, без модуля marketplace_supplier).
Fix: `TenantRepository.GetAllAsync` фільтрує `Slug != MarketplaceRepository.PlatformTenantSlug`
на рівні репозиторію (уникнули cross-feature reference з Application); `ProviderService.
CreateTenantUserAsync` — загальний guard: `!tenant.IsActive` → 400 "Tenant is not active."
(захищає від тієї ж помилки на будь-якому деактивованому tenant, не тільки platform).
Тести: `TenantRepositoryPlatformTenantTests` (EF InMemory, GetAllAsync виключає platform tenant)
+ `ProviderServiceTests.CreateTenantUser_InactiveTenant_IsRejected`. Build green, 515/515 тестів.
Data cleanup на prod (stray user) — окремо, поза скоупом цього фіксу.
Log: `bug014_2026-07-03_platform-tenant-visible-in-provider-list_backend-developer.md`
**Next:** deploy to prod; clean up stray user tenant 89d95a15-abcb-459a-b943-6e9a8a3f07ac.

---

## BUG-007 — /api/movements 500: паралельні запити на одному DbContext
**Status:** done · **Agent:** backend-developer · **Depends:** — · Updated: 2026-07-02
Found during store_manager role QA (follow-up to BUG-006). На prod `/api/movements`
повертав 500 на кожен виклик (5/5 запитів fail).
Root cause: `MovementService.GetAsync` запускав `_repo.GetAsync` і `_repo.CountAsync`
паралельно через `Task.WhenAll` на одному scoped `AppDbContext`. DbContext не
thread-safe → «A second operation was started on this context instance…» → 500.
Fix: обидва запити виконуються послідовно через `await` у
`ShelfGuard.Application/Features/Movements/MovementService.cs`. Grep по всьому
Application + Infrastructure: інших `Task.WhenAll` над одним DbContext немає.
Build green, 459/459 тестів.
Log: `bug007-008_2026-07-02_movements-concurrency-topproducts-jsonb_backend-developer.md`
**Next:** deploy to prod; re-run store_manager QA pass.

---

## BUG-008 — /api/analytics/pos/top-products 500: jsonb Barcodes у SQL-проєкції
**Status:** done · **Agent:** backend-developer · **Depends:** — · Updated: 2026-07-02
Found during store_manager role QA (follow-up to BUG-006). Ендпоінт падав 500 навіть
після фіксу DateTime Kind (BUG-006).
Root cause: `AnalyticsRepository.GetPosTopProductsAsync` проєктував
`i.Product!.Barcodes.Count > 0 ? i.Product.Barcodes[0] : null` всередині SQL-запиту.
`Barcodes` — `List<string>` mapped to `jsonb`; Npgsql не транслює `.Count` / індексер
`[0]` над jsonb-списком → runtime translation exception → 500.
Fix: у проєкції вибирається весь список (`Barcodes = i.Product!.Barcodes`), перший
штрихкод береться client-side (`FirstOrDefault()`) після `ToListAsync` — той самий
патерн, що в `DailySalesRepository.cs:50-54`. Інші `Barcodes.Count/[0]` у кодовій базі —
в Application-сервісах над матеріалізованими entity, не в IQueryable — не зачеплені.
Build green, 459/459 тестів.
Log: `bug007-008_2026-07-02_movements-concurrency-topproducts-jsonb_backend-developer.md`
**Next:** deploy to prod; re-run store_manager QA pass on POS analytics.

---

## BUG-006 — Analytics 500: DateTimeKind.Unspecified vs timestamptz
**Status:** done · **Agent:** backend-developer · **Depends:** — · Updated: 2026-07-02
Found during QA of store_manager role. On prod усі 4 POS analytics ендпоінти
(`/api/analytics/pos/summary`, `revenue-trend`, `top-products`, `cashiers`) повертали 500,
а `/api/analytics/write-offs` та `/api/movements` — 500 тільки з `from=&to=` фільтрами.
Root cause: `DateOnly.ToDateTime(TimeOnly.MinValue/MaxValue)` в `AnalyticsRepository.cs`
дає `DateTime` з `Kind=Unspecified`; Npgsql відхиляє такі параметри для `timestamptz`
колонок (`pos_transactions.CreatedAt` тощо) → runtime exception → 500. Тести не ловили,
бо використовують fake-репозиторії.
Fix: приватні хелпери `ToUtcStart(DateOnly)` / `ToUtcEnd(DateOnly)` через
`ToDateTime(..., DateTimeKind.Utc)`; замінено всі 14 конверсій. `MovementRepository` вже
використовував правильний overload — без змін. Build green, 459/459 тестів.
Log: `bug006_2026-07-02_analytics-datetime-kind-500_backend-developer.md`
**Next:** deploy to prod; re-run store_manager QA pass on analytics endpoints.

---

## TASK-278 — Live Chat: живий чат провайдер ↔ клієнт
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** — · Updated: 2026-06-21
Різниця між тікетом і чатом: тікет — для довгострокових задач (налаштування компанії), чат — миттєве спілкування.
**DB (міграція AddChatFeature):**
- `chat_sessions` (id, tenant_id, created_by_user_id, subject TEXT, status open/closed, created_at, updated_at; RLS на tenant_id)
- `chat_messages` (id, session_id, sender_user_id, sender_name TEXT, body TEXT, is_read, created_at; RLS через session → tenant_id)
**Backend:**
- `POST /api/chat/sessions` — клієнт відкриває нову сесію (перший повідомлення)
- `GET /api/chat/sessions` — клієнт бачить свої сесії (свій tenant)
- `GET /api/chat/sessions/{id}/messages` — список повідомлень сесії
- `POST /api/chat/sessions/{id}/messages` — надіслати повідомлення (клієнт або провайдер)
- `POST /api/chat/sessions/{id}/close` — закрити сесію
- `GET /api/admin/chat/sessions` (ProviderOnly) — всі сесії cross-tenant
- `GET /api/admin/chat/sessions/{id}/messages` (ProviderOnly) — повідомлення клієнта
- `POST /api/admin/chat/sessions/{id}/messages` (ProviderOnly) — відповідь провайдера
- `POST /api/admin/chat/sessions/{id}/close` (ProviderOnly) — закрити сесію
**Frontend (клієнт) — `SupportChatWidget.tsx`:**
- Повністю переробити: замість тікету показати список чат-сесій + кнопку "Новий чат"
- Активна сесія: вигляд як у месенджері (бульки повідомлень), input внизу, відправка через Enter/кнопку
- Polling кожні 3 секунди через `refetchInterval` React Query (без WebSocket)
**Frontend (провайдер) — нова вкладка в `/service-desk`:**
- Панель "Живий чат" поруч із існуючим Service Desk
- Список чат-сесій усіх клієнтів (ім'я, тенант, остання активність, кількість непрочитаних)
- При натисканні — повна переписка + input для відповіді
- Нові повідомлення підсвічуються, polling кожні 3с
Accept: dotnet build green; міграція green; клієнт може надіслати повідомлення, провайдер його бачить і відповідає; tsc + next build green.

## TASK-277 — Команда: створення користувача з логіном/паролем та правами
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** — · Updated: 2026-06-21
**Backend:**
- Розширити `InviteProviderMemberRequest` полем `Password?: string` (необов'язкове)
- В `ProviderTeamService.InviteMemberAsync`: якщо `Password` передано → хешувати його замість `tempPassword`
- Якщо `Password` не передано — поведінка залишається як є (tempPassword)
**Frontend — `InviteProviderMemberModal.tsx`:**
- Додати поля: «Пароль» (type=password) + «Підтвердження паролю»
- Валідація: обидва поля повинні збігатися, мінімум 6 символів
- Додати секцію «Права доступу» — readonly список того, що може робити обрана роль:
  - provider_admin: управління командою, всі клієнти, Service Desk, Чат
  - provider_agent: Service Desk, Чат, перегляд клієнтів
- Кнопка тепер «Створити користувача» (а не «Запросити»)
Accept: backend build green; фронтенд: tsc green; можна створити провайдер-агента з власним паролем, він може увійти в систему з цим паролем.

## TASK-276 — Розклад: множинний вибір днів при додаванні зміни
**Status:** done · **Agent:** frontend-developer · **Depends:** — · Updated: 2026-06-21
Поточний `AddSlotModal` у `ScheduleTab.tsx` дозволяє вибрати лише один день.
**Зміни:**
- Замінити `<select>` для дня тижня на 7 чекбоксів (Пн–Нд) у горизонтальній сітці
- Форма дозволяє виділити будь-яку кількість днів (мінімум 1)
- При сабміті — послідовно викликати `create.mutateAsync` для кожного вибраного дня з однаковими `userId`, `startTime`, `endTime`, `notes`
- Стан форми: `dayOfWeek` → `daysOfWeek: number[]`
- Якщо будь-який з викликів повертає помилку — показати її й зупинитись
- Після успіху — закрити модалку (одиночний `onClose()`)
Accept: tsc green; можна обрати 3 дні → backend отримує 3 POST-запити → 3 слоти з'являються у grid.

## TASK-275 — Маркетплейс: Full-width + Створення постачальника + Додавання товарів
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** — · Updated: 2026-06-21
**Frontend (швидке виправлення):**
- У `frontend/app/(dashboard)/marketplace/page.tsx` рядок 80: видалити `maxWidth: 1200` зі стилів обгортки
**Backend — нові провайдер-ендпоінти (`MarketplaceAdminController`):**
- `POST /api/admin/marketplace/suppliers` (ProviderOnly) — створити нового постачальника:
  Body: `{ companyName, region, categories[], website?, deliveryRegions[], workingHours?, paymentTerms?, isPublic, plan }`
  Дія: CREATE `Supplier` (tenantId = provider tenant_id) + CREATE `SupplierProfile` для нього
- `POST /api/admin/marketplace/suppliers/{id}/items` (ProviderOnly) — додати товар:
  Body: `{ customName, price?, minQty?, unit?, isAvailable }`
  Дія: CREATE `SupplierItem` (supplierId = id)
- `DELETE /api/admin/marketplace/suppliers/{id}/items/{itemId}` (ProviderOnly) — видалити товар
**Frontend — сторінка `/marketplace`:**
- Додати кнопку «+ Створити постачальника» (видима лише для PROVIDER_TEAM ролей) поруч із пошуковим рядком
- `CreateSupplierModal.tsx` (`features/marketplace/components/`): форма з полями companyName, region, categories (textarea через кому), isPublic toggle, plan select (free/premium)
- На `SupplierCard.tsx` або `marketplace/[id]/page.tsx` — кнопка «+ Додати товар» (видима для PROVIDER_TEAM):
  `AddSupplierItemModal.tsx`: customName, price, minQty, unit, isAvailable toggle
- Hooks: `useCreateSupplier`, `useAddSupplierItem`, `useDeleteSupplierItem` у `features/marketplace/hooks/`
Accept: backend build green; tsc + next build green; провайдер може створити постачальника → він з'являється у списку; можна додати/видалити товар; сторінка на всю ширину.

---

## v3.4 carry-over

## TASK-274 — Provider Schedule (розклад команди)
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** TASK-272 · Updated: 2026-06-20
Тижневий розклад доступності для агентів: recurring slots (DayOfWeek 0-6 + time range).
Backend: entity `ProviderScheduleSlot` + migration `AddProviderScheduleSlots` + `ProviderScheduleController`
(GET ?userId=, POST, DELETE/{id}; ProviderTeamMember/ProviderCanInvite policies).
Frontend: `ScheduleTab.tsx` — 7-колонковий weekly grid + AddSlotModal.
Build green, migration green, tsc green.
Log: `274_2026-06-20_provider-schedule_backend-developer.md`

## TASK-273 — Provider Employee Statistics
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** TASK-272 · Updated: 2026-06-20
Статистика продуктивності команди (без нової схеми): assigned/resolved tickets, created-by-provider, comments, avg resolution time.
Backend: `IProviderStatsRepository` + `ProviderStatsRepository` (cross-tenant) + `ProviderStatsService` + `GET /api/provider/team/stats`.
Frontend: `StatsTab.tsx` — таблиця з прогрес-баром resolve rate + кольоровими метриками.
Build green, tsc green.
Log: `273_2026-06-20_provider-employee-stats_backend-developer.md`

## TASK-272 — Provider HR: управління власним персоналом
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** — · Updated: 2026-06-20
Розширення команди провайдера: редагування учасника + реактивація.
Backend: `PUT /api/provider/team/{id}` + `POST /api/provider/team/{id}/reactivate` ([ProviderCanInvite]).
Frontend: `EditMemberModal.tsx` (нова) + оновлений `TeamTab.tsx` з кнопками Edit/Відновити.
Guard: роль власника (`provider`) не може бути змінена через API.
Build green, tsc green.
Log: `272_2026-06-20_provider-hr-staff-management_backend-developer.md`
**Next:** TASK-273 (employee performance stats), TASK-274 (schedule/calendar UI).

---

## TASK-271 — Backend: Provider cross-tenant Service Desk
**Status:** done · **Agent:** backend-developer · **Depends:** TASK-251 · Updated: 2026-06-20
Provider може бачити тікети з усіх тенантів та створювати тікети від імені клієнта.
Нові ендпоінти (ProviderOnly policy):
- `GET  /api/admin/service-desk?status=&tenantId=` — всі тікети cross-tenant
- `POST /api/admin/service-desk` — створити тікет для клієнтського тенанту
Нові файли: `IProviderTicketRepository`, `ProviderTicketRepository`, `IProviderTicketService`,
`ProviderTicketService`, `ProviderServiceDeskDtos`, `AdminServiceDeskController`.
Migration `AddTicketCreatedByProvider` — `CreatedByProvider bool DEFAULT false` на `support_tickets`.
Тікет зберігається з `TenantId = client tenant` + `CreatedByProvider = true` → клієнт бачить у
своєму Service Desk, Провайдер бачить у cross-tenant запиті.
Build green, 459/459 тестів.
Log: `271_2026-06-20_provider-service-desk-backend_backend-developer.md`
**Next:** TASK-272 Provider HR (власний персонал), TASK-270 chat button in header.

---

## BUG-005 — pos_transactions.RetryCount missing on production
**Status:** done · **Agent:** database-engineer · **Depends:** — · Updated: 2026-06-16
Flagged in TASK-204 log: prod threw `column p.RetryCount does not exist` in
`PosService.GetPendingFiscalizationAsync`. Root cause: migration
`20260613000000_AddPosTransactionRetryCount` (TASK-069, committed 2026-06-13) was never
actually deployed to prod. Fix: regenerated as `20260616151654_AddPosTransactionRetryCount`
(same single AddColumn, fresh timestamp so it lands after the v4 rename migrations on next
deploy). Build green, Pos tests 76/76 green.
Log: `bug005_2026-06-16_pos-retrycount-missing-column_database-engineer.md`
**Next:** verify on next prod deploy that the migration applies and fiscalization retry
worker stops erroring.

---

## TASK-078 — Mobile: Write-offs screen
**Status:** done · **Agent:** mobile-developer · **Depends:** — · Updated: 2026-06-15
Екран списання для мобільного працівника:
- Список власних списань (GET /api/write-offs)
- Кнопка «+ Списання» → scan штрихкод (expo-camera) → підтягнути назву товару → вибір причини (expired/damaged/theft/other) → кількість → коментар → підтвердження
- Detail екран окремого списання
- Тільки для ролей: storekeeper, store_manager і вище
Accept: tsc green; flow проти API (create + list); scan штрихкоду відкриває форму з назвою товару.

## TASK-079 — Mobile: Transfers screen
**Status:** done · **Agent:** mobile-developer · **Depends:** — · Updated: 2026-06-15
Екран переміщень між магазинами/зонами:
- Список переміщень (GET /api/transfers)
- Кнопка «+ Переміщення» → scan штрихкод → кількість → вибір destination store → підтвердження
- Статуси: pending / in_transit / completed
- Тільки для ролей: storekeeper, store_manager і вище
Accept: tsc green; create + list flow проти API.

## TASK-080 — Mobile: Notifications screen
**Status:** done · **Agent:** mobile-developer · **Depends:** — · Updated: 2026-06-15
Сповіщення на мобільному:
- Bell icon у (app)/_layout.tsx header з badge кількості непрочитаних
- Екран /notifications: список (GET /api/notifications/history), тип іконкою (expiry/stock/system), read/unread стилі
- Tap → mark as read
Accept: tsc green; список підвантажується з API; badge оновлюється.

## TASK-081 — Mobile: Dashboard з реальними даними
**Status:** done · **Agent:** mobile-developer · **Depends:** — · Updated: 2026-06-15
Підключити index.tsx до реальних API:
- Картки Safe/Warning/Critical/Expired → GET /api/stock/summary
- Секція «AI замовлення» → GET /api/ai-orders (pending suggestions, count)
- Секція «Останні події» → GET /api/stock/events?limit=5 (або /api/activity-logs)
- Pull-to-refresh
Accept: tsc green; реальні числа замість заглушок; pull-to-refresh працює.

---

## v3.3 carry-over

## TASK-075 — Architect: Menu groups + Role matrix
**Status:** done · **Agent:** project-manager · **Depends:** — · Updated: 2026-06-14
Визначити логічні групи навігації та матрицю доступу ролей до меню.
Нова роль: Касир (cashier) — тільки /pos.
Уточнено: StoreManager → менеджмент магазину; NetworkManager → мережева картина.
Accept: задокументована матриця, TASK-076 + TASK-077 готові до виконання.

## TASK-076 — Backend: Cashier role + оновлені AppPolicies
**Status:** done · **Agent:** backend-developer · **Depends:** 075 · Updated: 2026-06-14
Додати роль `cashier` до AppRoles enum (C#), оновити AppPolicies:
- CanAccessPos: cashier + storekeeper + store_manager + network_manager + enterprise_admin
- CanManageStore: store_manager + network_manager + enterprise_admin (без cashier/storekeeper/merchandiser)
- CanViewNetworkAnalytics: network_manager + enterprise_admin
Оновити UserInviteDto/UserUpdateDto валідацію нових ролей.
Accept: dotnet build green; тести авторизації з cashier роллю проходять.

## TASK-077 — Frontend: Згрупований Sidebar + RBAC видимість
**Status:** done · **Agent:** frontend-developer · **Depends:** 075, 076 · Updated: 2026-06-14
Переробити Sidebar.tsx: групи зі стрілкою expand/collapse, роль-based видимість.

**Групи та доступ:**
1. Головна: Дашборд — TENANT_ROLES
2. Каса (expand): Каса (/pos), POS Аналітика — CAN_ACCESS_POS (cashier + managers)
3. Склад (expand): Каталог, Залишки, Прийомка, Переміщення, Списання — CAN_RECEIVE_STOCK + TENANT_ROLES
4. Продажі (expand): Продажі, Замовлення, AI Замовлення, Події — AT_LEAST_STORE_MANAGER
5. Аналітика (expand): Аналітика загальна, POS Аналітика — CAN_VIEW_ANALYTICS
6. Управління (expand): Персонал, План магазину, IoT пристрої — AT_LEAST_STORE_MANAGER
7. Адмін: Провайдер, Адмін — PROVIDER_ONLY
8. Налаштування — all

**Нові role sets у frontend/lib/roles.ts:**
- CAN_ACCESS_POS: cashier + CAN_RECEIVE_STOCK
- CAN_MANAGE_STORE: AT_LEAST_STORE_MANAGER (без cashier/storekeeper)
- CAN_VIEW_NETWORK: network_manager + enterprise_admin

**Правила видимості по ролях:**
- cashier: тільки Каса (група Каса), Налаштування
- storekeeper: Склад, Каса (без POS Аналітики), Налаштування
- merchandiser: Склад (Каталог + Залишки, без Прийомки/Переміщень), Налаштування
- store_manager: Каса, Склад, Продажі, Аналітика, Управління, Налаштування
- network_manager: Каса (POS Аналітика), Продажі, Аналітика, Управління, Налаштування
- enterprise_admin: все крім Provider/Admin
Accept: tsc + next build green; кожна роль бачить тільки свої групи; collapse/expand працює.

---

# Carry-over from v3.2 «ПРРО Каса» (started 2026-06-12)

Scope: v3-spec §3 + §6 Фаза 4. ADR-012: Checkbox (SaaS ПРРО) as fiscal provider behind
IFiscalService, offline-first (ADR-011 flow stays). Test cash register registered in
Checkbox cabinet (фіскальний номер TEST582378; license key + cashier creds in
.claude/private/access.md — blocker resolved 2026-06-12).

## TASK-066 — DB: pos_shifts, pos_transactions, pos_transaction_items
**Status:** done · **Agent:** database-engineer · **Depends:** — · Updated: 2026-06-12
v3-spec §5 + Status/'pending_fiscalization', OfflineNumber; RLS (TenantId direct);
FK product_stock SET NULL (яка партія списана). Accept: migration + RLS verified, build green.
Committed as 6d7a5082 «feat(pos): v3.2 POS schema».

## TASK-067 — Infrastructure: Checkbox fiscal client (IFiscalService)
**Status:** done · **Agent:** backend-developer · **Depends:** — · Updated: 2026-06-12
Done: IFiscalService + DTOs (Application/Features/Pos/Fiscal), CheckboxFiscalClient +
PrroOptions + token store (Infrastructure/Integrations/Prro), Noop fallback, DI switch,
unit tests 292/292 green. Live: license key valid on api.checkbox.in.ua
(⚠️ dev-api host from docs does NOT resolve — docs corrected). Cashier creds received →
**full live e2e GREEN** (CheckboxLiveE2ETests, gated by PRRO_LIVE_E2E=1): PIN signin →
shift CREATED→OPENED → sell receipt DONE (fiscal_code TEST-KcEsEF + tax_url) → Z-report
CLOSED, ~6s total. Added IFiscalService.GetShiftStatusAsync (shift opening is async —
needed for polling; TASK-068 must poll after open/close).
Log: 067_2026-06-12_checkbox-fiscal-client_backend-developer.md
ADR-012. Integrations/Prro: CheckboxFiscalClient implementing IFiscalService —
cashier signin (login/password or PIN → bearer token), shift open/close, sell receipt,
receipt status; DTOs; config binding PRRO__* (PROVIDER/BASEURL/LICENSEKEY/CASHIER__*,
secrets in .env only); error mapping + timeouts; unit tests with fake HTTP handler.
Accept: unit tests green (fake handler); live: dev-api.checkbox.in.ua reachability green
+ license-key flow as far as possible without cashier creds (blocker: cashier login/PIN
pending from user).

## TASK-068 — API: POS endpoints (shifts, sales → FEFO + stock_events)
**Status:** done · **Agent:** backend-developer · **Depends:** 066, 067 · Updated: 2026-06-13
⚠️ ADR-013: must resolve fiscalization through the per-tenant IFiscalServiceFactory
(TASK-071), not the startup-time IFiscalService DI registration.
POST /api/pos/shifts/open|close, POST /api/pos/sales (items by barcode; critical → auto
discount price, expired → 423 block per spec §3), GET /api/pos/shifts/current, sales list.
Sale = one DB tx: pos_transaction + items + FEFO write-down + stock_events('pos_sale');
fiscalization async (Status). Accept: service tests (FEFO, expired block, totals), build green.

## TASK-069 — Worker: fiscalization retry job
**Status:** done · **Agent:** backend-developer (worker) · **Depends:** 067, 068 · Updated: 2026-06-13
Cron */5 min: pending_fiscalization docs → submit/poll receipt status via Checkbox
(through API endpoint backed by IFiscalService); update FiscalNumber/Status on DONE.
Offline numbering handled by Checkbox itself (ADR-012). Accept: tsc green;
retry/backoff covered.

## TASK-071 — Settings: ПРРО провайдер (Checkbox) у Налаштування → Інтеграції
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** 067 · Updated: 2026-06-13
ADR-013. Per-tenant fiscal provider config, same mechanism as the Claude key
(integration_configs service='claude' → ClaudeOrderAdvisor.ResolveAsync; web UI
features/integrations + IntegrationsTab).
**Backend:** storage in integration_configs (service='prro', JSONB: provider
[checkbox|disabled, extensible], base_url [test/prod], license_key, cashier_login,
cashier_password, cashier_pin_code; RLS already on table — verify tenant isolation).
Endpoints: GET/PUT /api/settings/prro (GET masks secrets: ••••+last 4; PUT with
masked/unchanged secret keeps stored value — secrets are write-only),
POST /api/settings/prro/test (ping cash-registers/info via X-License-Key + cashier
signin, no shift side effects). Per-tenant IFiscalServiceFactory
(Infrastructure/Integrations/Prro): tenant DB config → PRRO__* env fallback →
NoopFiscalService; replaces startup DI switch; CheckboxTokenStore keyed per
tenant+license key. TASK-068/069 consume the factory.
**Frontend:** rework SERVICE_META.prro (features/integrations/types.ts — current
fields are stale placeholders) → provider select («Checkbox» / «вимкнено»),
credential form (license key, login/password or PIN, base URL test/prod toggle),
«Перевірити з'єднання» button calling /test, status badge (connected/error/disabled)
in IntegrationsTab card.
**Accept:** backend unit tests (resolution order DB→env→noop, masking, keep-on-masked
PUT, factory per-tenant); test endpoint green against live Checkbox test register;
cross-tenant isolation verified; tsc + next build green; full UI flow: select provider
→ enter creds → test → save → re-open shows masked secrets.

## TASK-070 — Mobile: POS screens (tablet) in Expo app
**Status:** done · **Agent:** mobile-developer · **Depends:** 068 · Updated: 2026-06-13
Зміна (open/close + PIN), продаж: скан штрихкоду (expo-camera) → кошик → ціна з акцією,
critical/expired badge, оплата cash/card (терминал SDK / принтер — Phase 4.1, поза скоупом),
чек зі статусом фіскалізації. Accept: tsc green; flow проти прод-API.

## TASK-072 — Web: POS dashboard (зміни, транзакції, Z-звіти)
**Status:** done · **Agent:** frontend-developer · **Depends:** 068 · Updated: 2026-06-14

## TASK-074 — SaaS Admin Panel: tenant onboarding + управління
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** — · Updated: 2026-06-15
Provider-only панель: список тенантів, створення (назва+slug+план+перший адмін),
статус active/inactive, зміна плану (basic/standard/enterprise/trial), модулі,
usage stats (users/stores/products/sales). Route /admin, policy ProviderOnly.
Backend: GET|POST /api/admin/tenants, GET|PATCH|POST /api/admin/tenants/{id}/...
Frontend: /admin сторінка з таблицею тенантів + create modal + detail drawer.
Accept: dotnet build+test green; tsc green; CRUD flow проти API.

## TASK-073 — POS Аналітика: API + Web дашборд
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** 068 · Updated: 2026-06-15
Нові ендпоінти GET /api/analytics/pos/* + веб-дашборд /analytics/pos.
Метрики: виручка за період, динаміка по днях, топ товарів, ефективність касирів,
середній чек, розбивка cash/card. Дані з pos_transactions + pos_transaction_items.
Accept: backend тести зелені; tsc + next build green; графіки відображають реальні дані.
Веб-інтерфейс для десктоп касира/менеджера — аналог TASK-070 (mobile) але для Next.js.
Route `/pos`. Функціонал: поточна зміна (відкрити/закрити + статус фіскалізації),
список продажів зміни (чек-деталі), Z-звіт після закриття, sidebar «Каса» (CanReceiveStock).
Використовує існуючі ендпоінти TASK-068:
  GET  /api/pos/shifts/current
  POST /api/pos/shifts/open  (body: { storeId, openingCash? })
  POST /api/pos/shifts/close
  GET  /api/pos/sales?shiftId=
Не включає: продаж через сканер (мобільна функція), оплата терміналом — Phase 4.1.
Accept: tsc + next build green; shift open/close/list-sales flow проти API.

---
# Previous sprint — v3.1 «IoT Foundation» (started 2026-06-12)

Scope: v3-spec §6 Фаза 1. ADR-010: MQTT ingestion in worker. pos_* tables → Phase 4.
**✅ COMPLETE 2026-06-12** — log: 061-065_2026-06-12_iot-foundation_multi-agent.md
Builds/tests green (backend 15/15 IoT tests, worker tsc, next build).
Live e2e PASSED on local stack: migration+RLS ✓, mosquitto pub/sub ✓,
temp alert → notification rows ✓, weight −490г → FEFO −2 units ✓.
2 bugs caught & fixed in e2e (jsonb config parsing; $6 type cast in notification log).
**DEPLOYED to production 2026-06-12** (93.127.143.98): mosquitto healthy (port 1884),
V3IotFoundation migration applied (auto on API start), RLS 6 policies verified,
worker «[mqtt] connected, subscribed to shelfguard/#», /iot and /floor-plan → 200.
Deploy bug fixed on the way: deploy.sh sourced unquoted .env → truncated DB
connection string overrode --env-file → API crash loop (fix: 95f5586d + quoted .env).

## TASK-061 — DB: IoT schema (iot_devices, temperature_readings, weight_readings)
**Status:** done · **Agent:** database-engineer · **Depends:** — · Updated: 2026-06-12
v3-spec §5: 3 tables + RLS (tenant via iot_devices.tenant_id; readings join device),
FKs to stores/store_zones, idx_temp_readings_device_time + device_id unique.
Accept: migration applies cleanly; RLS verified cross-tenant; dotnet build green.

## TASK-062 — DevOps: Mosquitto MQTT broker in docker-compose
**Status:** done · **Agent:** devops-engineer · **Depends:** — · Updated: 2026-06-12
Service `mosquitto` (eclipse-mosquitto:2), port 1883, allow_anonymous for dev,
persistent volume, MQTT_URL env wired to worker. Accept: `docker compose up` →
pub/sub smoke test on shelfguard/# passes.

## TASK-063 — API: iot_devices CRUD + readings endpoints
**Status:** done · **Agent:** backend-developer · **Depends:** 061 · Updated: 2026-06-12
GET/POST /api/iot/devices, GET/PUT/DELETE(soft) /api/iot/devices/:id,
GET /api/iot/devices/:id/readings (temp, paged), GET /api/iot/temperature?store_id=
(latest per device). Thin controllers, service in Application/Features/IoT.
Accept: tests for service rules (device_id unique per tenant, soft delete); build+tests green.

## TASK-064 — Worker: MQTT listener → readings + stock_events + temp alerts
**Status:** done · **Agent:** backend-developer (worker) · **Depends:** 061, 062 · Updated: 2026-06-12
Subscribe shelfguard/#; resolve device by device_id; update last_seen_at/battery.
temp payload → temperature_readings + threshold check (fridge >+8°C, freezer >-12°C
from device config) → is_alert + notification queue (critical → manager/director).
weight payload → weight_readings + confidence calc (95/85/60, <70 = log only) →
stock_events (type sensor) + FEFO write-down for confident deltas.
Offline cron: last_seen_at > 30 min → alert. Accept: tsc green; unit-testable pure
funcs for confidence/thresholds; e2e via mosquitto_pub on local stack.

## TASK-065 — Web: IoT devices dashboard (/iot)
**Status:** done · **Agent:** frontend-developer · **Depends:** 063 (+064 for live data) · Updated: 2026-06-12
Devices table: type icon, zone, online/offline (last_seen_at), battery, firmware;
register/edit/deactivate dialogs; temperature tab: recharts line per device,
alert badges. Sidebar «IoT пристрої» (AT_LEAST_STORE_MANAGER).
Accept: tsc + next build green; CRUD flow works against API.

---
# Previous sprint — v2.5 «AI Agent» ✅ COMPLETE (2026-06-12) — v2 DONE

## TASK-060 — Web: AI orders dashboard ✅ done (2026-06-12)
Log: `.claude/logs/tasks/060_2026-06-12_ai-orders-dashboard_frontend-developer.md`
/ai-orders per spec §7 mockup: base/AI/final + reasoning, inline edit, accept/reject.
Claude key manageable via Налаштування → Інтеграції. Live e2e pending Anthropic credits.

## TASK-058 + TASK-059 — Claude advisor + AI orders API + daily job ✅ done (2026-06-11)
Log: `.claude/logs/tasks/058-059_2026-06-11_ai-order-agent_backend-developer.md`
ClaudeOrderAdvisor (Infrastructure/AI, official SDK, structured outputs), 6 endpoints,
worker cron 05:00 + Telegram notify. Awaiting CLAUDE_API_KEY for live e2e.

---
# Previous sprint — v2.4 «Cannibalization» ✅ COMPLETE (2026-06-11)

## TASK-057 — Promo cannibalization ✅ done (2026-06-11)
Log: `.claude/logs/tasks/057_2026-06-11_cannibalization_backend-developer.md`
Auto-suggestions (promo ×2.0, siblings ×0.7), apply flow, promo coefficient in formula.
E2e: Вода k_event 2.0 × k_promo 2.0 → ORDER 304. Next: v2.5 AI Agent (TASK-058..060).

---
# Previous sprint — v2.3 «Events & Weather» ✅ COMPLETE (2026-06-11)

## TASK-056 — Web: events calendar ✅ done (2026-06-11)
Log: `.claude/logs/tasks/056_2026-06-11_events-calendar_frontend-developer.md`
/events: month grid, recurring projection, CRUD + coefficient editor, seed button. 200 OK.
Next: v2.4 Cannibalization (TASK-057) → v2.5 AI Agent (TASK-058..060).

## TASK-054 — Demand events calendar ✅ done (2026-06-11)
Log: `.claude/logs/tasks/054_2026-06-11_demand-events_backend-developer.md`
4 tables + RLS, full CRUD, 5 seeded holidays, event coefficient wired into order
formula (most-specific scope wins, events multiply). E2e: Вода ×2 → ORDER 152.

## TASK-055 — Open-Meteo integration ✅ done (2026-06-11)
Log: `.claude/logs/tasks/055_2026-06-11_open-meteo-weather_backend-developer.md`
Client + 6 endpoints + worker cron 06:00 + weather coefficient in formula.
E2e on real Kyiv forecast: k_event 2.0 × k_weather 1.5 → ORDER 228.

---
# Previous sprint — v2.2 «Buffer & Formula» ✅ COMPLETE (2026-06-11)

## TASK-053 — Web: orders page + buffer funnel ✅ done (2026-06-11)
Log: `.claude/logs/tasks/053_2026-06-11_orders-page-buffer-funnel_frontend-developer.md`
/orders: one-click chain ADU→buffers→order, funnel viz, MOQ/USQ tags. Deployed, 200 OK.
Next sprint: v2.3 «Events & Weather» (TASK-054..056).

## TASK-051 — CDA buffer engine ✅ done (2026-06-11)
Log: `.claude/logs/tasks/051_2026-06-11_cda-buffer-engine_backend-developer.md`
product_buffer table + RLS, pure CdaBufferCalculator (9 tests), GET/recalculate endpoints.
Verified on production: Total 51.97 = G 36.03 + Y 5.02 + R 10.92 (hand-checked).

## TASK-052 — Order formula ✅ done (2026-06-11)
Log: `.claude/logs/tasks/052_2026-06-11_order-formula_backend-developer.md`
POST /api/orders/calculate. Full chain verified on production:
Вода Моршинська 51.97+24−0−0 → ORDER 76. Tests 9/9.

---
# Previous sprint — v2.1 «Data Foundation» ✅ COMPLETE (2026-06-11)

## TASK-046 — v2 schema: daily_sales, product_adu, supply_schedules ✅ done (2026-06-11)
Log: `.claude/logs/tasks/046_2026-06-11_v2-data-foundation-schema_database-engineer.md`
Migration V2DataFoundation applied to production. RLS verified (6 policies).

## TASK-047 — Daily Sales API ✅ done (2026-06-11)
Log: `.claude/logs/tasks/047_2026-06-11_daily-sales-api_backend-developer.md`
GET/POST /daily-sales (upsert), POST /import (CSV by barcode), PUT /:id/mark-anomaly.
Verified on production. Tests 5/5.

## TASK-048 — ADU calculation engine ✅ done (2026-06-11)
Log: `.claude/logs/tasks/048_2026-06-11_adu-engine_backend-developer.md`
Pure AduCalculator (9 unit tests) + eligibility query + upsert. Verified on production:
recalculate → 2 products with adu_effective 10.9167 (group 3, 30 valid days).

## TASK-049 — Supply schedules CRUD ✅ done (2026-06-11)
Log: `.claude/logs/tasks/049_2026-06-11_supply-schedules-crud_backend-developer.md`
Full CRUD + one-active-per-pair rule (409), ISO day validation, soft delete.
Verified on production (6/6 e2e checks). Tests 11/11.

## TASK-050 — Web: sales entry page ✅ done (2026-06-11)
Log: `.claude/logs/tasks/050_2026-06-11_sales-entry-page_frontend-developer.md`
/sales: filters + manual entry form + CSV import dialog + anomaly toggle. Deployed, 200 OK.

---
# v1 maintenance (parallel)
TASK-045 (mobile profile+receipt wiring) · TASK-034 (auth tests) · TASK-035 (bin/obj)
TASK-038 (impersonation verify) · TASK-039 (bot /start) — see backlog.md

---
# Done

## TASK-033 — Notifications e2e ✅ done (2026-06-11)
Log: `.claude/logs/tasks/033_2026-06-11_notifications-e2e_devops-engineer.md`
Fixed 5 pipeline breaks (pg URL format, PascalCase SQL, Redis collision with another
project, DATE→NaN statuses, duplicate scheduler). Verified live: statuses recompute
hourly, 23 notifications queued. Delivery needs TELEGRAM_BOT_TOKEN / RESEND_API_KEY (user).


## TASK-018 — Mobile App Scaffolding ✅ done (2026-06-07)
Log: `.claude/logs/tasks/018_2026-06-07_mobile-scaffolding_mobile-developer.md`

## TASK-025 — DB Fix: RLS + FK Constraints ✅ done (2026-06-04)
Log: `.claude/logs/tasks/025_2026-06-04_fix-rls-fk_database-engineer.md`

## TASK-019 — Analytics API ✅ done (2026-06-04)
Log: `.claude/logs/tasks/019_2026-06-04_analytics_backend-developer.md`


## TASK-016 — Write-offs ✅ done (2026-06-04)
Log: `.claude/logs/tasks/016_2026-06-04_write-offs_backend-developer.md`

## TASK-015 — Stock Transfers ✅ done (2026-06-04)
Log: `.claude/logs/tasks/015_2026-06-04_transfers_backend-developer.md`

## TASK-014 — Stock Receipts ✅ done (2026-06-04)
Log: `.claude/logs/tasks/014_2026-06-04_receipts_backend-developer.md`

## TASK-013 — Suppliers CRUD ✅ done (2026-06-04)
Log: `.claude/logs/tasks/013_2026-06-04_suppliers-crud_backend-developer.md`

## TASK-012 — Stores/Zones CRUD ✅ done (2026-06-04)
Log: `.claude/logs/tasks/012_2026-06-04_stores-zones_backend-developer.md`

## TASK-007 — ProductStock API + FEFO ✅ done (2026-06-04)
Log: `.claude/logs/tasks/007_2026-06-04_product-stock-api_backend-developer.md`

## TASK-006 — Products API ✅ done (2026-06-04)
Log: `.claude/logs/tasks/006_2026-06-04_products-api_backend-developer.md`

## TASK-002 — Full DB Schema ✅ done (2026-06-04)
Log: `.claude/logs/tasks/002_2026-06-04_full-db-schema_database-engineer.md`

## TASK-010 — Web dashboard ✅ done (2026-06-03)
Log: `.claude/logs/tasks/010_2026-06-03_web-dashboard_frontend-developer.md`

---

## TASK-027..031 — Frontend Pages ✅ done (2026-06-04)
Log: `.claude/logs/tasks/027_2026-06-04_frontend-pages_frontend-developer.md`
Pages: /stock, /receipts, /receipts/:id, /transfers, /write-offs, /analytics

---

## TASK-011b — Web products page (/inventory) ✅ done (2026-06-10)
Log: `.claude/logs/tasks/011b_2026-06-10_products-page_frontend-developer.md`
Route: /inventory — Catalog CRUD (list + create + edit + delete + detail drawer)

---

## TASK-024 — Notifications Settings API ✅ done (2026-06-10)
Log: `.claude/logs/tasks/024_2026-06-10_notifications-api_backend-developer.md`
Endpoints: GET /notifications/settings, PUT /notifications/settings, GET /notifications/history, POST /notifications/test

---

## TASK-023 — Users API (HR module) ✅ done (2026-06-10)
Log: `.claude/logs/tasks/023_2026-06-10_users-api_backend-developer.md`
Endpoints: GET /users, GET /users/:id, POST /users/invite, PUT /users/:id, PUT /users/:id/permissions, DELETE /users/:id, GET /users/:id/activity

---

## TASK-022 — Discounts API ✅ done (2026-06-10)
Log: `.claude/logs/tasks/022_2026-06-10_discounts-api_backend-developer.md`
Endpoints: GET /discounts, GET /discounts/:id, POST /discounts, PUT /discounts/:id/approve, PUT /discounts/:id/cancel

---

## BUG-004 — Inconsistent 404 error format ✅ fixed (2026-06-11)
Log: `.claude/logs/tasks/bug004_2026-06-11_error-format-standardization_backend-developer.md`
Central fix: custom IClientErrorFactory + InvalidModelStateResponseFactory in ShelfGuard.Api.
All error bodies now follow `{error: "..."}`. Verified on production. All 4 smoke-test bugs closed.

---

## BUG-003 — GET /api/analytics/summary ✅ closed: not a bug (2026-06-11)
Log: `.claude/logs/reviews/bug003-resolution_2026-06-11.md`
Route never existed — smoke test probed a guessed name. Real endpoint is
`/api/analytics/expiry-summary`; all 6 analytics routes verified 200 on production.
Stale `/api/analytics/dashboard` row in api-contracts.md corrected.

---

## BUG-002 — GET /api/stock/summary ✅ fixed (2026-06-11)
Log: `.claude/logs/tasks/bug002_2026-06-11_stock-summary-endpoint_backend-developer.md`
Response: `{safe, warning, critical, expired, needsVerification, total}`. Optional `?store_id` filter.
Verified on production: 25 total batches (11 safe / 7 warning / 5 critical / 2 expired).

---

## BUG-001 — RLS Tenant Leakage ✅ fixed (2026-06-10)
Log: `.claude/logs/tasks/bug001_2026-06-10_rls-tenant-leakage_security-reviewer.md`
Fix: `TenantConnectionInterceptor.BuildSetSql()` now always SETs `app.tenant_id`.
Provider users get null UUID → RLS returns `[]` instead of leaking tenant data.
Tests: 13/13 pass.

---

## Next candidates

- **TASK-007** — ProductStock (batches) API + FEFO logic — **найвищий пріоритет**, блокує dashboard реальні дані
- **TASK-011** — `/api/stock` backend endpoint + `/stock` frontend page
  - Requires: product_stock table ✅, catalog_products ✅
  - Blocks: real dashboard stats (Safe/Warning/Critical/Expired from actual batches)

- **TASK-012** — Extend DbSeeder with store, zones, catalog_products, stock batches
  - Makes dashboard show real FEFO data instead of POC products proxy

- **TASK-003b** — Migrate catalog API from POC `Products` → `catalog_products`
  - Low priority until stock API is built
