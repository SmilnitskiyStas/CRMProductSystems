# TASK-523: Banner.PublishedAt (draft/published lifecycle groundwork)

**Agent:** database-engineer
**Date:** 2026-08-14
**Status:** done — schema-only, no build/test regressions.

## Context

Follow-up to the Consumer App banners feature (TASK-520/521/522, shipped). User wants a history
view on the banners admin page: currently-running / past / draft banners. `Banner` had no
"never published" concept — `IsActive` is only a manual pause toggle, and every banner today
goes live immediately on create. This task adds only the schema needed to distinguish the three
buckets; TASK-524 (backend-developer, blocked on this) builds the publish endpoint + lifecycle
logic, TASK-525 (frontend-developer) builds the tabs UI.

## Done

- `backend/ShelfGuard.Domain/Entities/Banner.cs`:
  - Added `public DateTime? PublishedAt { get; private set; }` — `null` = draft, never
    published; non-null = first-publish timestamp. Kept intentionally separate from `IsActive`
    (manual pause) and `ValidFrom`/`ValidUntil` (display window), per brief.
  - `Create(...)` gained `bool publishImmediately = true` (trailing optional param, default
    preserves today's behavior — `PublishedAt = CreatedAt` when true, `null` when false).
    Confirmed the sole existing caller (`BannerService.cs:42`) uses named arguments, so the new
    param is a no-op for it.
  - Added `Publish(DateTime utcNow)` — idempotent, only sets `PublishedAt` (+ `UpdatedAt`) when
    currently null; a second call is a no-op and never overwrites the original timestamp. Backs
    TASK-524's `POST /api/banners/{id}/publish`.
  - `Update(...)` intentionally left untouched — no `publishedAt` param, per brief (publishing
    only happens via `Publish()`).
- `AppDbContext.cs` — added `e.Property(b => b.PublishedAt);` to the existing `Banner` fluent
  config block (nullable `timestamp with time zone`, no default).
- Migration `AddBannerPublishedAt` (`20260814083429`) — single `ALTER TABLE banners ADD COLUMN
  "PublishedAt" timestamp with time zone NULL`. No RLS changes (existing triad on `banners`
  already covers all columns).

## Verification

- Had to stop a stale `ShelfGuard.Api.exe` (PID 35752) that was locking build output DLLs from an
  earlier session before `dotnet ef migrations add` could build — otherwise unrelated to this
  change.
- `dotnet ef database update` applied cleanly against the real dev DB via the non-superuser
  `shelfguard_app_dev` app role (same DB/role TASK-520 verified against). Confirmed via `\d
  banners`: `PublishedAt` column present, nullable, no default; RLS policies (`tenant_isolation`/
  `provider_bypass`/`worker_bypass`) unchanged.
- `dotnet build ShelfGuard.sln` — 0 errors (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`, same as TASK-520).
- `dotnet test` (full suite) — **1411/1411 green**, no regressions from the additive nullable
  column.

## Not in scope (per brief)

- No service/controller/DTO changes — TASK-524 (backend-developer), blocked until this task
  landed, now unblocked.
- No frontend changes — TASK-525 (frontend-developer), blocked on TASK-524.
- `.claude/docs/database-schema.md` not updated — same deferred-to-doc-pass precedent as
  TASK-520/419/471.

## Git

Not committed — working tree left for review (repo convention: main session/user commits).

## Files

- `backend/ShelfGuard.Domain/Entities/Banner.cs` (modified — `PublishedAt` property, `Create(...)`
  `publishImmediately` param, `Publish(...)` method)
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs` (modified — 1 line added to `Banner`
  config block)
- `backend/ShelfGuard.Infrastructure/Migrations/20260814083429_AddBannerPublishedAt.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/20260814083429_AddBannerPublishedAt.Designer.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` (regenerated,
  `PublishedAt` metadata only)
