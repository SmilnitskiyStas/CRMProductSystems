# TASK-545 — Version History + Rollback

**Status:** done
**Agent:** backend-developer
**Date:** 2026-08-18
**Depends:** TASK-544 (generalized Draft→Validate→Publish)

## Scope delivered

1. **Archiving (Part 1).** `MobileConfigPublishService.PublishAsync` now archives the previously-
   published version (if one exists) in the same atomic `SaveChangesAsync` call as everything else,
   via the existing-but-previously-unused `MobileConfigurationVersion.Archive()` (TASK-531). No-op on
   a tenant's first-ever publish (`PublishedVersionId` still null).
2. **Version History query (Part 2).** New `IMobileConfigVersionHistoryService`/
   `MobileConfigVersionHistoryService`, backed by a new repository method
   `IMobileConfigurationRepository.GetVersionsForTenantAsync` (untracked, ordered by `Version`
   descending). New `GET /api/v1/mobile/config/versions` (`AtLeastEnterpriseAdmin`) returns
   `Id/Version/Status/CreatedAt/PublishedAt/CreatedBy` for every version — draft, published, and
   archived, none ever deleted.
3. **Rollback (Part 3).** New `MobileConfigPublishService.RollbackAsync` + new repository method
   `GetVersionByIdAsync` (tenant-scoped, tracked). New
   `POST /api/v1/mobile/config/versions/{versionId}/rollback`.

## Refactor: shared publish tail

Extracted a private `PublishVersionAsync(config, tenantId, draftRowToPublish, themelessBodyJson,
schemaVersion, theme, actingUserId, ct)` helper from `PublishAsync`'s body — archive-previous /
compose-theme / mark-published / clone-new-draft / repoint-pointers / atomic-save, all in one
`SaveChangesAsync` call. Both `PublishAsync` and `RollbackAsync` call it; `PublishAsync`'s own
public behavior is otherwise unchanged (same validation-then-compose order, same DTO shape).

`theme` is passed into the helper explicitly by the caller rather than re-derived from
`config.Theme` inside it. Reason: `RollbackAsync` may need to persist a brand-new `MobileTheme` row
(when the tenant had none) via `AddThemeAsync` earlier in the same unit of work — relying on EF
Core's navigation-fixup timing to make that instance reachable again through `config.Theme` before
the helper runs was an avoidable correctness risk, so the resolved `MobileTheme` instance is
threaded through as a parameter instead.

## Rollback sequence (as implemented in `RollbackAsync`)

1. Reject if `targetVersionId` is the tenant's current `PublishedVersionId` or `DraftVersionId` →
   `CannotRollbackToCurrentVersion`. Reject if the tenant has no `MobileConfiguration` row, or the
   target id doesn't exist for this tenant → `VersionNotFound`.
2. `MobileThemeJson.SplitTheme(target.ConfigurationJson)` — new helper, the inverse of
   `MobileConfigPublishService.ComposeTheme` — splits the historical (already-composed) document
   back into its `theme` node and the remaining theme-less body.
3. **Validation-bypass reasoning, made explicit and testable:** validating the target's WHOLE
   document (theme still attached) as-is would always fail — `MobileConfigValidator`'s top-level
   whitelist has no `theme` key (TASK-532's decision, matching every draft's shape) — regardless of
   whether the document was valid at its original publish time. Splitting theme out FIRST avoids
   that trap entirely. The theme-less body is then defensively re-validated anyway: this is expected
   to always pass in real usage (it already passed this same validator once, at the target's
   original publish time, and a non-Draft version is immutable so the shape cannot have drifted
   since) — kept as cheap defense-in-depth against a future stricter validator revision, and it can
   never re-trigger the theme-key rejection because theme is already removed by this point. Proven
   by `RollbackAsync_succeeds_for_a_historical_theme_containing_version_that_naive_whole_document_
   validation_would_reject`, which asserts the naive whole-document validation fails while the real
   rollback call succeeds for the exact same source document.
4. **Theme restoration, required not cosmetic:** the extracted `theme` node is applied onto the
   tenant's LIVE `MobileTheme` row (new `MobileThemeJson.ApplyTo` helper) — new row created via
   `AddThemeAsync` if none existed yet, otherwise `UpdateTheme` on the existing row — BEFORE calling
   the shared publish helper. This has to happen first: `PublishAsync` always composes a version's
   theme from whatever `MobileTheme` currently holds, so skipping this step would mean the very next
   *normal* publish silently overwrites the rollback's restored theme with the tenant's pre-rollback
   value, partially undoing the rollback. A target with no `theme` key at all (legacy/pre-TASK-544
   row) leaves the live `MobileTheme` untouched — defensive path only.
5. Reuses the shared `PublishVersionAsync` helper with `draftRowToPublish = config.DraftVersion` —
   the tenant's current draft row is repurposed to carry the restored historical content, same
   "mutate the one draft slot into published, then clone a fresh draft forward" mechanics a normal
   publish uses. Consequence, documented in the interface XML docs: any unpublished edits sitting in
   the current draft are superseded, exactly as their own prior content is what a normal publish
   supersedes with nothing lost — for rollback, "nothing lost" doesn't hold for the draft's own
   edits, since it is now carrying the historical body instead. This was a deliberate reading of the
   brief's "reuse the same...structure" instruction, not an oversight; a tenant with rollback-able
   history always has a non-null current draft by the TASK-544 invariant (every publish leaves a
   fresh draft behind), so the alternative "no draft to repurpose" is a defensive
   `InvalidOperationException`, not a normal user-facing error path (covered by a dedicated test).
