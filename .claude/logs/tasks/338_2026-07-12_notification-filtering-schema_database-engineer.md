# TASK-338 — NotificationQueue filtering schema (database-engineer, 2026-07-12)

**Status:** done (schema-only, per ADR-018 §3 — no repository/service changes)

## What
Extends `NotificationQueue` for the notifications page filter drawer (search/employee/
category/date/store): adds `StoreId` and `Title`, plus btree indexes for the filter
dimensions and a `pg_trgm` GIN index on `Title` for keyword search without parsing
`Payload` JSONB per query.

## Files
- `ShelfGuard.Domain/Entities/NotificationQueue.cs` — added `Guid? StoreId`, `string? Title`
- `ShelfGuard.Infrastructure/Data/AppDbContext.cs` — `NotificationQueue` config: `StoreId`
  mapped to column `LocationId`, `Title` mapped `varchar(255)`, 4 new btree indexes + 1 GIN
  trgm index
- `ShelfGuard.Infrastructure/Migrations/20260712122713_ExtendNotificationQueueFiltering.cs`

## Store/Location naming
Same rename-era convention already used by `StockStatusSnapshot.StoreId`,
`DailySale.StoreId`, `StockMovement.FromStoreId/ToStoreId`, `ProductStock.StoreId`: C#
property `StoreId` (`Guid?`), DB column `"LocationId"`. **No FK constraint added** —
matches the existing `NotificationQueue.TenantId`/`UserId` columns, which are also plain
`Guid?` with no enforced FK on this table (checked `AppDbContextModelSnapshot.cs` before
the change — neither had `HasOne`/`HasForeignKey`). Kept `StoreId` consistent with that,
rather than introducing the table's first FK constraint unasked.

## Indexes added
- `idx_notification_queue_tenant_createdat` — btree `(TenantId, CreatedAt)`
- `idx_notification_queue_tenant_eventtype` — btree `(TenantId, EventType)`
- `idx_notification_queue_tenant_store` — btree `(TenantId, LocationId)`
- `idx_notification_queue_tenant_user` — btree `(TenantId, UserId)`
- `idx_notification_queue_title_trgm` — GIN `(Title gin_trgm_ops)`, via Npgsql fluent
  `.HasMethod("gin").HasOperators("gin_trgm_ops")` (same fluent shape as the existing
  `idx_items_barcodes_gin` JSONB GIN index, but with an operator class for trigram text)

Confirmed no overlap with the one pre-existing index
(`idx_notification_queue_tenant_status` on `(TenantId, Status, CreatedAt)`) — its leading
2-column prefix is `(TenantId, Status)`, not `(TenantId, CreatedAt)`, so the new
`tenant_createdat` index is not redundant.

## pg_trgm extension
Not previously enabled anywhere in the schema (grepped all migrations for
`CREATE EXTENSION` — zero hits). Migration adds
`CREATE EXTENSION IF NOT EXISTS pg_trgm;` as the first statement in `Up()`, before the
trigram index is created. **Not dropped in `Down()`** — left in place intentionally as a
shared, low-risk-to-leave extension (comment left in the migration explaining this).

## Migration
`20260712122713_ExtendNotificationQueueFiltering` — additive only:
`AddColumn(LocationId, Title)` + 5 `CreateIndex` calls + extension SQL. `Down()` reverses
columns/indexes, leaves the extension.

## Verification
- `dotnet build` (full solution) — 0 errors, 1 pre-existing unrelated warning
  (`MarketplaceServiceTests.cs:534`, nullable dereference, not touched by this task)
- `dotnet ef migrations script` from the prior migration — confirmed generated SQL:
  `CREATE EXTENSION IF NOT EXISTS pg_trgm;`, `ALTER TABLE ... ADD "LocationId" uuid`,
  `ALTER TABLE ... ADD "Title" character varying(255)`, all 4 btree `CREATE INDEX`
  statements, and `CREATE INDEX idx_notification_queue_title_trgm ON notification_queue
  USING gin ("Title" gin_trgm_ops)`
- No `dotnet test` run — schema-only task, no existing tests touched, none added
- Did not touch `INotificationRepository`/`NotificationRepository`/`NotificationService`
  per the task brief — left for backend-developer
