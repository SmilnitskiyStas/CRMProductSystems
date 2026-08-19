# TASK-544 — Generalize Draft→Preview→Validate→Publish beyond Banner

**Agent:** backend-developer
**Status:** done
**Date:** 2026-08-18

## Scope delivered

New `MobileConfigPublishService` (`backend/ShelfGuard.Application/Features/MobileConfig/MobileConfigPublishService.cs`,
interface `IMobileConfigPublishService.cs`) — `PublishAsync(tenantId, actingUserId, ct)`:

1. Loads the tenant's draft (`MobileConfigurationRepository.GetByTenantIdAsync`). No draft at all
   (no `MobileConfiguration` row, or a row with no `DraftVersion`) → `NoDraftToPublish`.
2. Validates the draft's **original** `ConfigurationJson` via `MobileConfigValidator` — deliberately
   *before* theme composition, because the validator's top-level key whitelist has no `theme` entry;
   composing first would make every publish fail validation. Invalid → `ValidationFailed` with the
   field-level errors, nothing persisted.
3. Composes the tenant's current `MobileTheme` row into a **copy** of the validated JSON (new shared
   helper `MobileThemeJson.ToJsonObject`, used by both this service and the read service's fallback
   path so the field mapping never drifts between the two).
4. In one atomic `SaveChangesAsync` call: sets the composed JSON onto the draft version (while it is
   still `Draft` status — `UpdateConfigurationJson` throws otherwise), flips it to `Published` with
   `PublishedAt`, clones the **original** (theme-less) draft content into a brand-new `Draft`-status
   row with the next version number, and repoints `MobileConfiguration.PublishedVersionId`/
   `DraftVersionId` accordingly. Any failure (validation, or a mid-transaction DB error) leaves the
   previous published version, if any, completely untouched — it is never mutated or archived by
   this service at all; only the pointer moves. Whether/how superseded versions get archived is left
   to TASK-545 (Version History).

## Theme reconciliation (property #1)

