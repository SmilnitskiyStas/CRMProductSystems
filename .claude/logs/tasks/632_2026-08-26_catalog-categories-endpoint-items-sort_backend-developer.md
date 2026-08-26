# TASK-632 — Catalog: Categories endpoint + Items sortBy/sortDescending (backend)

**Status:** done · **Agent:** backend-developer
**Parallel:** frontend-developer (disjoint files) on the same contract, main worktree.

## Part 1 — `GET /api/categories`

New read-only endpoint, net-new layer (Category had no controller/service/repository before).

- `ShelfGuard.Domain/Interfaces/ICategoryRepository.cs` — `GetAllActiveAsync`
- `ShelfGuard.Infrastructure/Data/Repositories/CategoryRepository.cs` — `.Where(IsActive).OrderBy(Name)`, no explicit `TenantId` filter (mirrors `ItemRepository` exactly — RLS `tenant_isolation`/`provider_bypass` policies on `categories`, already present since the initial `FullSchema` migration, handle isolation)
- `ShelfGuard.Application/Features/Catalog/{ICategoryService,CategoryService}.cs`, `Dtos/CategoryDto.cs` — `CategoryDto(Guid Id, string Name)`
- `ShelfGuard.Api/Controllers/CategoriesController.cs` — `[Authorize(Policy = AppPolicies.CanViewStock)]`, `ProducesResponseType(List<CategoryDto>, 200)`
- DI: registered in `ShelfGuard.Infrastructure/DependencyInjection.cs` and `ShelfGuard.Application/DependencyInjection.cs`

Response shape: `[{ "id": "<guid>", "name": "<string>" }]` — flat, no pagination, active-only, ordered by name.

## Part 2 — `sortBy`/`sortDescending` on `/api/items`

**Contract (for cross-check against frontend):**
- `sortBy`: `"name"` (default, asc) | `"barcode"` | `"category"` | `"purchaseprice"` | `"retailprice"` | `"minstock"` | `"maxstock"`. Unrecognized/omitted → normalizes to `"name"`, never errors.
- `sortDescending`: omitted → `false` when the effective key is `"name"` (preserves old behavior), `true` for any other explicit key (same convention as this week's Receipts/Transfers/WriteOffs/Stock work). Explicit `true`/`false` always wins.
- Existing `category_id`/`segment_id`/`management_type`/`search`/`ids`/`page`/`pageSize` behavior unchanged.

Threaded through `ItemsController.GetAll` → `IItemService.GetPagedAsync` → `IItemRepository.GetPagedAsync` → `ItemRepository.ApplySort` (new private helper), with a new `ItemSortKeys` allowlist in `ShelfGuard.Application/Features/Catalog/ItemSortKeys.cs` (same shape as `ReceiptSortKeys`/`StockSortKeys`/etc.).

**Judgment calls:**
- `"barcode"`: `Item.Barcodes` is a jsonb-mapped `List<string>` with no natural single sortable scalar, and this column has a documented history of LINQ shapes that build fine but throw against real Postgres (`ItemRepository.GetByBarcodeAsync`'s existing comment). Rather than risk a repeat, `"barcode"` falls back to the same order as `"name"` in both directions — documented in `ItemSortKeys` and `ItemRepository.ApplySort`. Verified live against Postgres: `sortBy=barcode` returns byte-identical ordering to `sortBy=name`.
- `"category"`: null category always sorts last regardless of direction (typical "uncategorized" UI expectation), via a primary always-ascending `Category == null ? 1 : 0` key, `ThenBy`/`ThenByDescending(Category.Name)` for direction. Verified live: the last page of a 216-item catalog contains exactly the 16 uncategorized items for both `sortDescending=true` and `false`.
- No per-key direction override otherwise (e.g. price ascending-first) — followed the established blanket convention from Stock/Receipts (which default text columns like "productname"/"supplier" to descending too, not just numeric ones) for consistency across the app.

## Verification

- `dotnet build`: clean.
- Manually tested both endpoints against the real local dev Postgres (crm on :5435, `shelfguard_app_dev` role) via a running API instance + JWT login as seeded `manager@demo.local`: categories list correct/ordered/scoped; each sortBy key reorders rows; unrecognized sortBy returns 200 (no error); `search`/`category_id` filters unaffected.
- `dotnet test`: full suite — pre-existing flaky failures unrelated to this change (Postgres `53300: sorry, too many clients already` on `AudienceBuilderRepositoryIntegrationTests`/`MobileThemeServiceRlsIntegrationTests`/`SupplierAgreementMarkSignedRlsIntegrationTests`, connection-pool contention from running many real-DB integration tests in parallel — reproduced with varying counts, 16 then 22, across two full runs, confirming it's pre-existing environmental flakiness, not a regression). Targeted filter (`Catalog|ItemRepository|WriteOffService`, 94 tests incl. the new sort integration tests) — **0 failures**.
- Added `ShelfGuard.Tests/Infrastructure/ItemRepositoryGetPagedSortIntegrationTests.cs` (real Postgres, same template as `StockRepositoryGetPagedSearchSortIntegrationTests`) covering: default order preserved, unrecognized-key fallback, category null-last both directions, all 4 numeric keys' default-descending, barcode fallback to name order (both directions), and a regression check that `categoryId` filtering still composes with sort.
- Updated pre-existing call sites for the new `IItemRepository`/`IItemService.GetPagedAsync` params (no interface defaults, matching this week's Stock/Receipt precedent): `ItemsControllerTests.cs`, `ItemServiceTests.cs`, `ItemRepositoryGetPagedTests.cs`, `ItemRepositoryGetPagedBarcodeSearchIntegrationTests.cs`, `WriteOffService.cs` (calls `IItemRepository.GetPagedAsync` directly to hydrate write-off line items) + `WriteOffServiceTests.cs`, and two hand-written `IItemRepository` fakes (`PosServiceTests.cs`, `FiscalizationRetryTests.cs`) that needed the new method signature implemented.

## Files touched

- New: `ShelfGuard.Domain/Interfaces/ICategoryRepository.cs`, `ShelfGuard.Infrastructure/Data/Repositories/CategoryRepository.cs`, `ShelfGuard.Application/Features/Catalog/{ICategoryService,CategoryService,ItemSortKeys}.cs`, `ShelfGuard.Application/Features/Catalog/Dtos/CategoryDto.cs`, `ShelfGuard.Api/Controllers/CategoriesController.cs`, `ShelfGuard.Tests/Infrastructure/ItemRepositoryGetPagedSortIntegrationTests.cs`
- Modified: `ShelfGuard.Infrastructure/DependencyInjection.cs`, `ShelfGuard.Application/DependencyInjection.cs`, `ShelfGuard.Domain/Interfaces/IItemRepository.cs`, `ShelfGuard.Infrastructure/Data/Repositories/ItemRepository.cs`, `ShelfGuard.Application/Features/Catalog/{IItemService,ItemService}.cs`, `ShelfGuard.Api/Controllers/ItemsController.cs`, `ShelfGuard.Application/Features/WriteOffs/WriteOffService.cs`, plus the test files listed above.
