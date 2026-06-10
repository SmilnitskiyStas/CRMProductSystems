# TASK-022 — Discounts API
**Agent:** backend-developer
**Date:** 2026-06-10
**Status:** done

## Summary
TASK-022 was already fully implemented in a prior session. Verified all layers are complete and `dotnet build` passes with 0 errors.

## Implemented files
| Layer | File |
|---|---|
| Domain | `ShelfGuard.Domain/Entities/Discount.cs` |
| Domain | `ShelfGuard.Domain/Interfaces/IDiscountRepository.cs` |
| Application | `ShelfGuard.Application/Features/Discounts/IDiscountService.cs` |
| Application | `ShelfGuard.Application/Features/Discounts/DiscountService.cs` |
| Application | `ShelfGuard.Application/Features/Discounts/Dtos/DiscountDtos.cs` |
| Infrastructure | `ShelfGuard.Infrastructure/Data/Repositories/DiscountRepository.cs` |
| Api | `ShelfGuard.Api/Controllers/DiscountsController.cs` |

## Endpoints
```
GET  /api/discounts                [AtLeastStoreManager]  → DiscountDto[]  (?storeId, ?status)
GET  /api/discounts/{id}           [AtLeastStoreManager]  → DiscountDto | 404
POST /api/discounts                [AtLeastStoreManager]  → 201 DiscountDto | 400
PUT  /api/discounts/{id}/approve   [AtLeastStoreManager]  → DiscountDto | 400 | 404
PUT  /api/discounts/{id}/cancel    [AtLeastStoreManager]  → DiscountDto | 400 | 404
```

## Status flow
`pending` → approve → `active` → (auto-expire when ValidUntil passed) → `expired`
`pending` | `active` → cancel → `cancelled`

## DI registration
- `IDiscountService` → `DiscountService` in Application DI
- `IDiscountRepository` → `DiscountRepository` in Infrastructure DI
- `AppDbContext.Discounts` → `discounts` table (EF mapping in FullSchema migration)

## Build result
`dotnet build` — 0 Warnings, 0 Errors ✅
