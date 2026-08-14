# TASK-520: Banner schema (Consumer App plan — banners, promoted products, view/click log)

**Agent:** database-engineer
**Date:** 2026-08-14
**Status:** done — created, migrated, and live-verified against the real (non-superuser) app role. No blocker.

## Context

Schema-only slice of the Consumer App plan (`C:\Users\stass\.claude\plans\quirky-questing-hoare.md`).
Backs the "banners" block on the consumer home feed (currently hardcoded `CONSUMER_NEWS` in
`mobile/features/loyalty/news.ts`). TASK-521 (backend-developer, blocked on this task) builds the
service/controller layer on top; TASK-522 (frontend-developer) builds the admin UI.

## Done

- `backend/ShelfGuard.Domain/Entities/Banner.cs` (new) — private-setter + static `Create(...)`
  factory, styled after `Discount.cs` per the brief. Fields per spec: `Title`/`Eyebrow?`/
  `Description`/`Body`/`Terms` (Body/Terms are plain "\n"-joined text, not JSON — matches how the
  mock data already shapes these), `ImageUrl?`/`Icon`/`BackgroundColor`/`AccentColor` (image with
  icon+color fallback), `DetailMode` (`internal`/`external`, see `BannerDetailMode` consts)/
  `ExternalUrl?`, `ValidFrom`/`ValidUntil?`, `IsActive` (manual pause, never a hard-delete flag),
  `SortOrder`, `CreatedBy?`/`CreatedAt`/`UpdatedAt?`. `IsCurrentlyActive(DateTime utcNow)` mirrors
  `DemandEvent.IsActiveOn`'s computed-status pattern (`IsActive && utcNow >= ValidFrom && (ValidUntil
  == null || utcNow <= ValidUntil)`). Also added `Update(...)`/`SetImageUrl(...)`/`SetActive(...)`
  mutation methods — not explicitly listed in the brief's field list, but needed for TASK-521's
  planned PUT/image-upload/soft-DELETE endpoints to have somewhere to put those state transitions
  without reaching into private setters; kept in the same encapsulated-mutation style `Discount`
  already uses for its own transitions (`Approve`/`Cancel`/`MarkWebhookSent`).
- `backend/ShelfGuard.Domain/Entities/BannerLocation.cs` (new) — `Id`/`TenantId`/`BannerId`/
  `LocationId` only, zero navigation properties either side, styled directly after `UserLocation.cs`
  per the brief (including its "no CreatedAt/audit field" shape, since the brief's own field list
  for this table doesn't include one and there's no "who assigned this" concept for a banner→
  location join the way there is for a user→location grant).
- `backend/ShelfGuard.Domain/Entities/BannerProduct.cs` (new) — `Id`/`TenantId`/`BannerId`/`ItemId`/
  `SortOrder`, same shape as `BannerLocation`.
- `backend/ShelfGuard.Domain/Entities/BannerEvent.cs` (new) — `Id`/`TenantId`/`BannerId`/
  `EventType` (`view`/`click`, `BannerEventType` consts)/`ConsumerAccountId?` (nullable — anonymous
  events allowed)/`OccurredAt`. Append-only: only a `Create(...)` factory, no update/delete methods,
  per the brief (analytics computed via `COUNT(...) GROUP BY EventType` on read in TASK-521, no
  denormalized counter to maintain here).
- `AppDbContext.cs` — 4 new DbSets + 4 fluent config blocks, inline in `OnModelCreating` (confirmed
  this codebase has zero `IEntityTypeConfiguration<T>` classes anywhere — grepped before starting,
  all config lives in `AppDbContext.cs` itself, matched that convention). `Banner` gets real
  `Tenant`/`Creator` navigation (mirrors `Discount`); `BannerLocation`/`BannerProduct`/`BannerEvent`
  use `HasOne<T>().WithMany()` with no navigation property, mirroring `UserLocation`'s exact wiring
  style. Added a unique `(BannerId, ItemId)` index on `banner_products` — not explicitly required by
  the brief (only `banner_locations`' unique constraint was spelled out), but the same data-
  integrity concern applies (stop a product being attached twice to one banner) — flagged here as a
  judgment call, not silently added.
- Migration `AddBannersSchema` (`20260814055748`) — canonical RLS triad on all 4 tables
  (`tenant_isolation` NULLIF-guarded + `provider_bypass IN ('provider','provider_admin')` +
  `worker_bypass`), added in the same migration per the brief's explicit "not as a follow-up"
  instruction. No `consumer_self_access` policy — consumer reads of banners go through the tenant's
  own `app.tenant_id` context set by the (future, TASK-521) consumer-content controller, the same
  way `ConsumerLoyaltyController`'s public reads work, not through `app.consumer_account_id` identity
  matching (that pattern is specific to a consumer reading *their own* loyalty rows, not public
  marketing content).

## Indexes

- `idx_banners_tenant_active_sort` (TenantId, IsActive, SortOrder) — consumer feed query shape.
- `uq_banner_locations_banner_location` (unique, BannerId+LocationId) — per brief.
- `idx_banner_locations_tenant_location` (TenantId, LocationId) — reverse lookup.
- `uq_banner_products_banner_item` (unique, BannerId+ItemId) — judgment-call addition, see above.
- `idx_banner_products_tenant_banner` (TenantId, BannerId).
- `idx_banner_events_tenant_banner_type` (TenantId, BannerId, EventType) — the analytics
  `COUNT(...) GROUP BY EventType` query shape TASK-521 will run.

## No FK-validation-under-RLS gotcha (KI-029)

All 4 tables are brand-new `CREATE TABLE`s with zero pre-existing rows, so the "ALTER TABLE ADD
CONSTRAINT against an already-populated column" false-positive `23503` doesn't apply. Applied
directly via the app's own non-superuser `shelfguard_app_dev` connection — `dotnet ef database
update` with `ConnectionStrings__DefaultConnection` set explicitly (the design-time factory's
fallback otherwise resolves to an unrelated local `postgres`/5432 role — same gotcha TASK-471 hit).
Applied cleanly on the first try, no `crm` superuser escape hatch, no grant-ownership incident.

## Verification (live, real app role, not superuser)

Ran via `docker exec -i crmproductsystems-postgres-1 psql -U shelfguard_app_dev -d crm`:

1. **Ownership** — `pg_tables`: all 4 tables owned by `shelfguard_app_dev`.
2. **Policy/flag byte-check** — `pg_class`: `relrowsecurity`/`relforcerowsecurity` both `t` on all
   4. `pg_policies`: exactly 3 policies each, correct `qual` text (NULLIF guard present,
   `provider_bypass = ANY (ARRAY['provider','provider_admin'])`, no fail-open branch).
3. **Positive path**: inserted a banner + banner_location + banner_event under tenant A, read back
   `1`/`1`/`1`, then rolled back.
4. **Cross-tenant isolation**: same rows, switched `app.tenant_id` to a genuinely different tenant
   B — `0`/`0`/`0`.
5. **Fail-closed**: fully `RESET app.tenant_id; RESET app.role;` (no session vars at all) →
   `SELECT count(*) FROM banners` returns `0`, not every row.
6. **Bypass roles**: `provider` and `worker` both see the cross-tenant test row (`1` each).
7. **Unique backstop**: a second `(BannerId, LocationId)` insert correctly raised `duplicate key
   value violates unique constraint "uq_banner_locations_banner_location"`.
8. **Cascade**: deleting the parent banner correctly cascaded away its `banner_locations` row (`0`
   remaining) — confirms `BannerId -> banners Cascade` fires.
9. All test transactions rolled back — dev DB left clean, no residue.
10. `dotnet build ShelfGuard.sln` — 0 errors (1 pre-existing, unrelated warning in
    `MarketplaceServiceTests.cs`). `dotnet test` (full suite) — **1411/1411 green**, including
    `RlsCrossTenantIntegrationTests`'s dynamic `AllForceRlsTables_...` and
    `TenantIsolationPolicies_HaveNoFailOpenBranch_...` audits, which enumerate every FORCE-RLS table
    at query time and automatically picked up all 4 new ones — no new xUnit file needed, same
    precedent as TASK-419/TASK-471.

## Not in scope (per brief)

- No service/controller/repository/DTO code — TASK-521 (backend-developer), blocked until this task
  landed.
- No frontend files — TASK-522 (frontend-developer), blocked on TASK-521.
- No handoff doc from this task — the shared plan file (`quirky-questing-hoare.md`) already gives
  TASK-521 everything it needs about this schema.
- `.claude/docs/database-schema.md` not updated — consistent with TASK-419/TASK-471 precedent
  (schema-doc updates deferred to a documentation-writer pass once the full feature ships), and not
  listed as a deliverable in this task's brief.

## Git

Not committed — working tree left for review (repo convention: main session/user commits).

## Files

- `backend/ShelfGuard.Domain/Entities/Banner.cs` (new)
- `backend/ShelfGuard.Domain/Entities/BannerLocation.cs` (new)
- `backend/ShelfGuard.Domain/Entities/BannerProduct.cs` (new)
- `backend/ShelfGuard.Domain/Entities/BannerEvent.cs` (new)
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs` (4 DbSets + 4 fluent config blocks added)
- `backend/ShelfGuard.Infrastructure/Migrations/20260814055748_AddBannersSchema.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/20260814055748_AddBannersSchema.Designer.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` (regenerated by
  `dotnet ef migrations add`, new-entity metadata only)
