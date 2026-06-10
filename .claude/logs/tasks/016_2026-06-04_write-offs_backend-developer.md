# TASK-016: Write-offs
**Date:** 2026-06-04
**Agent:** backend-developer
**Status:** done

## Files created
- `ShelfGuard.Domain/Interfaces/IWriteOffRepository.cs`
- `ShelfGuard.Application/Features/WriteOffs/Dtos/WriteOffDtos.cs` (4 records)
- `ShelfGuard.Application/Features/WriteOffs/IWriteOffService.cs`
- `ShelfGuard.Application/Features/WriteOffs/WriteOffService.cs`
- `ShelfGuard.Infrastructure/Data/Repositories/WriteOffRepository.cs`
- `ShelfGuard.Api/Controllers/WriteOffsController.cs`
- `ShelfGuard.Tests/WriteOffs/WriteOffServiceTests.cs` (17 tests)

## Endpoints
```
GET  /api/write-offs              [CanViewStock]       ?store_id, ?status
GET  /api/write-offs/{id}         [CanViewStock]
POST /api/write-offs              [CanReceiveStock]
PUT  /api/write-offs/{id}/approve [AtLeastStoreManager]
PUT  /api/write-offs/{id}/reject  [AtLeastStoreManager]
GET  /api/write-offs/{id}/pdf     → 501 Not Implemented (Puppeteer planned)
```

## Business rules
- POST creates with status=pending_approval (direct submit for review)
- Valid reasons: expired, damaged, theft, production_loss, other (or null)
- TotalLossAmount = sum(quantity * unitPrice) for items with price
- PUT /approve: for each item with ProductStockId → deducts stock, logs write_off movement; status=approved
- PUT /reject: sets status=rejected; cannot reject approved write-offs
- Items without ProductStockId: tracked for loss reporting, no stock deduction

## Tests: 195/195 passed (17 new). dotnet publish: 0 errors.
