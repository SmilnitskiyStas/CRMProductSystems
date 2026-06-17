# TASK-230 — DB: Auto Service schema

**Agent:** database-engineer
**Date:** 2026-06-17
**Status:** done

## Summary

Created full DB schema for the Auto Service module (Phase 4). All 5 tables created with tenant_id, RLS, FKs, indexes, and CHECK constraints.

## Files Created

### Domain Entities (ShelfGuard.Domain/Entities/)
- `AsCustomer.cs` — auto service customer (name, phone, email, notes)
- `AsVehicle.cs` — customer vehicle (brand, model, year, vin, license_plate, mileage)
- `AsServiceCatalog.cs` — service catalog entry (name, description, item_id FK, default_price, duration_hours, is_active)
- `WorkOrderStatus.cs` — enum: New | InProgress | WaitingParts | Done | Invoiced
- `AsWorkOrder.cs` — work order (vehicle_id, mechanic_user_id, status, notes, completed_at)
- `AsWorkOrderLine.cs` — work order line (type: service|part, service_catalog_id, item_id, qty, price, discount)

### Infrastructure
- `AppDbContext.cs` — added 5 DbSets + Fluent API config for all entities
- `Migrations/20260617190610_V4AutoServiceSchema.cs` — EF Core migration

## Schema Details

### Tables Created
| Table | RLS | FKs | Indexes |
|---|---|---|---|
| `as_customers` | ✅ tenant_isolation + provider_bypass | tenants RESTRICT | (TenantId) |
| `as_vehicles` | ✅ tenant_isolation + provider_bypass | tenants RESTRICT, as_customers CASCADE | (TenantId), (CustomerId) |
| `as_service_catalog` | ✅ tenant_isolation + provider_bypass | tenants RESTRICT, items SET NULL | (TenantId) |
| `as_work_orders` | ✅ tenant_isolation + provider_bypass | tenants RESTRICT, as_vehicles RESTRICT, users SET NULL | (TenantId), (VehicleId), (Status) |
| `as_work_order_lines` | ✅ tenant_isolation + provider_bypass | as_work_orders CASCADE, as_service_catalog SET NULL, items SET NULL | (WorkOrderId) |

### CHECK Constraints
- `CK_as_work_orders_status`: Status IN ('New','InProgress','WaitingParts','Done','Invoiced')
- `CK_as_work_order_lines_type`: Type IN ('service','part')
- `CK_as_work_order_lines_ref`: (Type='service' AND ServiceCatalogId IS NOT NULL) OR (Type='part' AND ItemId IS NOT NULL)

### WorkOrderStatus Enum Mapping
`WorkOrderStatus` enum mapped to TEXT column via `.HasConversion<string>()`.
Default value stored as 'New'.

## Test Results
- `dotnet ef migrations add` ✅ succeeded
- `dotnet build` ✅ 0 errors, 0 warnings
- `dotnet test` ✅ 444/444 passed

## Handoff

Next: TASK-231 (backend-developer) — implement CRUD API for customers, vehicles, service catalog, work orders.
Key note: work order complete action must use FEFO for spare parts (item_type=spare_part).
