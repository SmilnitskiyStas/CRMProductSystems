# TASK-553 (backend half) — Consolidate TENANT_ISOLATION_TESTS suite, fix role-creation race

**Status:** done
**Agent:** backend-developer
**Depends on:** devops-engineer half (`.github/workflows/ci.yml` Postgres service — already done)

## Problem (from devops half's finding)

7 RLS test classes (`RlsCrossTenantIntegrationTests`, `LoyaltyRlsIntegrationTests`,
`LoyaltyJoinRlsIntegrationTests`, `MobileConfigDraftServiceRlsIntegrationTests`,
`MobileConfigPublishedReadRlsIntegrationTests`, `MobileThemeServiceRlsIntegrationTests`,
`StoreScopeRlsIntegrationTests`) each independently ran the same non-atomic "check pg_roles,
`CREATE ROLE rls_audit_test_role IF NOT EXISTS`, `GRANT ALL PRIVILEGES`" sequence in their own
`IAsyncLifetime.InitializeAsync`. Postgres roles are cluster-wide; xUnit runs different classes'
`IAsyncLifetime` fixtures in parallel by default (each class with no `[Collection]` gets its own
implicit collection, and implicit collections run concurrently). Two classes' bootstraps racing
threw `23505` (duplicate key on `pg_authid`) or `XX000` (tuple concurrently updated), which each
class's broad `catch (Exception)` misattributed as "Postgres unreachable," silently soft-skipping
that class's assertions for the run. Devops reproduced this 2/2 locally with a different random
subset skipped each time.

## Fix

Added `backend/ShelfGuard.Tests/Infrastructure/RlsAuditRoleFixture.cs`:
- `RlsAuditRoleFixture : IAsyncLifetime` — resolves the connection string once
  (`SHELFGUARD_TEST_DB_CONNECTION` env var, same default as before), creates/grants
  `rls_audit_test_role` exactly once, exposes `ConnectionString`/`DbAvailable`/`UnavailableReason`.
- `[CollectionDefinition("TENANT_ISOLATION_TESTS")] TenantIsolationTestsCollection :
  ICollectionFixture<RlsAuditRoleFixture>` — the marker type; the collection name is the suite name
  required by TASK-553's DoD.

Tagged all 7 classes `[Collection("TENANT_ISOLATION_TESTS")]` and removed their duplicated
`DO $$ ... CREATE ROLE ... END $$; GRANT ...` block from `InitializeAsync`, replacing it with a
check on the injected `RlsAuditRoleFixture.DbAvailable` (soft-skip immediately if false, same
soft-skip convention as before) followed by each class's own connection/`NpgsqlDataSource` setup
(unchanged — only the role-bootstrap DDL was removed, not each class's own per-test-method
connection logic). Two xUnit guarantees make this airtight rather than just narrowing the window:
collection-fixture `InitializeAsync` runs exactly once per run, and classes sharing a `[Collection]`
never run in parallel with each other.

## Verification

**Race fix — 5 repeated runs, filtered to the 7 formerly-racing classes (32 tests total):**

| Run | Total | Passed | Skip events (`grep -c "Skipping"`) |
|---|---|---|---|
| 1 | 32 | 32 | 0 |
| 2 | 32 | 32 | 0 |
| 3 | 32 | 32 | 0 |
| 4 | 32 | 32 | 0 |
| 5 | 32 | 32 | 0 |

Zero skip events across all 5 runs, vs. devops' pre-fix reproduction of 6 skips (run 1) and 1 skip
(run 2) out of 2 attempts. Filter used:
`FullyQualifiedName~RlsCrossTenantIntegrationTests|FullyQualifiedName~LoyaltyRlsIntegrationTests|FullyQualifiedName~LoyaltyJoinRlsIntegrationTests|FullyQualifiedName~MobileConfigDraftServiceRlsIntegrationTests|FullyQualifiedName~MobileConfigPublishedReadRlsIntegrationTests|FullyQualifiedName~MobileThemeServiceRlsIntegrationTests|FullyQualifiedName~StoreScopeRlsIntegrationTests`.

**Stage B/C/E dynamic coverage confirmed, not assumed:**
- `RlsCrossTenantIntegrationTests.AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_
  AndWorkerBypass` passed in all 5 runs (it queries `pg_policies`/`pg_class` for every table with
  `relforcerowsecurity = true`, so any table missing the triad would show up as an assertion
  failure, not a silent gap).
- Confirmed directly against the dev DB (`docker exec ... psql`) that `mobile_configurations`,
  `mobile_configuration_versions`, `mobile_themes` all have `relrowsecurity = t` AND
  `relforcerowsecurity = t` — i.e. the dynamic test actually inspects them, not skips them for
  lacking FORCE RLS (which would have been a false-coverage gap of the same shape this audit test
  exists to catch).
- Confirmed all three carry `tenant_isolation` (with NULLIF), `provider_bypass`, `worker_bypass`
  directly via `pg_policies` — matches the dynamic test's own assertion.

**CI gating:** No `.github/workflows/ci.yml` change needed/made. The suite runs as part of the
normal `dotnet test` step devops-engineer's CI change already gates the build on — no extra
`--filter` needed, since excluding this collection was never how it ran; `[Collection]` only
affects execution ordering/sharing, not whether `dotnet test` picks the tests up. A test failure
anywhere in `TENANT_ISOLATION_TESTS` fails the same `dotnet test` invocation as everything else.

**Build/full suite (re-verified at the end):**
- `dotnet build ShelfGuard.sln` — 0 errors, 1 pre-existing unrelated warning
  (`MarketplaceServiceTests.cs:534`, nullable dereference, untouched by this task).
- `dotnet test ShelfGuard.Tests` (full suite, real Postgres, `--no-build`) — **1685/1685 passed**,
  0 skip events in the log (matches devops' pre-existing 1685/1685 baseline).

## Files changed

- `backend/ShelfGuard.Tests/Infrastructure/RlsAuditRoleFixture.cs` — new, the shared fixture +
  collection definition.
- `backend/ShelfGuard.Tests/Infrastructure/RlsCrossTenantIntegrationTests.cs`,
  `LoyaltyRlsIntegrationTests.cs`, `StoreScopeRlsIntegrationTests.cs`,
  `LoyaltyJoinRlsIntegrationTests.cs`, `MobileConfigDraftServiceRlsIntegrationTests.cs`,
  `MobileConfigPublishedReadRlsIntegrationTests.cs`, `MobileThemeServiceRlsIntegrationTests.cs` —
  tagged `[Collection("TENANT_ISOLATION_TESTS")]`, constructor now takes `RlsAuditRoleFixture`,
  removed each class's own `CREATE ROLE`/`GRANT` DDL.

No changes to `.github/workflows/ci.yml` or any non-test source file. `git status` also shows
pre-existing unrelated dirty state (`Auth/LoyaltyServiceTests.cs`,
`Infrastructure/MobileConfigPublishConcurrencyIntegrationTests.cs`) from before this task started —
neither was touched here.
