# TASK-002: Full DB Schema

**Date:** 2026-06-04
**Agent:** database-engineer
**Status:** done
**Duration:** ~1.5h

## What was done
Created and applied the full v1 database schema per v1-spec.md section 4.

### Domain entities created (20 new files in ShelfGuard.Domain/Entities/)
Store, StoreZone, Category, ProductSegment, Supplier, CatalogProduct,
ProductSupplierSetting, ProductStock, StockMovement, StockEvent,
StockReceipt, StockReceiptItem, StockTransfer, StockTransferItem,
WriteOff, WriteOffItem, Discount, NotificationSetting, NotificationQueue, ActivityLog

### AppDbContext
Updated with all 20 new DbSets and EF Core configurations.

### Migration: 20260604040956_FullSchema
- 19 new tables created
- RLS enabled on all tenant tables (19 policies)
- FEFO index: `idx_stock_expiry_active` on product_stock
- Additional index: `idx_stock_tenant_store`

## Tables created
stores, store_zones, categories, product_segments, suppliers,
catalog_products, product_supplier_settings, product_stock,
stock_movements, stock_events, stock_receipts, stock_receipt_items,
stock_transfers, stock_transfer_items, write_offs, write_off_items,
discounts, notification_settings, notification_queue, activity_logs

## Key decisions
- POC `Products` table kept intact for backward compat with existing catalog API
- New tenant-aware products table named `catalog_products` to avoid collision
- Child tables (receipt_items, transfer_items, write_off_items) use EXISTS subquery RLS through parent
- `store_zones` RLS joins through `stores` table
- All column names double-quoted in RLS SQL to match EF Core PascalCase

## Tests
- Build: ✅ clean
- Migration applied: ✅ `Done.` — no errors
- RLS policies: created for all 19 tenant tables

## Notes for next agent
- backend-developer: `/api/stock` endpoint can now be built using `product_stock` + `catalog_products`
- Seeder in `DbSeeder.cs` should be extended to add demo store, zones, catalog products, and stock batches
- The existing `DbSeeder` only seeds POC `Products` — dashboard stats are still derived from it
