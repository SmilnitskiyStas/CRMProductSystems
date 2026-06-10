# TASK-012: Stores + Zones CRUD
**Date:** 2026-06-04
**Agent:** backend-developer
**Status:** done

## What was implemented

Full Stores and Zones CRUD API backed by `stores` and `store_zones` tables.

### Files created
| File | Purpose |
|---|---|
| `ShelfGuard.Domain/Interfaces/IStoreRepository.cs` | Repository contract |
| `ShelfGuard.Application/Features/Stores/Dtos/StoreDtos.cs` | DTOs (8 records) |
| `ShelfGuard.Application/Features/Stores/IStoreService.cs` | Service interface |
| `ShelfGuard.Application/Features/Stores/StoreService.cs` | Business logic |
| `ShelfGuard.Infrastructure/Data/Repositories/StoreRepository.cs` | EF Core repository |
| `ShelfGuard.Api/Controllers/StoresController.cs` | HTTP endpoints at `/api/stores` |
| `ShelfGuard.Tests/Stores/StoreServiceTests.cs` | 29 unit tests |

### Files modified
- `Application/DependencyInjection.cs` — registered `IStoreService`
- `Infrastructure/DependencyInjection.cs` — registered `IStoreRepository`

## Endpoints

```
GET    /api/stores                           [CanViewStock]
GET    /api/stores/{id}                      [CanViewStock]
POST   /api/stores                           [AtLeastEnterpriseAdmin]
PUT    /api/stores/{id}                      [AtLeastEnterpriseAdmin]
PUT    /api/stores/{id}/floor-plan           [AtLeastStoreManager]
GET    /api/stores/{id}/zones                [CanViewStock]
POST   /api/stores/{id}/zones                [AtLeastStoreManager]
PUT    /api/stores/{id}/zones/{zoneId}       [AtLeastStoreManager]
DELETE /api/stores/{id}/zones/{zoneId}       [AtLeastStoreManager]  (soft delete)
```

## Validation
- Store types: shop, central_warehouse, production, distribution
- Zone types: shelf, fridge, freezer, display, production, warehouse
- ShelvesCount ≥ 1
- Zone ownership validated (storeId must match zone.StoreId)
- DELETE zone = soft delete (IsActive = false)

## Test results
141/141 passed (29 new for Stores)
dotnet publish Release: 0 errors, 0 warnings
