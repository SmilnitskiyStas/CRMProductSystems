# Handoff 392 → 392b (backend-developer)

TASK-392 Stage 1 (database-engineer) landed the schema for store-scoped user↔location
assignment. This is additive/no-op — nothing reads this table for access control yet.
This handoff is for whoever builds the API/service layer on top (TASK-392b or similar).

## What exists now

- **`backend/ShelfGuard.Domain/Entities/UserLocation.cs`** — `Id`, `TenantId`, `UserId`,
  `LocationId`, `AssignedByUserId` (nullable, who granted it), `CreatedAt`. Private
  setters + static `Create(tenantId, userId, locationId, assignedByUserId)`. No
  navigation properties (matches `TenantRole.CreatedByUserId`'s bare-FK style) — if you
  need `.Include()`-able navigation for a "user + their locations" query, you'll need to
  add it yourself (additive, no new migration needed since the FK/column already exist).
- **`DbSet<UserLocation> UserLocations`** on `AppDbContext`.
- **Table `user_locations`**: unique `(TenantId, UserId, LocationId)` (prevents dupes —
  lean on this for idempotent "assign" upserts), secondary `(TenantId, LocationId)` for
  reverse lookup ("who covers this location"). Standard RLS
  (tenant_isolation/provider_bypass/worker_bypass) — nothing store-scoped yet.
- **`User.StoreId`** now correctly maps to physical column `LocationId` (FK, SetNull).
  This is a UI/invite-time "default home location" hint ONLY — do not read it for access
  control, and do not repurpose it as a substitute for `user_locations` rows. The FK was
  added as `NOT VALID` (zero-downtime pattern — enforced for new/updated rows immediately,
  existing rows not yet validated) specifically to avoid crashing the app on startup in
  production — see KI-029 in `known-issues.md` before you add any FK of your own to an
  already-populated column referencing an RLS-protected table; it's a real, previously-hit
  failure mode, not a theoretical one.

## The access model you're implementing on top of this

Per project-architect's design (already confirmed with product owner, not up for
re-litigation):

- **`enterprise_admin`** sees every location in the tenant, unconditionally, via an
  `app.role`/role check — it gets **zero** rows in `user_locations`. Don't write rows for
  this rank; don't expect to find any when reading.
- **Every other rank** (network_manager, store_manager, merchandiser, storekeeper,
  cashier, staff) — **including single-location roles** — needs **exactly one row** per
  location it can access. A store_manager who only ever works one store still gets 1 row,
  not a shortcut through `User.StoreId`. This is deliberate: one enforcement mechanism,
  not two.

## What's explicitly NOT done yet (yours or a later task's to do)

1. **No enforcement.** No RESTRICTIVE RLS store_scope policy anywhere reads
   `user_locations` yet. That's Stage 3, a separate migration on `product_stock`/
   `daily_sales`/`pos_shifts`/etc — a database-engineer task, not this one.
2. **No `app.user_id` session variable.** Any RLS policy that will eventually EXISTS-
   subquery into `user_locations` needs to know *which user* is running the current
   query — today `TenantConnectionInterceptor` only sets `app.tenant_id`/`app.role`, not
   a per-user id. Adding that session var is presumably part of your task, not schema.
3. **No service/repository/controller code** touches `UserLocation` at all yet — you're
   building that from scratch (invite-time assignment, admin UI for managing a user's
   location list, whatever the actual feature surface is).

## One thing worth knowing before you touch `AppDbContext.cs`

While generating this migration, a second, unrelated concurrent task (TASK-391,
`TenantRole.AllowedTabs`) was editing the same `AppDbContext.cs`/`Migrations/` folder at
the same time, with no git-worktree isolation between the two agent sessions. It got
resolved cleanly (each migration ended up scoped correctly, verified via `dotnet build` +
858/858 tests + inspecting the applied local-dev schema), but it cost real time and one
close call (a real, already-committed migration was transiently deleted mid-race and had
to be restored via `git checkout`). If another agent is likely to be touching
`AppDbContext.cs`/`Migrations/` concurrently with your work, ask for a `git worktree`
before you start — CLAUDE.md already mandates this for Codex parallelism; it apparently
isn't automatic for parallel Claude agent sessions either.

## Verification already done (don't need to redo)

- `dotnet build` — 0 errors.
- `dotnet test` — 858/858 passed (after migrations applied).
- All three migrations applied cleanly to local dev Postgres (`crmproductsystems-postgres-1`,
  port 5435) **through the actual restricted `shelfguard_app_dev` role** (not the `crm`
  superuser) — the same non-superuser connection production uses for
  `db.Database.MigrateAsync()` at container startup. Schema, FKs (including the `NOT VALID`
  state), indexes, RLS, and live FK-enforcement were all confirmed via direct `psql`
  inspection.

Full detail (including the concurrency incident, and the RLS/FK-validation production-outage
risk this task found and fixed — now tracked as KI-029 in `known-issues.md` for any future
migration that adds an FK to a possibly-populated column referencing an RLS-protected table)
is in `.claude/logs/tasks/392_2026-07-19_user-locations-schema_database-engineer.md`.
