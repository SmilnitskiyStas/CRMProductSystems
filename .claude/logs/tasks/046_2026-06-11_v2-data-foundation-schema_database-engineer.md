---
task_id: TASK-046
date: 2026-06-11
agent: database-engineer
status: done
---

# TASK-046 — v2 schema: daily_sales, product_adu, supply_schedules

## Migration
`20260611111552_V2DataFoundation` — applied to production via API startup MigrateAsync.

## Created
| Item | Details |
|---|---|
| Entities | `DailySale`, `ProductAdu`, `SupplySchedule` in ShelfGuard.Domain/Entities |
| `daily_sales` | QuantitySold/EndOfDay decimal(10,2), Date DATE, IsPromoDay, IsAnomaly, Source(20)='manual'. UNIQUE(StoreId,ProductId,Date), idx(TenantId,Date) |
| `product_adu` | Adu30/60/90d + AduEffective decimal(10,4), ProductGroup smallint, ValidDays30/60d. UNIQUE(StoreId,ProductId) |
| `supply_schedules` | DayOfWeek integer[], OrderLeadDays, IsActive. idx(StoreId,SupplierId) |
| RLS | All 3 tables: ENABLE RLS + tenant_isolation + provider_bypass |
| FKs | → catalog_products / stores / suppliers, ON DELETE CASCADE |

## Spec deviations (intentional)
- Spec §8 references `products(id)`; FKs point to `catalog_products` — the real v1 catalog
  (legacy `Products` is POC-only).
- Added `TenantId+Date` index for ADU window scans (not in spec).
- Added CreatedAt audit columns (project convention).

## Verification
- `dotnet ef migrations script` reviewed — tables/indexes/policies correct
- Production: `pg_tables.rowsecurity = t` ×3, 6 policies present, API healthy
- Local docker postgres was down — validated via script + prod apply

## Next
TASK-047 (Daily Sales API) and TASK-049 (Supply Schedules CRUD) are unblocked.
