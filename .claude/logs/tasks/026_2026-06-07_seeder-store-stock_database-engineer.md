# TASK-026: Seeder — v1 catalog/stock/stores test data

**Agent:** database-engineer
**Date:** 2026-06-07
**Status:** done

## What was done

Updated `backend/ShelfGuard.Infrastructure/Data/DbSeeder.cs` with full v1 demo data:

### Added
1. **Categories** — 6 categories: Молочні, Овочі та фрукти, М'ясо та ковбаса, Бакалія, Напої, Хлібобулочні
2. **Supplier** — "АТБ Постачання" with contact details and NET30 terms
3. **Store** — "Магазин №1 — Центральний" (Київ, вул. Хрещатик)
4. **StoreZones** — 4 zones: Стелаж A (shelf, 2-6°C), Холодильник (fridge, 0-4°C), Морозильна камера (freezer, -20°C), Стелаж B (dry)
5. **CatalogProducts** — 22 tenant-aware products with barcode, category, supplier, shelf_life, temp ranges, purchase/retail prices
6. **ProductStock batches** — 36 batches distributed across all 4 statuses:
   - safe: 15 batches (normal stock, expiry 4-720 days out)
   - warning: 8 batches (expiry 1-4 days)
   - critical: 6 batches (expiry today or +1 day)
   - expired: 6 batches (expiry -1 to -3 days)
7. **Legacy Products** — kept 7 POC entries for backward compat with existing Products API

### Architecture notes
- Two-phase SaveChangesAsync: first saves tenant/users/categories/supplier/store/zones/products, then stock batches (FK dependencies)
- FEFO ordering is natural: batches have distinct expiry_date values per product
- Status field is set directly (seed data, not computed by cron)

## Files changed
- `backend/ShelfGuard.Infrastructure/Data/DbSeeder.cs`

## Build
`dotnet build ShelfGuard.Infrastructure` — 0 warnings, 0 errors