6. Concurrency: unchanged mechanism — same `SaveChangesAsync` call, same xmin/unique-index
   translation to `ConcurrencyConflictException` → `ConcurrentPublish`, proven at the service level
   by a mocked-repository unit test (matching `PublishAsync`'s existing concurrency test shape). No
   new real-Postgres integration test was added for rollback specifically: the exact same repository
   `SaveChangesAsync` code path is exercised for both publish and rollback (no new Postgres-level
   behavior was introduced), so the existing `MobileConfigPublishConcurrencyIntegrationTests`
   Postgres proof already covers the mechanism; that file's `RendezvousMobileConfigurationRepository`
   test double was updated to implement the two new repository interface members (compile
   requirement only, no new integration scenario).

## One existing test deliberately changed (not silently left broken)

`MobileConfigPublishServiceTests.PublishAsync_never_touches_the_previous_published_version` asserted
TASK-544's own deliberate deferral verbatim ("previous published version... never archived, not
mutated... TASK-545's decision, not this one's") — i.e. it encoded the exact OLD contract Part 1's
DoD explicitly requires changing. Updated in place (renamed to
`PublishAsync_archives_the_previously_published_version_when_one_exists`, assertions flipped from
"still published, `DidNotReceive`" to "now archived, `Received(1)`") rather than left failing or
deleted silently. Every other pre-existing test in the file passes unmodified.

## Files changed

- `backend/ShelfGuard.Domain/Interfaces/IMobileConfigurationRepository.cs` — added
  `GetVersionByIdAsync`, `GetVersionsForTenantAsync`.
- `backend/ShelfGuard.Infrastructure/Data/Repositories/MobileConfigurationRepository.cs` —
  implemented both (no schema change; both are plain queries against existing columns/indexes).
- `backend/ShelfGuard.Application/Features/MobileConfig/MobileThemeJson.cs` — added `SplitTheme`
  (inverse of `ComposeTheme`) and `ApplyTo` (theme-node → live `MobileTheme` row).
- `backend/ShelfGuard.Application/Features/MobileConfig/IMobileConfigPublishService.cs` — added
  `RollbackAsync`.
- `backend/ShelfGuard.Application/Features/MobileConfig/MobileConfigPublishService.cs` — Part 1
  archiving, `RollbackAsync`, shared `PublishVersionAsync` helper; class remarks extended with the
  TASK-545 decisions (archiving / validation-bypass reasoning / theme-restoration requirement).
- `backend/ShelfGuard.Application/Features/MobileConfig/Dtos/MobileConfigPublishDtos.cs` — added
  `VersionNotFound`/`CannotRollbackToCurrentVersion` to `MobileConfigPublishErrorType`.
- `backend/ShelfGuard.Application/Features/MobileConfig/Dtos/MobileConfigVersionHistoryDtos.cs`
  (new) — `MobileConfigVersionSummaryDto`/`Response`.
- `backend/ShelfGuard.Application/Features/MobileConfig/IMobileConfigVersionHistoryService.cs` (new).
- `backend/ShelfGuard.Application/Features/MobileConfig/MobileConfigVersionHistoryService.cs` (new).
- `backend/ShelfGuard.Api/Controllers/MobileConfigVersionsController.cs` (new) — `GET .../versions`,
  `POST .../versions/{versionId}/rollback`, same `AtLeastEnterpriseAdmin`/`ITenantContext` pattern as
  `MobileConfigPublishController`/`MobileConfigDraftController`.
- `backend/ShelfGuard.Application/DependencyInjection.cs` — registered
  `IMobileConfigVersionHistoryService`.
- `backend/ShelfGuard.Tests/Infrastructure/MobileConfigPublishConcurrencyIntegrationTests.cs` —
  `RendezvousMobileConfigurationRepository` implements the two new interface members (pass-through).
- `backend/ShelfGuard.Tests/MobileConfig/MobileConfigPublishServiceTests.cs` — one test updated (see
  above), 12 new tests (2 archiving, 10 rollback: not-found/cannot-rollback-to-current ×2/
  validation-bypass proof/archiving-on-rollback/property-2-clone/theme-restoration proof/legacy-no-
  theme path/concurrency/defensive-no-draft-row).
- `backend/ShelfGuard.Tests/MobileConfig/MobileConfigVersionHistoryServiceTests.cs` (new, 3 tests).
- `backend/ShelfGuard.Tests/MobileConfig/MobileConfigVersionsControllerTests.cs` (new, 10 tests).

No `AppDbContext.cs` change and no new EF migration — `Archive()`, `MobileTheme.Update()`, and every
column/index this task needed already existed from TASK-531/544.

## Verification

- `dotnet build ShelfGuard.sln` — 0 errors (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`).
- `dotnet test ShelfGuard.sln` — **1636/1636 passed** (1611 pre-existing + 25 new/changed).
- `git status` confirms changes are confined to the `MobileConfig` feature area (Application/Domain/
  Infrastructure/Api/Tests) plus DI registration — no unrelated files, no migrations, no docs edited
  (this repo's mobile-config Stage 6 work as a whole is still uncommitted from prior tasks, so a
  clean pre/post diff isn't available from git alone; verified instead by enumerating every file this
  session's Write/Edit calls actually touched).

## Next

TASK-546 (frontend-developer) — Version History UI + rollback action, consuming
`GET /api/v1/mobile/config/versions` and `POST /api/v1/mobile/config/versions/{versionId}/rollback`.
