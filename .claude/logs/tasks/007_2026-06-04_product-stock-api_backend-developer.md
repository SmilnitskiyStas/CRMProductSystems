# TASK-007: ProductStock API + FEFO
**Date:** 2026-06-04
**Agent:** backend-developer
**Status:** done

## What was implemented

Full ProductStock (batch) API backed by `product_stock` table with FEFO consumption logic and action suggestions.

### Files created
| File | Purpose |
|---|---|
| `ShelfGuard.Domain/Interfaces/IStockRepository.cs` | Repository contract |
| `ShelfGuard.Application/Features/Stock/Dtos/StockDtos.cs` | DTOs (8 records) |
| `ShelfGuard.Application/Features/Stock/StockStatus.cs` | Static status computation helper |
| `ShelfGuard.Application/Features/Stock/IStockService.cs` | Service interface |
| `ShelfGuard.Application/Features/Stock/StockService.cs` | Business logic incl. FEFO + suggestions |
| `ShelfGuard.Infrastructure/Data/Repositories/StockRepository.cs` | EF Core repository |
| `ShelfGuard.Api/Controllers/StockController.cs` | HTTP endpoints at `/api/stock` |
| `ShelfGuard.Tests/Stock/StockStatusTests.cs` | 10 unit tests for status computation |
| `ShelfGuard.Tests/Stock/StockServiceTests.cs` | 15 unit tests for service |

### Files modified
- `Application/DependencyInjection.cs` — registered `IStockService`
- `Infrastructure/DependencyInjection.cs` — registered `IStockRepository`

## Endpoints implemented

```
GET  /api/stock                    [CanViewStock]   ?store_id, ?status, ?zone_id, ?product_id
GET  /api/stock/{id}               [CanViewStock]
GET  /api/stock/expiring           [CanViewStock]   ?store_id, ?days=7
GET  /api/stock/expired            [CanViewStock]   ?store_id
GET  /api/stock/needs-check        [CanViewStock]   ?store_id
GET  /api/stock/suggestions        [CanViewStock]   ?store_id
POST /api/stock                    [CanReceiveStock]
PUT  /api/stock/{id}               [AtLeastStoreManager]
POST /api/stock/{id}/verify        [CanReceiveStock]
POST /api/stock/fefo-consume       [CanReceiveStock]  { productId, storeId, quantity, notes }
```

## Business rules enforced

### Status computation (StockStatus.Compute)
```
quantity = 0               → sold_out
expiryDate ≤ today         → expired
1..6 days left             → critical
7..14 days left            → warning
lastCheckedAt > 90 days    → needs_verification  (only when > 14 days left)
else                       → safe
```

Status computed dynamically on every read — independent of cron cadence.

### FEFO Consume
- Batches ordered by ExpiryDate ASC (nearest first)
- Greedy consumption across batches
- Each consumption logs a `write_off` StockMovement
- Partial success when stock insufficient: returns QuantityShortfall > 0

### Suggestions (GET /api/stock/suggestions)
Priority order per batch:
1. Transfer to store with deficit of same product
2. Pass to production/distribution store
3. Discount if quantity > min_stock * 1.5 (% depends on days_left)
4. Return to supplier if return_policy = true
5. Write-off (fallback)

### POST /stock creates receipt StockMovement automatically

## Test results
112/112 passed (25 new for Stock: 10 status + 15 service)
dotnet publish Release: 0 errors, 0 warnings
