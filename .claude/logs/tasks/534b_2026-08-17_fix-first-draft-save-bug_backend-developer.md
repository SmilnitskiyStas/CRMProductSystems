# TASK-534b — Fix circular-dependency crash on first mobile-config draft save

**Status:** done
**Agent:** backend-developer
**Date:** 2026-08-17

## What was done

- `MobileConfigDraftService.SaveDraftAsync`
  (`backend/ShelfGuard.Application/Features/MobileConfig/MobileConfigDraftService.cs`) — the
  first-draft-creation branch (`config.DraftVersion is null`) now calls `_repo.SaveChangesAsync(ct)`
  right after `AddVersionAsync`, **before** `config.SetDraftVersion(draftVersion.Id)` /
  `_repo.Update(config)`. The existing trailing `SaveChangesAsync(ct)` after the if/else still runs
  as before, so this branch now does two `SaveChangesAsync()` calls total: insert the new
  `MobileConfiguration` (if the tenant had none) and the new `MobileConfigurationVersion` with the
  pointer still null, then a second call that sets `DraftVersionId` and saves the update. The
  `config.DraftVersion is not null` (update-existing-draft) branch is untouched — still exactly one
  `SaveChangesAsync()` call, same as before this fix.
- `MobileConfigDraftServiceTests.cs`
  (`SaveDraftAsync_creates_MobileConfiguration_and_first_draft_version_when_none_exists`) — updated
  the mocked-repo assertion from `Received(1).SaveChangesAsync(...)` to `Received(2)` to match the
  new create-path call count. No other assertions in that test, and no assertions in any
  update-path test, changed.
- New live-Postgres integration test file:
  `backend/ShelfGuard.Tests/Infrastructure/MobileConfigDraftServiceRlsIntegrationTests.cs` — same
  soft-skip-if-no-Postgres / shared-`NpgsqlDataSource` / `SET ROLE rls_audit_test_role` pattern as
  `MobileConfigPublishedReadRlsIntegrationTests.cs`, adapted for an authenticated-staff session
  (`SET app.tenant_id`, no anonymous-session RESET) since `MobileConfigDraftService` isn't
  `[AllowAnonymous]`. Two tests:
  - `SaveDraftAsync_first_ever_save_for_a_tenant_succeeds_against_real_postgres` — the actual
    regression path: brand-new tenant, no `MobileConfiguration` row, real
    `MobileConfigurationRepository` + real `AppDbContext.SaveChangesAsync`. Asserts the call
    succeeds, the returned DTO is correct, and a fresh read-back (`GetByTenantIdAsync` under a
    second session) shows the persisted `MobileConfiguration.DraftVersionId` pointing at the new
    version with the right JSON (compared via `JsonNode.DeepEquals`, not raw string equality — the
    `jsonb` column re-serializes on round-trip).
  - `SaveDraftAsync_second_save_mutates_the_same_draft_version_in_place` — a second save for the
    same tenant takes the existing update-in-place branch; confirms it still round-trips under real
    Postgres after the fix (same version row, no new version number).

## Regression proof (ran against pre-fix code first, not just reasoned about)

Reverted the fix locally (kept the new test file in place), ran
`dotnet test --filter "FullyQualifiedName~MobileConfigDraftServiceRlsIntegrationTests"`:

- First run used a random `Guid` for `actingUserId`, which failed on an unrelated FK
  (`MobileConfigurationVersion.CreatedBy` → `users`) and masked the real bug — fixed the test to
  pass `actingUserId: null` instead (that plumbing is already covered by
  `MobileConfigDraftServiceTests`' mocked unit tests, not this file's job).
- Re-ran against the still-reverted (buggy) service: both tests failed with
  `Npgsql.PostgresException: 23503` on
  `FK_mobile_configuration_versions_mobile_configurations_MobileC~` — the concrete Postgres-side
  manifestation of the `MobileConfiguration`↔`MobileConfigurationVersion` FK cycle TASK-534
  described as "circular dependency detected" (EF surfaced it as a real constraint violation here
  rather than its own client-side `InvalidOperationException`, but same root cause: both rows
  `Added` in one `SaveChangesAsync`, pointer set before either exists in the DB).
- Re-applied the fix, re-ran the same filter: both tests passed (one further fix needed — the
  read-back JSON assertion had to switch from raw string equality to `JsonNode.DeepEquals`, see
  above; unrelated to the circular-dependency bug itself).

## Verification

- `dotnet build ShelfGuard.sln` — 0 errors, 1 pre-existing unrelated warning
  (`MarketplaceServiceTests.cs` CS8602, not touched by this task).
- `dotnet test --filter "FullyQualifiedName~MobileConfig"` — 53/53 passed.
- `dotnet test ShelfGuard.sln` (full suite) — 1464/1464 passed (1462 pre-existing + 2 new). Postgres
  was reachable (docker-compose, port 5435), so both new RLS integration tests actually executed
  against real dev Postgres, not skipped.
- `git status` reviewed: this task's changes are limited to `MobileConfigDraftService.cs`,
  `MobileConfigDraftServiceTests.cs`, and the new `MobileConfigDraftServiceRlsIntegrationTests.cs`.
  The repo still carries the same large amount of pre-existing uncommitted state from earlier
  Stage 6 tasks (TASK-527/528/531/532/533/534) that TASK-534's own log already noted — none of it
  touched here.

## Files

- `backend/ShelfGuard.Application/Features/MobileConfig/MobileConfigDraftService.cs` (fix)
- `backend/ShelfGuard.Tests/MobileConfig/MobileConfigDraftServiceTests.cs` (assertion update only)
- `backend/ShelfGuard.Tests/Infrastructure/MobileConfigDraftServiceRlsIntegrationTests.cs` (new)

## Next

Stage C (TASK-535+) can rely on Draft CRUD working end-to-end against real Postgres, per TASK-534's
"Next" note. Orchestrator updates the roadmap entry to `done`.
