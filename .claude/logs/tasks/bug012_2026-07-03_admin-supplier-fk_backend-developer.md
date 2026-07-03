# BUG-012 — POST /api/admin/marketplace/suppliers → 500 (FK violation)

**Agent:** backend-developer · **Date:** 2026-07-03 · **Status:** done

## Root cause
`MarketplaceService.AdminCreateSupplierAsync` хардкодив `platformTenantId = Guid.Empty`;
FK `FK_suppliers_tenants_TenantId` існував завжди → INSERT падав, TASK-275 admin-флоу
ніколи не працював на prod. Рядків з `TenantId = Guid.Empty` у БД немає (усі згадки
«existing suppliers Guid.Empty» в ADR-016/коментарях — історичні, оновлено).

## Fix
- `IMarketplaceRepository.GetOrCreatePlatformTenantIdAsync()` (новий метод) +
  імплементація в `MarketplaceRepository`: get-or-create системного tenant
  «Platform Marketplace» (slug `platform-marketplace`, `business_type = supplier`,
  `Deactivate()`, без users). Race-safe: catch `DbUpdateException` по unique slug
  index → detach + перечитування переможця.
- `MarketplaceService.AdminCreateSupplierAsync` бере tenant id звідти (до створення
  Supplier/Profile, тому SaveChanges tenant-а ізольований).
- Оновлені стейл-коментарі Guid.Empty: `SupplierProfile.cs`, `IMarketplaceRepository.cs`,
  `ISupplierCabinetService.cs`; ADR-016 amendment у `.claude/docs/decisions.md`.

## Supplier cabinet — не зачеплено
Кабінет резолвить профіль через `GetOwnerManagedProfileAsync` (фільтр
`IsOwnerManaged = true`); admin-створені профілі мають `false`, platform-tenant не має
users/login. Покрито тестом `GetOwnerManagedProfileAsync_NeverResolvesPlatformSuppliers`.

## Чому старі тести не зловили
TASK-275 тести — NSubstitute-моки `IMarketplaceRepository`: FK не перевіряються.
Додано `Microsoft.EntityFrameworkCore.InMemory` 8.0.0 у Tests +
`MarketplaceRepositoryPlatformTenantTests` (4 тести: create / reuse / reuse across
contexts / cabinet-ізоляція) + 2 service-тести в `MarketplaceServiceTests`
(platform tenant id пропагується в Supplier і Profile, IsOwnerManaged=false;
invalid input не чіпає tenant/save).

## Verification
- `dotnet build` — 0 warnings, 0 errors
- `dotnet test` — 506/506 green (+6 нових)

## Files
- backend/ShelfGuard.Application/Features/Marketplace/MarketplaceService.cs
- backend/ShelfGuard.Domain/Interfaces/IMarketplaceRepository.cs
- backend/ShelfGuard.Infrastructure/Data/Repositories/MarketplaceRepository.cs
- backend/ShelfGuard.Domain/Entities/SupplierProfile.cs
- backend/ShelfGuard.Application/Features/Marketplace/ISupplierCabinetService.cs
- backend/ShelfGuard.Tests/Marketplace/MarketplaceRepositoryPlatformTenantTests.cs (new)
- backend/ShelfGuard.Tests/Marketplace/MarketplaceServiceTests.cs
- backend/ShelfGuard.Tests/ShelfGuard.Tests.csproj
- .claude/docs/decisions.md, .claude/tasks/current.md

**Next:** deploy to prod; re-check «+ Створити постачальника» на /marketplace.
