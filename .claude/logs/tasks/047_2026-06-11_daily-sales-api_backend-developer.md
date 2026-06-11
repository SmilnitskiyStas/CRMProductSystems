---
task_id: TASK-047
date: 2026-06-11
agent: backend-developer
status: done
---

# TASK-047 — Daily Sales API

## Files
| Layer | File |
|---|---|
| Domain | `Interfaces/IDailySalesRepository.cs` |
| Application | `Features/Sales/{IDailySalesService, DailySalesService, Dtos/DailySalesDtos}.cs` |
| Application | `AssemblyInfo.cs` (new — InternalsVisibleTo ShelfGuard.Tests) |
| Infrastructure | `Data/Repositories/DailySalesRepository.cs` |
| Api | `Controllers/DailySalesController.cs` |
| Tests | `Tests/Sales/DailySalesServiceTests.cs` — 5 CSV-parser tests |

## Endpoints (all `[Authorize(AtLeastStoreManager)]`)
```
GET  /api/daily-sales?store_id&product_id&from&to  → DailySaleDto[]
POST /api/daily-sales                              → 200 DailySaleDto | 400 | 404
     body: {storeId, productId, date, quantitySold, quantityEndOfDay?, isPromoDay}
     Upserts by UNIQUE(store, product, date); source='manual'
POST /api/daily-sales/import?store_id=…            → 200 CsvImportResult | 400 | 404
     multipart 'file'; header: barcode,date,quantity_sold[,quantity_end_of_day][,is_promo_day]
     Resolves products by barcode (one batched query); upserts; source='import';
     row-level errors collected, valid rows still imported; 10k row / 5MB limits
PUT  /api/daily-sales/{id}/mark-anomaly            → 200 | 404   body: {isAnomaly}
```

## Design decisions
- **Upsert, not 409** — the sales-entry grid edits the same (store,product,date) cell
  repeatedly; conflict errors would fight the UX. UNIQUE index still guards integrity.
- **Validation:** quantity ≥ 0, date not in future, store/product must exist+active.
- **Roles:** AtLeastStoreManager (sales data is commercial — storekeeper/merchandiser get 403).
- tenant_id from JWT only; RLS scopes all reads.

## Production verification
- POST upsert → 200 (Вода Моршинська, 12.5) ✓
- GET filter by store → rows ✓
- CSV import → `{created:1, updated:0, skipped:1, errors:["Line 3: barcode 'UNKNOWN-BC' not found"]}` ✓
- Negative quantity → 400 ✓ · storekeeper → 403 ✓
- Tests 5/5; build 0/0
