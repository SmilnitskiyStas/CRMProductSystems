# TASK-006: Products API (catalog_products)
**Date:** 2026-06-04
**Agent:** backend-developer
**Status:** done

## What was implemented

Full Products API backed by `catalog_products` table (v1 tenant-aware, distinct from POC `Products`).

### Files created
| File | Purpose |
|---|---|
| `ShelfGuard.Domain/Interfaces/ICatalogProductRepository.cs` | Repository contract |
| `ShelfGuard.Application/Features/Catalog/Dtos/CatalogProductDto.cs` | Request/response DTOs (5 records) |
| `ShelfGuard.Application/Features/Catalog/ICatalogProductService.cs` | Service interface |
| `ShelfGuard.Application/Features/Catalog/CatalogProductService.cs` | Business logic |
| `ShelfGuard.Infrastructure/Data/Repositories/CatalogProductRepository.cs` | EF Core repository |
| `ShelfGuard.Api/Controllers/CatalogController.cs` | HTTP endpoints at `/api/products` |
| `ShelfGuard.Tests/Catalog/CatalogProductServiceTests.cs` | 19 unit tests |

### Files modified
- `ShelfGuard.Application/DependencyInjection.cs` — registered `ICatalogProductService`
- `ShelfGuard.Infrastructure/DependencyInjection.cs` — registered `ICatalogProductRepository`

## Endpoints implemented

```
GET    /api/products                          [CanViewStock]
GET    /api/products/{id}                     [CanViewStock]
GET    /api/products/by-barcode/{code}        [CanViewStock]
POST   /api/products                          [AtLeastStoreManager]
PUT    /api/products/{id}                     [AtLeastStoreManager]
DELETE /api/products/{id}  (soft delete)      [AtLeastStoreManager]
GET    /api/products/{id}/suppliers           [CanViewStock]
POST   /api/products/{id}/suppliers           [AtLeastStoreManager]
```

Query params on GET /api/products: `category_id`, `segment_id`, `management_type`

## Business rules enforced
- ManagementType must be one of: MTS, MTO, NA, NM
- DELETE is soft delete (IsActive = false)
- Duplicate product-supplier setting rejected (409 via 400+error)
- MOQ and USQ must be > 0
- tenantId always from JWT claim `tenant_id`, never from body

## Test results
87/87 passed (19 new for Catalog, 68 existing)

## Notes
- Full solution compiles clean (`dotnet publish` to Release confirms 0 errors)
- `dotnet build` blocked by running API process (PID 56900 access denied), used `dotnet publish` to /tmp to verify
