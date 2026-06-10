# TASK-025 — Fix DB: RLS + FK Constraints
**Date:** 2026-06-04
**Agent:** database-engineer
**Status:** done

## Problem Summary (from architect audit)

1. `notification_settings` had no RLS policy
2. `stock_movements` had no FK constraints on `ProductId`, `FromStoreId`, `ToStoreId`
3. `write_offs` had no FK constraint on `StoreId`
4. `discounts` had no FK constraints on `ProductId`, `StoreId`, `ProductStockId`

## Solution

Pure SQL migration `20260604120000_FixRlsAndForeignKeys` applied via `dotnet ef database update`.

### RLS Added
- `notification_settings` — policy via EXISTS subquery through `users.TenantId`
  (table has no direct TenantId; isolation is derived from owning user)

### FK Constraints Added
| Table | Column | References | On Delete |
|---|---|---|---|
| stock_movements | ProductId | catalog_products.Id | RESTRICT |
| stock_movements | FromStoreId | stores.Id | RESTRICT |
| stock_movements | ToStoreId | stores.Id | RESTRICT |
| write_offs | StoreId | stores.Id | RESTRICT |
| discounts | ProductId | catalog_products.Id | RESTRICT |
| discounts | StoreId | stores.Id | RESTRICT |
| discounts | ProductStockId | product_stock.Id | SET NULL |

### Indexes Added (bonus — support TASK-021 Movements API)
- `idx_movements_tenant_type`
- `idx_movements_tenant_store`
- `idx_movements_product`
- `idx_movements_created_at`

## Architecture Note
FKs were added as pure SQL (not via EF HasForeignKey) because `StockMovement`, `WriteOff`, `Discount` entities have no navigation properties for these fields. EF model snapshot is intentionally NOT updated. See `database-schema.md` FK table for the documented divergence.

## Files Changed
- `Migrations/20260604120000_FixRlsAndForeignKeys.cs` — new migration
- `Migrations/20260604120000_FixRlsAndForeignKeys.Designer.cs` — snapshot (unchanged model)
- `.claude/docs/database-schema.md` — updated RLS table, added FK table, added new indexes
