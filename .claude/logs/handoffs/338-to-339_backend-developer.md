# Handoff: TASK-338 database → TASK-339 backend-developer (notification filtering — repo/service layer)

**From:** database-engineer · **Date:** 2026-07-12
**DB state:** merged-ready on `main` working tree; migration
`20260712122713_ExtendNotificationQueueFiltering` auto-applies on API start. `dotnet build`
0 errors (full solution). No tests added — schema-only task.

## What exists now (schema layer only)

`ShelfGuard.Domain/Entities/NotificationQueue.cs` gained two new properties:

- `Guid? StoreId` — DB column `"LocationId"` (nullable, no FK constraint — see caveat below)
- `string? Title` — DB column `"Title"`, `varchar(255)`

Both are `init`-only, following the existing pattern on this entity (`TenantId`, `UserId`,
`Channel`, `EventType`, `Payload` are all `init`-only too — only `Status`/`RetryCount`/
`SentAt`/`Error`/`IsRead`/`ReadAt` are mutable `set`).

## New indexes (all live in `AppDbContext.OnModelCreating` → `NotificationQueue` block)

- `idx_notification_queue_tenant_createdat` — `(TenantId, CreatedAt)`
- `idx_notification_queue_tenant_eventtype` — `(TenantId, EventType)`
- `idx_notification_queue_tenant_store` — `(TenantId, LocationId)` (i.e. `StoreId` in C#)
- `idx_notification_queue_tenant_user` — `(TenantId, UserId)`
- `idx_notification_queue_title_trgm` — GIN trigram on `Title`, use `ILIKE '%term%'` or
  `%` (similarity) operator in raw SQL / EF `EF.Functions.ILike(n.Title, $"%{term}%")` to
  actually hit this index — a plain `.Contains()` translated by EF Core to `LIKE` may or
  may not use it depending on provider translation; `ILike` is the safe bet with Npgsql.

Existing pre-existing index `idx_notification_queue_tenant_status` on
`(TenantId, Status, CreatedAt)` is unchanged — still there, still used for worker queue
polling.

## What's left for you (out of database-engineer scope)

1. **`INotificationRepository`/`NotificationRepository`** (`ShelfGuard.Domain/Interfaces/`,
   `ShelfGuard.Infrastructure/Data/Repositories/`) — `GetHistoryAsync` currently takes only
   `(tenantId, limit, ct)`. ADR-018 needs a filtered variant: search (Title `ILike`),
   employee (`UserId`), category (`EventType`), date range (`CreatedAt`), store (`StoreId`).
   Your call on whether to extend the existing method's signature or add a new one — no
   product decision pending here.
2. **`GetHistoryAsync` must exclude `Channel = 'system'` rows** (ADR-018 §1) — those are
   undispatched outbox intents (`UserId = null`, `Status = 'pending'`), not real
   per-user notifications; they'd otherwise leak into the UI feed as phantom rows once the
   outbox pattern lands. Add a `WHERE Channel <> 'system'` (or equivalent) to
   `GetHistoryAsync` regardless of which task actually introduces the outbox writes — this
   filter is cheap to add now and safe even before any `system` rows exist.
3. **Populate `Title`/`StoreId` on every enqueue path** — `EnqueueAsync` callers
   (`NotificationService.cs`, worker `logNotifications`, and any new backend-originated
   outbox writers) need to start passing these two fields. Check every existing call site;
   old rows will have `Title = null`/`StoreId = null` — filters should treat that as
   "unmatched" for search/store filters, not throw.
4. No frontend work implied — separate downstream task once the filtered
   `GetHistoryAsync` contract is fixed.

## Deviations / things to double check

- **No FK constraint on `StoreId → locations`.** ADR-018 §3 calls it a "nullable FK" but
  the existing `NotificationQueue.TenantId`/`UserId` columns on this same table have never
  had enforced FK constraints (checked the pre-change model snapshot — no `HasOne`/
  `HasForeignKey` on either). I matched that existing (lighter) pattern rather than
  introducing the table's first hard FK, to stay consistent within the entity. If you need
  referential integrity here (e.g. to safely `.Include(n => n.Store)`), that's a follow-up
  schema change — flag back to database-engineer rather than adding it yourself.
- `Title` is `varchar(255)` — if a future service wants a longer generated summary, that's
  a length bump, not a redesign.
- `pg_trgm` extension is now enabled cluster-wide (`CREATE EXTENSION IF NOT EXISTS
  pg_trgm`) — available for any future trigram search elsewhere in the schema, not just
  this column.