- `MobileConfigPublishedReadService` (`GET /api/v1/mobile/config`) now reads `theme` from
  `PublishedVersion.ConfigurationJson.theme` — the exact snapshot `MobileConfigPublishService`
  composed at publish time — not live from `MobileTheme` anymore.
  `IMobileConfigurationRepository.GetPublishedByTenantIdAsync` no longer `Include`s `Theme` at all
  (it's structurally unused by this read path now).
- Fallback for a published document with no `theme` key at all (only possible for a row published
  before this change, or seeded directly by a test): `MobileTheme.CreateDefault(...)`'s hardcoded
  defaults via the same `MobileThemeJson` helper — a defensive compatibility path, not the normal
  read; there is no live join in either branch.
- Net effect: `PUT /api/v1/mobile/theme` (TASK-536) now only edits the tenant's *pending* theme —
  it has no effect on real consumers until the next publish, closing the "immediate effect" gap
  TASK-536's own remarks flagged as pending this task.

## Draft continuity (property #2)

`MobileConfigDraftService.SaveDraftAsync` mutates `DraftVersion` in place. Publish never leaves
`DraftVersionId` pointing at the row it just published: it always clones the just-published
version's *original* content into a new `Draft` row first and repoints `DraftVersionId` there. The
clone is seeded from the pre-composition (theme-less) JSON, so the "a draft's `ConfigurationJson`
never carries a `theme` key" invariant holds by construction, not by stripping a key back out.
Verified by both a unit test and the real-Postgres concurrency test (below).

## Concurrency mechanism

Two lines of defense, both translating into the existing `ConcurrencyConflictException` →
`MobileConfigPublishErrorType.ConcurrentPublish` (safe-to-retry, mirrors
`PosService`/`LoyaltyService`'s existing xmin-conflict-to-409 pattern):

1. **xmin optimistic-concurrency token** on `MobileConfiguration` and `MobileConfigurationVersion`
   (`AppDbContext`, same pattern as `ProductStock`/`LoyaltyMembership` — TASK-356/TASK-414). Protects
   the pointer row and the version row Publish mutates in place from a stale concurrent writer's
   last-write-wins UPDATE. New migration `20260818124333_AddMobileConfigConcurrencyTokens` is a
   documented no-op `Up`/`Down` (xmin is a reserved Postgres system column, no real DDL — same shape
   as the two precedent migrations; the scaffolded version was manually emptied to match).
2. **Pre-existing unique index** on `(MobileConfigurationId, Version)` as a second line of defense:
   `MobileConfigPublishService` computes the cloned draft's next version number
   (`GetMaxVersionNumberAsync`) *before* its own `SaveChangesAsync`, so two publishes that both read
   that MAX before either commits can pick the same number — this surfaces as a Postgres
   unique-violation (`DbUpdateException`, not `DbUpdateConcurrencyException`), which
   `MobileConfigurationRepository.SaveChangesAsync` now also catches (matched on the specific
   constraint name) and translates the same way.

Real-Postgres proof: `MobileConfigPublishConcurrencyIntegrationTests` (mirrors
`PosConcurrencySalesIntegrationTests`/`LoyaltyConcurrencySalesIntegrationTests`'s deterministic
rendezvous-gate pattern, not timing luck) runs two concurrent `PublishAsync` calls against the same
draft and asserts: exactly one succeeds, one gets `ConcurrentPublish`, exactly one `Published`
version exists afterward, and `DraftVersionId` ends up pointing at exactly one new, distinct row (no
orphaned duplicate). Ran green 4/4 times locally against the dev Postgres.

## Publish-trigger endpoint

No publish-trigger endpoint was scoped anywhere in Stage D/E's registered task list — checked
TASK-547 (Preview API), which is explicitly staff-only `GET` of the current draft and never touches
publish. Added `POST /api/v1/mobile/config/publish` (`MobileConfigPublishController`), staff-gated
`AtLeastEnterpriseAdmin` like `MobileConfigDraftController`, as a separate controller (draft CRUD and
publish are documented as different concerns on `IMobileConfigDraftService`'s own remarks) so the
exact route stays a sibling of `/api/v1/mobile/config/draft` without an absolute-route override.
`400` for no-draft/validation-failed (validation carries `{ errors: [...] }`, matching the existing
draft-save error shape), `409` for a concurrent-publish conflict.

## Required frontend follow-up (not fixed here — backend-only task)

`frontend/features/consumer-app/components/ThemeEditorSection.tsx`'s (TASK-537) UI copy telling the
admin "changes take effect immediately... no preview or publish step yet" is now inaccurate: theme
edits are pending until the next publish, exactly like every other part of the document. This needs
a small copy/UX fix flagged for a follow-up frontend task — not done here per the backend-only scope
of this task.

## Files changed

New: `MobileConfigPublishService.cs`, `IMobileConfigPublishService.cs`, `MobileThemeJson.cs`,
`Dtos/MobileConfigPublishDtos.cs`, `MobileConfigPublishController.cs`, migration
`20260818124333_AddMobileConfigConcurrencyTokens.*`, `MobileConfigPublishServiceTests.cs`,
`MobileConfigPublishControllerTests.cs`, `MobileConfigPublishConcurrencyIntegrationTests.cs`.

Modified: `AppDbContext.cs` (xmin tokens ×2), `MobileConfigurationRepository.cs` (concurrency
translation, dropped live `Theme` include from the published-read query), `IMobileConfigurationRepository.cs`
(doc updates), `MobileConfigPublishedReadService.cs` (theme sourcing), `DependencyInjection.cs`
(service registration), `AppDbContextModelSnapshot.cs` (regenerated), `MobileConfigPublishedReadServiceTests.cs`
(rewritten theme-related cases). `MobileThemeController`/`MobileThemeService`/
`MobileConfigDraftService.cs`'s save-mutation logic were **not** touched, per the task brief.

Note: this repo currently carries a large amount of pre-existing uncommitted work from TASK-531
through TASK-543 (the whole consumer app-builder feature tree) sitting in the working tree — `git
status` on `backend/` shows several untracked directories/files that predate this task. The list
above is this task's own contribution; nothing outside it was touched.

## Verification

- `dotnet build ShelfGuard.sln` — 0 errors, 1 pre-existing unrelated warning (`MarketplaceServiceTests.cs`).
- `dotnet test ShelfGuard.sln` — **1611/1611 passed** (1594 baseline + 17 new: 9 publish-service unit
  tests, 6 publish-controller tests, 1 real-Postgres concurrency integration test, +1 net new
  published-read-service test after rewriting the theme-sourcing cases).
- Concurrency integration test re-run 4/4 green (no flakiness observed).

## Next

TASK-545 (Version History + Rollback) can build directly on this: every publish already leaves the
previous published version row completely untouched (not archived, not deleted) and every draft
save from now on targets a fresh row, so a tenant's full `MobileConfigurationVersion` history is
already queryable by `TenantId` ordered by `Version` — TASK-545 only needs to add read/rollback
behavior on top, not change how Publish itself writes.
