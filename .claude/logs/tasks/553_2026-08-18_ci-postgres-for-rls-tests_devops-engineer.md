# TASK-553 (CI/infra half) — Postgres service for RLS integration tests in backend-ci

**Status:** done (this half only — backend-developer's test-suite-consolidation half follows separately)
**Agent:** devops-engineer

## Problem

`RlsCrossTenantIntegrationTests` and 14 other Postgres-backed integration test classes (grep
`ShelfGuard.Tests` for `IAsyncLifetime`/`NpgsqlConnection`) soft-skip (pass trivially) whenever no
reachable Postgres is configured. `backend-ci` in `.github/workflows/ci.yml` had no Postgres service
at all, so every one of these classes silently did nothing on every push/PR. TASK-553's DoD requires
this suite to gate CI, which is impossible without a real DB in the job.

## Change

`.github/workflows/ci.yml`, `backend-ci` job only:

1. Added a `postgres:16-alpine` service container — `POSTGRES_DB=crm`, `POSTGRES_USER=crm`,
   `POSTGRES_PASSWORD=crm_dev_password`, mapped `5435:5432`, with a `pg_isready` health check gating
   job startup. These values and the port intentionally mirror `docker-compose.yml`'s local dev
   Postgres exactly, and match every affected test class's hardcoded `DefaultConnectionString`
   (`Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password`) — so the tests
   resolve to the CI service with **zero code changes and zero env-var overrides**.
2. Added two steps before `Test`: install `dotnet-ef` (global tool, pinned to `8.0.11` to match
   `Microsoft.EntityFrameworkCore.Design`'s version in `ShelfGuard.Infrastructure.csproj` — an
   unpinned install resolves the newest dotnet-ef, which can require a newer .NET than the `8.x`
   SDK/runtime this job installs and then fail to run), then
   `dotnet ef database update --project ShelfGuard.Infrastructure --startup-project ShelfGuard.Api`
   with `ConnectionStrings__DefaultConnection` pointed at the same service (picked up by
   `AppDbContextFactory`'s `IDesignTimeDbContextFactory`, which reads that exact env var — no
   `ASPNETCORE_ENVIRONMENT`/appsettings dependency).
3. `frontend-ci`, `worker-ci`, `mobile-ci`, `deploy` jobs untouched.

No test file was touched — every affected class's connection-string default and
`SHELFGUARD_TEST_DB_CONNECTION` override convention already matched this config, so nothing needed
to change there.

## Local verification (could not trigger an actual GitHub Actions run)

Reproduced the planned CI steps as closely as practical on the dev machine:

1. Started a throwaway `postgres:16-alpine` container (fresh, no pre-existing data) with the exact
   env vars above, on a separate local port to avoid colliding with the machine's already-running dev
   Postgres container.
2. Installed `dotnet-ef` **pinned to 8.0.11** (temporarily replacing the machine's pre-existing global
   dotnet-ef 10.0.8, restored afterward) and confirmed `dotnet ef database update --project
   ShelfGuard.Infrastructure --startup-project ShelfGuard.Api` applies all 100+ migrations cleanly
   from empty, using the same `ConnectionStrings__DefaultConnection` env-var mechanism the workflow
   step uses.
3. Ran `dotnet build` then `dotnet test --no-build --verbosity normal` against the freshly migrated
   DB, twice.
   - **Both runs: 1685/1685 tests passed, 0 failed.**
   - Confirmed the DB-dependent code paths actually executed (not just "no failures"): real
     multi-second-to-tens-of-ms execution times on RLS/concurrency tests (e.g.
     `LoyaltyConcurrencySalesIntegrationTests` producing a genuine concurrent-redemption 409 outcome,
     `AudienceBuilderRepositoryIntegrationTests`/`PriceSegmentsRepositoryIntegrationTests` returning
     real query results), and — separately — direct confirmation the dynamic
     `AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass` test and the
     other `RlsCrossTenantIntegrationTests` facts ran against live `pg_policies`/`pg_class`, not a
     soft-skip stub.
4. Not verified (could not be, without an actual Actions run): GitHub-hosted-runner-specific
   behavior — service-container networking quirks, `~/.dotnet/tools` PATH availability for the global
   tool install on the actual runner image, exact service-container startup timing under load. These
   are standard, widely-used patterns (service containers + global dotnet tool install), but flagging
   the gap between local Docker reproduction and an actual Actions run explicitly.

## Unrelated finding — NOT fixed, reporting only

**A pre-existing race condition in the shared-role-creation pattern used by all 7 `rls_audit_test_role`-creating test classes** (`RlsCrossTenantIntegrationTests`, `LoyaltyRlsIntegrationTests`,
`LoyaltyJoinRlsIntegrationTests`, `MobileConfigDraftServiceRlsIntegrationTests`,
`MobileConfigPublishedReadRlsIntegrationTests`, `MobileThemeServiceRlsIntegrationTests`,
`StoreScopeRlsIntegrationTests`) causes some of them to **non-deterministically soft-skip even when
Postgres is fully reachable**, once real parallel test execution against a real DB is possible (this
literally could not happen before — no CI Postgres meant every one of these classes always hit the
"unreachable" branch for the same, single, expected reason).

Root cause: each class's `IAsyncLifetime.InitializeAsync` runs the same non-atomic
`DO $$ IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'rls_audit_test_role') THEN CREATE ROLE
rls_audit_test_role ... END $$; GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO
rls_audit_test_role;` against the **same shared Postgres cluster** — roles are cluster-wide, not
per-database, and xUnit runs `IAsyncLifetime` per test instance in parallel across test classes by
default. When two of these fixtures' `InitializeAsync` calls interleave, one throws:
- `23505: duplicate key value violates unique constraint "pg_authid_rolname_index"` (the
  check-then-create TOCTOU race), or
- `XX000: tuple concurrently updated` (concurrent catalog DDL from the `GRANT` statement).

Both land inside the fixture's broad `catch (Exception ex)` block, which sets `_dbAvailable = false`
and logs `"Skipping ... — no reachable Postgres..."` — **misattributing a real concurrency bug in the
test harness as "DB unreachable,"** and silently soft-skipping that class's assertions for that run
(soft-skip = pass in this codebase's convention, no xUnit `Skip`, so it does not fail the build).

Reproduced on **every** local run (2/2), with a different, non-deterministic subset of the 7 classes
affected each time:
- Run 1: 6 skip events across `LoyaltyRlsIntegrationTests` (×2), `StoreScopeRlsIntegrationTests`,
  `MobileConfigPublishedReadRlsIntegrationTests`, and `RlsCrossTenantIntegrationTests` (×2).
- Run 2: 1 skip event, `LoyaltyRlsIntegrationTests` only.

Impact: even after this CI change lands, "CI green" does **not** guarantee every RLS assertion in
every one of these 7 classes executed on that particular run — a random subset can silently no-op,
and which subset varies run to run. The majority of coverage does run correctly (1685/1685 passed
both times, and the bulk of the RLS/concurrency classes completed with real multi-second Postgres
timings), so this is a coverage-flakiness gap, not a currently-failing build. Out of this task's
scope to fix (would mean editing 7 test files' fixture logic, explicitly excluded by the brief) —
flagging for the backend-developer half of TASK-553 or a follow-up task. A likely fix shape (not
implemented here): serialize the role-bootstrap via `pg_advisory_lock`, or have exactly one shared
fixture (`ICollectionFixture`) own role creation instead of each class doing it independently.

## Files changed

- `.github/workflows/ci.yml` — `backend-ci` job only (Postgres service + migration-apply step).

No other files changed. `git diff --stat` confirms: 1 file, 34 insertions, 0 deletions.
