# TASK-PAGINATION — Add Pagination to LIST Endpoints

**Date:** 2026-06-18
**Agent:** backend-developer
**Status:** done

## Summary

Added server-side pagination to 6 previously unbounded GET LIST endpoints.

## Files Created

- `backend/ShelfGuard.Application/Common/Pagination.cs` — `PagedResult<T>` and `PagedQuery` shared DTOs

## Files Modified

### Domain Interfaces
- `ShelfGuard.Domain/Interfaces/IStockRepository.cs` — added `GetPagedAsync`
- `ShelfGuard.Domain/Interfaces/IReceiptRepository.cs` — added `GetPagedAsync`
- `ShelfGuard.Domain/Interfaces/IWriteOffRepository.cs` — added `GetPagedAsync`
- `ShelfGuard.Domain/Interfaces/ITransferRepository.cs` — added `GetPagedAsync`
- `ShelfGuard.Domain/Interfaces/IItemRepository.cs` — added `GetPagedAsync`
- `ShelfGuard.Domain/Interfaces/ISupplierRepository.cs` — added `GetPagedAsync`

### Infrastructure Repositories (all 6)
Each repository got `GetPagedAsync` that does COUNT + Skip/Take in a single round-trip pair.

### Application Service Interfaces (all 6)
Each interface got `GetPagedAsync` returning `PagedResult<T>`.

### Application Services (all 6)
Each service got `GetPagedAsync` implementation calling the repo method.

### Controllers (all 6)
`GET /api/stock`, `/api/receipts`, `/api/write-offs`, `/api/transfers`, `/api/items`, `/api/suppliers` now accept `?page=1&pageSize=50` and return `PagedResult<T>`.

## Rules Applied
- pageSize clamped to [1, 200] via `PagedQuery.ClampedPageSize`
- page clamped to ≥1 via `PagedQuery.ClampedPage`
- Default: page=1, pageSize=50
- Original `GetAllAsync` preserved in all repositories/services (used internally or by other callers)
- `DailySalesController` and `SupplySchedulesController` skipped — both have date/filter parameters that already bound results naturally
- Build: `dotnet build` → 0 errors, 0 warnings
