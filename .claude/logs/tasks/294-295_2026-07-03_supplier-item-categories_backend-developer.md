# TASK-294 / TASK-295 — Supplier item category registry + CRUD validation

**Status:** done · **Agent:** backend-developer

## TASK-294 — registry + endpoint
- New `ShelfGuard.Domain.Constants.SupplierItemCategories` (`backend/ShelfGuard.Domain/Constants/SupplierItemCategories.cs`): 4 fixed categories (`food`, `auto_parts`, `medical`, `construction`) with field lists per ADR-017 §4 (key, LabelUa, type, required, options for `select`).
- `SupplierItemCategories.Validate(string? category, Dictionary<string,object?>? attributes)` → `List<string>` (Ukrainian error messages). `category == null` → always empty (ADR-017 §5). Unknown category key → single error, rejected. Known category → one error per missing/blank required field.
- New DTOs in `MarketplaceDtos.cs`: `SupplierItemCategoryDto`, `SupplierItemCategoryFieldDto`.
- `IMarketplaceService.GetItemCategories()` / `MarketplaceService.GetItemCategories()` — maps registry to DTOs.
- New endpoint `GET /api/marketplace/item-categories` (`[AllowAnonymous]`) on `MarketplaceController`.

## TASK-295 — DTOs + CRUD validation
- `SupplierItemDto`, `AdminAddSupplierItemDto`, `AdminUpdateSupplierItemDto` — added `string? Category = null`, `Dictionary<string,object?>? Attributes = null` (optional trailing params, no breaking change to existing call sites).
- `SupplierCabinetService` has no own item DTOs — it reuses `MarketplaceService.AdminAddSupplierItemAsync` / `AdminUpdateSupplierItemAsync` directly (already the case pre-task), so validation added there covers both admin and cabinet paths. `ToItemDto` in both `MarketplaceService` and `SupplierCabinetService` now round-trip `Category`/`Attributes`.
- `MarketplaceService.AdminAddSupplierItemAsync` / `AdminUpdateSupplierItemAsync`: call `SupplierItemCategories.Validate` before persisting (update validates the **effective** category/attributes — patched value if provided, else existing item value). Non-empty errors → `(null, joinedErrorString)`, same tuple-based convention as existing `CustomName` validation; controllers already map non-null error → 400 (no controller changes needed).
- `SupplierRecommendationDto.MatchedItem` needed no changes — reuses `SupplierItemDto`.

## Tests
- `backend/ShelfGuard.Tests/Domain/SupplierItemCategoriesTests.cs` — new file, 13 tests: null category always valid, unknown category rejected, each of the 4 categories (missing required → errors, all required present → empty), blank-string treated as missing, registry shape (4 categories).
- `backend/ShelfGuard.Tests/Marketplace/MarketplaceServiceTests.cs` — added: add/update item with category="medical" missing `expiry_date` → error; all required present → success; no category → success regardless of attributes; `GetItemCategories()` returns 4 categories with correct field counts/keys/select options.

## Verify
- `dotnet build`: green, 0 warnings/errors.
- `dotnet test`: 535/535 green (515 baseline + 20 new, no regressions).

## Files touched
- `backend/ShelfGuard.Domain/Constants/SupplierItemCategories.cs` (new)
- `backend/ShelfGuard.Application/Features/Marketplace/Dtos/MarketplaceDtos.cs`
- `backend/ShelfGuard.Application/Features/Marketplace/IMarketplaceService.cs`
- `backend/ShelfGuard.Application/Features/Marketplace/MarketplaceService.cs`
- `backend/ShelfGuard.Application/Features/Marketplace/SupplierCabinetService.cs`
- `backend/ShelfGuard.Api/Controllers/MarketplaceController.cs`
- `backend/ShelfGuard.Tests/Domain/SupplierItemCategoriesTests.cs` (new)
- `backend/ShelfGuard.Tests/Marketplace/MarketplaceServiceTests.cs`
