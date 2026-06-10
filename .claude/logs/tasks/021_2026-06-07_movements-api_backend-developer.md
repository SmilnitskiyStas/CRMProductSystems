# TASK-021: Movements API (GET /api/movements)

**Agent:** backend-developer
**Date:** 2026-06-07
**Status:** done

## What was done

`GET /api/analytics/movements` existed (aggregated analytics).
TASK-021 required a separate raw audit-log endpoint `GET /api/movements` — created from scratch.

### New files
- `ShelfGuard.Domain/Interfaces/IMovementRepository.cs`
- `ShelfGuard.Application/Features/Movements/Dtos/MovementDto.cs` — MovementDto + MovementPageDto
- `ShelfGuard.Application/Features/Movements/IMovementService.cs`
- `ShelfGuard.Application/Features/Movements/MovementService.cs`
- `ShelfGuard.Infrastructure/Data/Repositories/MovementRepository.cs`
- `ShelfGuard.Api/Controllers/MovementsController.cs`

### Modified
- `ShelfGuard.Application/DependencyInjection.cs` — registered IMovementService
- `ShelfGuard.Infrastructure/DependencyInjection.cs` — registered IMovementRepository

### Endpoint
```
GET /api/movements
  ?product_id   (Guid, optional)
  ?store_id     (Guid, optional) — matches from_store_id OR to_store_id
  ?type         (string, optional) — receipt/transfer/write_off/adjustment
  ?from         (DateOnly, optional)
  ?to           (DateOnly, optional)
  ?page         (int, default 1)
  ?page_size    (int, default 50, max 200)

Response: MovementPageDto { items[], total, page, pageSize }
Auth: Authorize(CanViewStock)
tenant_id from JWT only
```

## Build
`dotnet build` — 0 warnings, 0 errors
