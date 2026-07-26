# TASK-417: Fix — CRITICAL, 100%-reproducible RLS break in consumer loyalty join flow

**Agent:** backend-developer
**Date:** 2026-07-26
**Status:** done — fixed and verified live against real Postgres RLS (not mocks), no blocker

## Bug (from TASK-416 QA report)

`POST /api/consumer/loyalty/{tenantId}/join` 500'd for every consumer, always: `Npgsql.PostgresException
42501: new row violates row-level security policy for table "customers"`.

**Root cause:** a consumer session never carries the `tenant_id` JWT claim (cross-tenant by design —
`TenantConnectionInterceptor` falls back to the null-UUID for `app.tenant_id` on such sessions).
`LoyaltyService.JoinAsync` → `FindOrCreateCustomerAsync` reads/writes `customers`, which only has the
canonical `tenant_isolation`/`provider_bypass`/`worker_bypass` triad — no identity-based policy (unlike
`loyalty_memberships`/`loyalty_ledger_entries`, which got `consumer_self_access` in TASK-404). Lookup
silently returned 0 rows (RLS hid them), so the code always took the "create" branch, and the INSERT's
`WITH CHECK` rejected it because the row's real `TenantId` could never equal the null-UUID session var.

Confirmed by reading the RLS migration directly: `consumer_self_access` on `loyalty_memberships` has no
`FOR`/`WITH CHECK` clause, so Postgres derives the INSERT check from its `USING` expression too — that
policy alone (keyed on `ConsumerAccountId`, independent of `tenant_id`) already permits the membership
INSERT regardless of `app.tenant_id`. So only the `customers` step was ever actually broken; the
membership INSERT would have succeeded on its own. `JoinAsStaffAsync` (staff session, real `tenant_id`
claim already set) was untouched and confirmed unaffected — it was never broken.

## Fix

Rejected the "add an identity-based RLS policy to `customers`" option per the brief (no natural
`ConsumerAccountId` column on that table, shared with staff-created customers too). Instead:

- New `ITenantSessionOverride` (`backend/ShelfGuard.Application/Services/ITenantSessionOverride.cs`) +
  EF/Postgres implementation `TenantSessionOverride`
  (`backend/ShelfGuard.Infrastructure/Services/TenantSessionOverride.cs`): `ExecuteAsync<T>(tenantId,
  action, ct)` opens an explicit transaction, issues `SET LOCAL app.tenant_id = '{tenantId:D}'`, runs
  `action`, commits. Postgres auto-reverts `SET LOCAL` at transaction end (commit or rollback) — no
  manual restore step to forget, no leak to later queries on the same pooled connection even on an
  unhandled exception. `tenantId` is a `Guid` (not a raw string), so `:D`-formatting it has no
  injection surface (same reasoning `TenantConnectionInterceptor.BuildSetSql` already relies on).
  Interface doc spells out the security contract explicitly: only ever call with a tenantId the caller
  already trusts unconditionally for that operation — not a general RLS-bypass escape hatch.
- `LoyaltyService.JoinAsync` (`backend/ShelfGuard.Application/Features/Loyalty/LoyaltyService.cs`): the
  existing-membership idempotent-return branch is unchanged (needs no override —
  `consumer_self_access` already covers it). The customer-lookup-or-create + membership-create branch
  now runs inside `_tenantScope.ExecuteAsync(tenantId, ...)` — both writes atomic as a side benefit
  (either both land or neither does; previously a mid-sequence failure could have orphaned a
  `Customer` row with no membership).
- New constructor param on `LoyaltyService` (`ITenantSessionOverride`); DI registration added in
  `backend/ShelfGuard.Infrastructure/DependencyInjection.cs` (Scoped, next to the other TASK-405
  loyalty registrations).
- `JoinAsStaffAsync` untouched — staff sessions already have the correct `tenant_id` claim, never hit
  this gap.

## Tests

- `backend/ShelfGuard.Tests/Auth/LoyaltyServiceTests.cs`: updated the mock wiring (`ITenantSessionOverride`
  substitute, pass-through `ExecuteAsync`) so every pre-existing `JoinAsync` test still passes unchanged;
  added one new regression test pinning that the create-branch is actually routed through the override
  with the correct `tenantId` (mock-level; can only prove wiring, not real RLS).
- New `backend/ShelfGuard.Tests/Infrastructure/LoyaltyJoinRlsIntegrationTests.cs` — live Postgres, real
  repositories (not mocks), a throwaway `NOSUPERUSER NOBYPASSRLS` role, and the exact consumer-session
  GUC shape (no `app.tenant_id`, only `app.role='consumer'` + `app.consumer_account_id`). 3 tests:
  new consumer join creates a correctly-scoped `Customer`+`LoyaltyMembership` (and confirms the `SET
  LOCAL` override doesn't leak past its own transaction, checked on the same connection); second join
  call is idempotent, no duplicate rows; second tenant join stays isolated (own `Customer` row, no
  cross-tenant leak into a tenant-A-scoped staff read relying purely on RLS with no C#-side filter) and
  the cross-tenant wallet read (`GetMembershipsForConsumerAsync`, guarded solely by
  `consumer_self_access`, untouched by this fix) still correctly returns both memberships.
- This is exactly the live-RLS coverage QA flagged as missing for this path.

## Test-infra side effect (fixed, in scope)

Adding a 4th raw-Postgres, fresh-`NpgsqlDataSource`-per-call integration test file tipped EF Core's
process-wide `ManyServiceProvidersCreatedWarning`-as-error threshold (~20 cumulative distinct
`DbContextOptions` instances) over the edge, intermittently failing two *unrelated* pre-existing tests
(`PosConcurrencySalesIntegrationTests`, `LoyaltyConcurrencySalesIntegrationTests` — neither had the
defensive `.ConfigureWarnings(...)` downgrade `LoyaltyRepositoryIntegrationTests`/
`MarketingAnalyticsRepositoryIntegrationTests` already carry for the identical reason). Fixed two ways:
made my own new file share one `NpgsqlDataSource`/`DbContextOptions` per test method instead of building
a fresh one per call, and added the same one-line `.ConfigureWarnings(w =>
w.Log(CoreEventId.ManyServiceProvidersCreatedWarning))` precedent to those two files' `NewContext()`
helpers (test-infra hygiene only, zero behavior change to what either file actually asserts).

## Verification

`dotnet build`: 0 errors, 1 pre-existing unrelated warning (`MarketplaceServiceTests.cs`). `dotnet test`:
full suite run **3× consecutively, 1109/1109 green each time** (was 1105 before this task; +4: 1 mock
regression + 3 live-RLS integration), confirming both the fix and the test-infra fix are stable, not
lucky. New integration tests independently re-run in isolation too (all real DB round-trips, 3-10s each
— not silent soft-skips).

## Files touched

- New: `backend/ShelfGuard.Application/Services/ITenantSessionOverride.cs`,
  `backend/ShelfGuard.Infrastructure/Services/TenantSessionOverride.cs`,
  `backend/ShelfGuard.Tests/Infrastructure/LoyaltyJoinRlsIntegrationTests.cs`
- Modified: `backend/ShelfGuard.Application/Features/Loyalty/LoyaltyService.cs`,
  `backend/ShelfGuard.Infrastructure/DependencyInjection.cs`,
  `backend/ShelfGuard.Tests/Auth/LoyaltyServiceTests.cs`,
  `backend/ShelfGuard.Tests/Pos/PosConcurrencySalesIntegrationTests.cs` (test-infra only),
  `backend/ShelfGuard.Tests/Pos/LoyaltyConcurrencySalesIntegrationTests.cs` (test-infra only)

Not committed (repo convention — main session/user commits).

## Next

Security-reviewer should sanity-check `ITenantSessionOverride`'s contract/usage before wider release
(new, first-of-its-kind primitive in this codebase — narrow today, but worth an explicit second look
given what it's for). No other follow-up identified; `JoinAsStaffAsync` and the cross-tenant wallet
read were both confirmed unaffected, not just assumed.
