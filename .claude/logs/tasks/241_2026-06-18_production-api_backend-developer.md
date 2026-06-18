# TASK-241 — Production Module API

**Agent:** backend-developer
**Date:** 2026-06-18
**Status:** done

## Summary

Implemented the Production Module API on top of the DB schema from TASK-240.
All application-layer code was already scaffolded (service, interface, DTOs, repository)
but the controller, DI wiring, and tests were missing.

## Files Created

- `backend/ShelfGuard.Api/Controllers/ProductionController.cs` — thin controller, all endpoints
- `backend/ShelfGuard.Tests/Production/ProductionServiceTests.cs` — 9 unit tests, all passing

## Files Modified

- `backend/ShelfGuard.Application/Features/Production/Dtos/ProductionDtos.cs`
  — added `ProductionCompleteResultDto`
- `backend/ShelfGuard.Application/Features/Production/IProductionService.cs`
  — updated `CompleteOrderAsync` signature to return 5-tuple including `OutputStockBatchId`
- `backend/ShelfGuard.Application/Features/Production/ProductionService.cs`
  — updated `CompleteOrderAsync`: allows Planned + InProgress (not just InProgress);
    returns output stock batch ID in result tuple
- `backend/ShelfGuard.Application/DependencyInjection.cs`
  — registered `IProductionService → ProductionService`
- `backend/ShelfGuard.Infrastructure/DependencyInjection.cs`
  — registered `IProductionRepository → ProductionRepository`

## Endpoints

```
GET    /api/production/recipes              — list (active only by default, ?includeInactive=true)
GET    /api/production/recipes/{id}         — detail with ingredients list
POST   /api/production/recipes              — create recipe + ingredients
PUT    /api/production/recipes/{id}         — update recipe fields
DELETE /api/production/recipes/{id}         — soft delete (409 if has active orders)

GET    /api/production/orders               — list, filterable by status + recipe_id + location_id
GET    /api/production/orders/{id}          — detail with consumptions
POST   /api/production/orders               — create order
PUT    /api/production/orders/{id}          — update status/notes
POST   /api/production/orders/{id}/complete — FEFO write-down + output stock
POST   /api/production/orders/{id}/cancel   — cancel (409 if done)
```

All endpoints: `[Authorize]` + `[RequireModule("production")]`

## FEFO complete action

- Pre-validates ALL ingredients before consuming any (atomic guarantee)
- Consumes from `product_stock` batches ordered by `expiry_date ASC` (FEFO)
- Creates `production_order_consumptions` records per batch
- Creates `stock_events` entries: `production_consumption` + `production_output`
- Adds finished product as new `product_stock` row
- Allowed from `Planned` or `InProgress` status (409 for Done/Cancelled)
- Returns 422 with `{error, itemId}` on insufficient stock

## Test Results

- 9 new tests in `ProductionServiceTests`
- Total suite: 459/459 passing (0 failures)
- `dotnet build`: 0 errors, 0 warnings

## Acceptance Criteria

- [x] dotnet build green, 0 errors
- [x] dotnet test 459/459 green (450 existing + 9 new)
- [x] All endpoints have `[Authorize]` + `[RequireModule("production")]`
- [x] complete action: atomic FEFO write-down + output stock + consumptions log
- [x] Task log created
- [x] Backlog updated: TASK-241 → done, TASK-242 → in_progress
