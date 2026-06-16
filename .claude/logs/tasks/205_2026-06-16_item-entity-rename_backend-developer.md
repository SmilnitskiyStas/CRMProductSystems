# TASK-205 — Backend: CatalogProduct → Item entity + API rename

**Agent:** backend-developer · **Date:** 2026-06-16 · **Status:** done

## What was done
- Renamed `CatalogProduct` entity → `Item` (`ShelfGuard.Domain/Entities/Item.cs`); `ItemType` property already added in TASK-204
- Renamed across the stack (mechanical sed-based rename, ASCII-only patterns — safe for files containing Cyrillic content like `"шт"` default unit):
  - `ICatalogProductRepository` → `IItemRepository`, `CatalogProductRepository` → `ItemRepository`
  - `ICatalogProductService` → `IItemService`, `CatalogProductService` → `ItemService`
  - `CatalogProductDto` → `ItemDto`
  - `DbSet<CatalogProduct> CatalogProducts` → `DbSet<Item> Items`
  - All navigation properties on dependent entities (`ProductStock.Product`, `PosTransactionItem.Product`, `PromoCannibalization.AffectedProduct`, etc.) now typed `Item?`
  - Test class `CatalogProductServiceTests` → `ItemServiceTests`
- Controller: `CatalogController` → `ItemsController`, route `/api/products` → `/api/items`
- Added `ProductsLegacyController` (`/api/products/*` → 301 redirect to `/api/items/*`), mirroring the `StoresLegacyController` pattern from TASK-201
- Files physically renamed via `git mv` to match new class names
- Generated migration `V4ItemEntityRename` — empty `Up()`/`Down()` (no schema change, just refreshes the EF model snapshot's entity-name keys from `CatalogProduct` to `Item`). Applied locally.

## Verification
- `dotnet build` → 0 errors
- `dotnet test` → 402/402 passed
- No remaining `CatalogProduct` references outside historical migration files (those use string-based entity names internally and don't need updating — confirmed they don't reference the CLR type directly)
- Smoke-tested locally: `GET /api/items` → 401 (route resolves, auth-protected as expected); `GET /api/products` → 301 redirect to `/api/items`

## Note on the empty migration
Renaming a C# entity class changes its key in the EF model (entity name = CLR full type name by default), even though the physical table (`items`) and columns were unchanged by TASK-204. Without `V4ItemEntityRename`, the next `migrations add` would see a phantom diff (old model still keyed `ShelfGuard.Domain.Entities.CatalogProduct`). This migration is a no-op against the database — it only updates `AppDbContextModelSnapshot.cs`.

## Next
TASK-206 — Frontend: catalog/products → items (depends on this).
