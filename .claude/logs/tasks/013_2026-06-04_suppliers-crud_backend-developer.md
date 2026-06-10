# TASK-013: Suppliers CRUD
**Date:** 2026-06-04
**Agent:** backend-developer
**Status:** done

## Files created
- `ShelfGuard.Domain/Interfaces/ISupplierRepository.cs`
- `ShelfGuard.Application/Features/Suppliers/Dtos/SupplierDtos.cs` (3 records)
- `ShelfGuard.Application/Features/Suppliers/ISupplierService.cs`
- `ShelfGuard.Application/Features/Suppliers/SupplierService.cs`
- `ShelfGuard.Infrastructure/Data/Repositories/SupplierRepository.cs`
- `ShelfGuard.Api/Controllers/SuppliersController.cs`
- `ShelfGuard.Tests/Suppliers/SupplierServiceTests.cs` (8 tests)

## Endpoints
```
GET    /api/suppliers              [AtLeastStoreManager]  ?include_inactive=false
GET    /api/suppliers/{id}         [AtLeastStoreManager]
POST   /api/suppliers              [AtLeastNetworkManager]
PUT    /api/suppliers/{id}         [AtLeastNetworkManager]
DELETE /api/suppliers/{id}         [AtLeastNetworkManager]  (soft delete)
```

## Business rules
- Duplicate name check per tenant (RLS handles tenant isolation)
- DeliveryDays ≥ 0
- Soft delete (IsActive = false)
- GET supports ?include_inactive for management screens

## Tests: 150/150 passed (9 new). dotnet publish: 0 errors.
