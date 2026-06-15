# TASK-200 — DB: stores → locations + location_type
**Date:** 2026-06-15  
**Agent:** database-engineer  
**Status:** done

## What was done

EF Core migration `V4LocationsRename` applied successfully.

### Schema changes
- `stores` → `locations` (table rename)
- `store_zones` → `location_zones` (table rename)
- Added `locations.LocationType varchar(50) DEFAULT 'retail_store'`
- Added `tenants.BusinessType varchar(50) DEFAULT 'retail'`
- Renamed `StoreId` → `LocationId` in all dependent tables:
  - `write_offs`, `weather_data`, `temperature_readings`, `supply_schedules`
  - `product_stock`, `product_buffer`, `product_adu`, `pos_transactions`, `pos_shifts`
  - `iot_devices`, `discounts`, `demand_events`, `daily_sales`, `ai_order_suggestions`
  - `location_zones` (was `store_zones`)
- Renamed `FromStoreId`/`ToStoreId` → `FromLocationId`/`ToLocationId` in `stock_transfers`
- Renamed `DestinationStoreId` → `DestinationLocationId` in `stock_receipts`
- Renamed `FromStoreId`/`ToStoreId` → `FromLocationId`/`ToLocationId` in `stock_movements`
- All FK constraints recreated with new names pointing to `locations`/`location_zones`
- RLS policy `tenant_isolation` on `location_zones` updated to reference `locations` table and `"LocationId"` column

### Domain entities updated
- `Store.cs`: added `LocationType` property
- `Tenant.cs`: added `BusinessType` property + `UpdateBusinessType()` method

### AppDbContext updated
- `Store` → `ToTable("locations")`, `HasColumnName("LocationType")`
- `StoreZone` → `ToTable("location_zones")`, `StoreId.HasColumnName("LocationId")`
- `StockMovement` → `FromStoreId.HasColumnName("FromLocationId")`, `ToStoreId.HasColumnName("ToLocationId")`
- All other entities: `StoreId.HasColumnName("LocationId")`

## Fixes required during migration
The auto-generated migration had FK/index mismatches with actual DB state:
- Removed 3 non-existent DropForeignKey: `stock_receipts_DestinationStoreId`, `stock_transfers_FromStoreId`, `stock_transfers_ToStoreId`
- Added 3 missing DropForeignKey: `discounts_StoreId`, `stock_movements_FromStoreId`, `stock_movements_ToStoreId`
- Removed 4 non-existent RenameIndex calls (those indexes didn't exist in DB)
- Added stock_movements column renames and new FK constraints

## Acceptance criteria
- [x] Migration applies cleanly (`dotnet ef database update`)
- [x] `dotnet build` green (0 warnings, 0 errors)
- [x] `locations` and `location_zones` tables exist in DB
- [x] `locations.LocationType` and `tenants.BusinessType` columns present
- [x] RLS policies on `location_zones` verified (tenant_isolation + provider_bypass)

## Next
TASK-201 — Backend: Store → Location entity + API rename
