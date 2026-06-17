# TASK-231 — Backend: Auto Service Module API

**Agent:** backend-developer
**Date:** 2026-06-17
**Status:** done

## What was done

Implemented the complete Auto Service Module API on top of the TASK-230 migration (`20260617190610_V4AutoServiceSchema`).

### Files created

#### Domain
- `ShelfGuard.Domain/Interfaces/IAutoServiceRepository.cs` — repository interface with methods for customers, vehicles, service catalog, work orders, FEFO write-down

#### Infrastructure
- `ShelfGuard.Infrastructure/Data/Repositories/AutoServiceRepository.cs` — EF Core implementation; tenant isolation via RLS

#### Application
- `ShelfGuard.Application/Features/AutoService/Dtos/AutoServiceDtos.cs` — all DTOs
- `ShelfGuard.Application/Features/AutoService/IAutoServiceService.cs` — service interface
- `ShelfGuard.Application/Features/AutoService/AutoServiceService.cs` — business logic

#### API
- `ShelfGuard.Api/Controllers/AutoServiceController.cs` — thin controller; all endpoints with `[Authorize]` + `[RequireModule("auto_service")]`

#### Tests
- `ShelfGuard.Tests/AutoService/AutoServiceServiceTests.cs` — 6 unit tests with fake repository

#### DI registrations
- `ShelfGuard.Application/DependencyInjection.cs` — `IAutoServiceService` → `AutoServiceService`
- `ShelfGuard.Infrastructure/DependencyInjection.cs` — `IAutoServiceRepository` → `AutoServiceRepository`

### Endpoints implemented

```
GET    /api/auto-service/customers
GET    /api/auto-service/customers/{id}
POST   /api/auto-service/customers
PUT    /api/auto-service/customers/{id}
DELETE /api/auto-service/customers/{id}     — 409 if has vehicles

GET    /api/auto-service/vehicles
GET    /api/auto-service/vehicles/{id}
POST   /api/auto-service/vehicles
PUT    /api/auto-service/vehicles/{id}
DELETE /api/auto-service/vehicles/{id}      — 409 if has open work orders

GET    /api/auto-service/service-catalog
POST   /api/auto-service/service-catalog
PUT    /api/auto-service/service-catalog/{id}
DELETE /api/auto-service/service-catalog/{id}  — soft delete (IsActive=false)

GET    /api/auto-service/work-orders
GET    /api/auto-service/work-orders/{id}
POST   /api/auto-service/work-orders
PUT    /api/auto-service/work-orders/{id}
POST   /api/auto-service/work-orders/{id}/lines
DELETE /api/auto-service/work-orders/{id}/lines/{lineId}
POST   /api/auto-service/work-orders/{id}/complete
```

### FEFO complete action

`CompleteWorkOrderAsync` pre-validates all part lines for sufficient stock before consuming any. If any part is short → returns 422 `{"error": "Insufficient stock for item '...'"}` without touching DB. On success: consumes FEFO-ordered batches, creates `stock_events` with `event_type = auto_service_consumption`, sets `work_order.status = Done`.

### Test results

- `dotnet build` — green (0 warnings, 0 errors)
- `dotnet test` — 450/450 passed (444 existing + 6 new)

### New tests

1. `DeleteCustomerAsync_WithVehicles_Returns409`
2. `DeleteCustomerAsync_WithNoVehicles_Succeeds`
3. `AddLineAsync_InvalidType_ReturnsValidationError`
4. `AddLineAsync_OnDoneOrder_Returns409`
5. `CompleteWorkOrderAsync_InsufficientStock_Returns422`
6. `CompleteWorkOrderAsync_HappyPath_StatusDoneAndStockEventsCreated`
