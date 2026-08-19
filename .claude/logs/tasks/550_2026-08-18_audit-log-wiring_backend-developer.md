# TASK-550 — Audit log wiring for consumer-platform events

**Status:** done
**Agent:** backend-developer (database-engineer co-listing checked and found unnecessary — see below)
**Depends:** TASK-544, TASK-545 (both already `done`)

## Decision 3 recap (already resolved 2026-08-17)

Reuse the existing generic `ActivityLog` table (`backend/ShelfGuard.Domain/Entities/ActivityLog.cs`)
for the new consumer-platform events. No new table, no migration. Confirmed no genuine
database-engineer work exists for this task (same finding pattern as TASK-541): `ActivityLog` already
has the columns needed (`TenantId`, `UserId`, `Action`, `EntityType`, `EntityId`, `Meta`), an index on
`(TenantId, CreatedAt)` and `(TenantId, UserId, CreatedAt)` from the Block 16 pre-launch audit, and no
FK constraints on `UserId`/`TenantId` to worry about. Proceeded as backend-developer alone.

## Scope decisions (four DoD categories)

### 1. Mobile config changed / published / rolled back — wired, in scope

- `MobileConfigDraftService.SaveDraftAsync` → `mobileconfig.draft_saved` on every successful save.
  `EntityType = "mobile_configuration_version"`, `EntityId` = the draft version's id, `Meta =
  {"version":N}`.
- `MobileConfigPublishService.PublishAsync` → `mobileconfig.published`, same entity shape, `Meta =
  {"version":N}` (the newly published version number).
- `MobileConfigPublishService.RollbackAsync` → `mobileconfig.rolled_back`, `Meta` records
  `rolledBackToVersion` (int), `rolledBackToVersionId` (guid), and `newVersion` (the freshly
  published version) — satisfies the brief's explicit "which historical version was rolled back to"
  requirement.
- Logging for Publish/Rollback happens at the `PublishAsync`/`RollbackAsync` call sites, not inside
  the shared `PublishVersionAsync` tail, so the `Action` string always reflects which public method
  the caller actually invoked even though both share the archive/compose/save mechanics.
- **`MobileThemeService.UpdateThemeAsync` — included.** Judgment call, decided yes: theme edits are
  the same "mobile config changed" admin action, just on a separate entity/service (see the
  service's own LIVE-EFFECT GAP remarks on why theme isn't draft/publish-gated). Logs
  `mobileconfig.theme_updated`, `EntityType = "mobile_theme"`, `EntityId` = the theme row id.
  Required adding `Guid? actingUserId` as `UpdateThemeAsync`'s second parameter (interface +
  implementation + `MobileThemeController` + all test call sites) to match the
  `(tenantId, actingUserId, ...)` convention `SaveDraftAsync`/`PublishAsync`/`RollbackAsync` already
  use — no other callers of `IMobileThemeService.UpdateThemeAsync` exist outside this controller.

### 2. Feature flag changed — investigated, implemented as a draft-save-time diff

No dedicated feature-flag editor endpoint exists; flags live inside the same config document's
`features` object, edited only through `SaveDraftAsync`. Decided: diff old vs. new `features` object
inside `SaveDraftAsync` and emit a separate `mobileconfig.feature_flags_changed` entry only when it
actually differs, keyed on the new boolean values of the changed keys (`Meta` = JSON object of just
the changed flags, e.g. `{"news":true}`).

Two design choices worth recording:
- **No diff on a tenant's very first-ever draft** — nothing to diff against, so nothing is logged
  beyond the generic `draft_saved` (features are being *created*, not *changed*).
- **Not duplicated into `PublishAsync`/`RollbackAsync`.** Publish never lets an admin edit document
  content — it only promotes whatever `SaveDraftAsync` already produced — so `SaveDraftAsync` is the
  only place flag *values* can actually change. Rollback's own `mobileconfig.rolled_back` entry
  already captures "the whole document changed" at a coarser grain; a redundant flag-diff there
  would just restate the same fact.
- A key that disappears between saves without a replacement value is not reported as "changed" — the
  validator requires `features` to always be present with only whitelisted keys, so silent removal is
  not a real shape worth a separate code path.

### 3. Role changed — already covered, no new wiring

Confirmed `UserService.cs` already logs role changes via the generic `LogAsync` helper it has used
since before this initiative:
- `UpdateAsync` → `user.updated`, `Meta = {"role":"<new role>"}` (line ~370, fires on every profile
  update including role changes — the role value is always in `Meta` regardless of whether it
  changed on that particular call).
- `AssignTenantRoleAsync` → `user.tenant_role_assigned`, `Meta = {"tenantRoleId":...}` (capability
  template assignment, ADR-020).

This DoD line was already satisfied before this task started. No new code added.

### 4. Promotion edited — investigated, judged out of scope

Per `docs/architecture/CURRENT_STATE.md` §6: the consumer-app's "Promotions" is a read projection
over the pre-existing `Discount` entity (`GetActivePromotionsAsync`) — no new `Promotion`/`Coupon`
domain exists. Checked `DiscountsController`/`IDiscountService` — confirmed zero `ActivityLog`
wiring anywhere in the Discounts feature (predates Stage 6 entirely, not touched by any Stage 6
task).

Judged out of scope for this task:
- The write path for discounts (`Create`/`Approve`/`Cancel`) is 100% pre-existing and unrelated to
  the consumer-platform initiative — Stage 6 only added a *read* projection on top of it.
- TASK-550's own `Depends` (TASK-544, TASK-545) and the roadmap's own decision-3 resolution text
  ("reuse ActivityLog for the new config/publish/rollback/feature-flag events") never actually
  mentions "promotion edited" in its final scoping sentence, even though the earlier DoD bullet
  list did — a signal the roadmap's own narrowing already dropped it.
- Wiring audit logging into `DiscountService` would be doing unrelated work on a feature this whole
  initiative hasn't otherwise touched, which the task brief explicitly warned against.

No code changed for this item. If a future task wants Discount-level audit coverage, it should be a
standalone, deliberately-scoped task against `DiscountService`, not folded into Stage 6 cleanup.

## Files changed

- `backend/ShelfGuard.Application/Features/MobileConfig/MobileConfigDraftService.cs` — audit logging
  + feature-flag diff, new `IActivityLogRepository` dependency.
- `backend/ShelfGuard.Application/Features/MobileConfig/MobileConfigPublishService.cs` — audit
  logging for publish/rollback, new `IActivityLogRepository` dependency.
- `backend/ShelfGuard.Application/Features/MobileConfig/IMobileThemeService.cs` /
  `MobileThemeService.cs` — audit logging, new `IActivityLogRepository` dependency, new
  `actingUserId` parameter on `UpdateThemeAsync`.
- `backend/ShelfGuard.Api/Controllers/MobileThemeController.cs` — passes `GetUserId()` (same
  claim-resolution helper the sibling Draft/Publish/Versions controllers already use) into the new
  `actingUserId` parameter.
- Tests updated for the new constructor dependency / method signature, plus new coverage:
  `MobileConfigDraftServiceTests.cs`, `MobileConfigPublishServiceTests.cs`,
  `MobileThemeServiceTests.cs`, `MobileConfigDraftServiceRlsIntegrationTests.cs`,
  `MobileConfigPublishConcurrencyIntegrationTests.cs`, `MobileThemeServiceRlsIntegrationTests.cs`.

No schema migration. `IActivityLogRepository` was already DI-registered (`AddScoped`) in
`ShelfGuard.Infrastructure/DependencyInjection.cs`, so constructor injection resolved the new
dependency with no DI wiring changes needed.

## Read-side verification

`UserService.GetActivityAsync` → `IActivityLogRepository.GetByUserAsync`, and the provider-panel
`GetFilteredAsync`, both query by `TenantId`/`UserId`/optional free-text `Action` filter with no
hardcoded action allowlist anywhere in the controllers or repository. New entries surface through
these existing paths automatically — confirmed by grep, no special-casing needed or added.

Note (non-blocking, out of this task's layer): a prior task (TASK-403,
`.claude/logs/tasks/403_..._activity-log-labels_frontend-developer.md`) added frontend label
mappings for known `Action` strings. The four new action strings introduced here
(`mobileconfig.draft_saved`, `mobileconfig.feature_flags_changed`, `mobileconfig.published`,
`mobileconfig.rolled_back`, `mobileconfig.theme_updated`) are not in that mapping yet, so the
provider Activity Log UI will show them as raw strings/fallback rather than a friendly label until a
frontend task adds them. Flagged for awareness, not fixed here (frontend layer, different agent).

## Verification

- `dotnet build ShelfGuard.sln` — 0 errors (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`).
- `dotnet test ShelfGuard.sln --no-build` — **1685/1685 passed**, 0 failed, 0 skipped (re-run twice
  for stability).
- Real-Postgres RLS/concurrency integration tests for this area (`MobileConfigDraftServiceRlsIntegrationTests`,
  `MobileThemeServiceRlsIntegrationTests`, `MobileConfigPublishConcurrencyIntegrationTests`) ran
  against a live local Postgres (confirmed by multi-second real-DB execution times and a genuine
  concurrent-publish race outcome in the output, not soft-pass skips) — proves the new
  `ActivityLogRepository` writes succeed inside these flows against the real schema, not just
  against mocks.
- `git status` confirmed changes limited to the files listed above (all new/untracked, consistent
  with the rest of this uncommitted Stage 6 work) plus this task log — no schema migration, no
  unrelated files touched.
