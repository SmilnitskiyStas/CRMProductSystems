# TASK-014: Stock Receipts (прийомка)
**Date:** 2026-06-04
**Agent:** backend-developer
**Status:** done

## Files created
- `ShelfGuard.Domain/Interfaces/IReceiptRepository.cs`
- `ShelfGuard.Application/Features/Receipts/Dtos/ReceiptDtos.cs` (7 records)
- `ShelfGuard.Application/Features/Receipts/IReceiptService.cs`
- `ShelfGuard.Application/Features/Receipts/ReceiptService.cs`
- `ShelfGuard.Infrastructure/Data/Repositories/ReceiptRepository.cs`
- `ShelfGuard.Api/Controllers/ReceiptsController.cs`
- `ShelfGuard.Tests/Receipts/ReceiptServiceTests.cs` (14 tests)

## Endpoints
```
GET  /api/receipts               [CanReceiveStock]  ?store_id, ?status
GET  /api/receipts/{id}          [CanReceiveStock]
POST /api/receipts               [CanReceiveStock]
PUT  /api/receipts/{id}/items    [CanReceiveStock]
PUT  /api/receipts/{id}/receive  [CanReceiveStock]
PUT  /api/receipts/{id}/cancel   [AtLeastStoreManager]
```

## Business rules
- POST creates draft receipt with items (QuantityOrdered)
- PUT /items updates received qty, expiry_date, batch_number per item (pre-populated workflow: if supplier already filled expiry/batch, storekeeper just confirms)
- PUT /receive: validates all items have ExpiryDate → creates ProductStock batches + StockMovements (type=receipt) → sets status=received
- PUT /cancel: allowed for draft/ordered/in_transit; forbidden on received
- IsProcessed = QuantityReceived.HasValue && ExpiryDate.HasValue

## Tests: 164/164 passed (14 new). dotnet publish: 0 errors.
