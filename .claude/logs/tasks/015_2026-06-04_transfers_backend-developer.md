# TASK-015: Stock Transfers
**Date:** 2026-06-04
**Agent:** backend-developer
**Status:** done

## Files created
- `ShelfGuard.Domain/Interfaces/ITransferRepository.cs`
- `ShelfGuard.Application/Features/Transfers/Dtos/TransferDtos.cs` (4 records)
- `ShelfGuard.Application/Features/Transfers/ITransferService.cs`
- `ShelfGuard.Application/Features/Transfers/TransferService.cs`
- `ShelfGuard.Infrastructure/Data/Repositories/TransferRepository.cs`
- `ShelfGuard.Api/Controllers/TransfersController.cs`
- `ShelfGuard.Tests/Transfers/TransferServiceTests.cs` (14 tests)

## Endpoints
```
GET  /api/transfers              [CanViewStock]    ?store_id, ?status
GET  /api/transfers/{id}         [CanViewStock]
POST /api/transfers              [CanReceiveStock]
PUT  /api/transfers/{id}/confirm [CanReceiveStock]
PUT  /api/transfers/{id}/cancel  [AtLeastStoreManager]
```

## Business rules
- POST creates with status=in_transit and immediately deducts source ProductStock
- Validates: items not empty, from≠to, valid transfer type, stock belongs to from_store, sufficient quantity
- PUT /confirm: creates new ProductStock at destination (expiryDate + batchNumber copied as-is — spec invariant), logs inbound movement, status=received
- PUT /cancel: restores deducted source stock, status=cancelled
- TransferType: store_to_store | cs_to_store | store_to_production | null

## Key invariant (v1-spec 2.5)
expiry_date and batch_number NEVER change on transfer — copied verbatim from source ProductStock.

## Tests: 178/178 passed (14 new). dotnet publish: 0 errors.
