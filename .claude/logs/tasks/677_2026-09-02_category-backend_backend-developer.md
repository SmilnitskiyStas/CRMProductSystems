# TASK-677 — B2: category backend (provider CRUD + business_type filter + item validation + uncategorized/subtree)

**Status:** done · **Agent:** backend-developer · Plan `.claude/plans/1-giggly-catmull.md` (Частина B / B2)
Builds on B1 (TASK-676, `platform_categories` global table, applied to dev DB, not committed).

## What changed

**1. Tenant read — `GET /api/categories` business-type filtered**
- `ICategoryService.GetAllAsync(Guid? tenantId, CancellationToken)`; `CategoryService` now injects
  `ITenantRepository`, looks up `Tenant.BusinessType`, filters **in memory** (jsonb `List<string>`
  doesn't translate to LINQ-to-SQL `Where`). Null tenantId (provider) → no filter. Empty
  `BusinessTypes` → visible to all. Case-insensitive match.
- `CategoriesController` injects `ITenantContext`, passes `TenantId`. `CategoryDto` shape unchanged.

**2. Provider CRUD — `ProviderCategoriesController` @ `api/provider/categories` `[ProviderOnly]`**
- New `ProviderCategoriesController` (GET tree incl. inactive / POST 201 / PUT 200 / DELETE 204
  soft-delete). Controller maps `error.Contains("not found") → 404` else 400.
- New DTOs `PlatformCategoryDto` / `CreatePlatformCategoryRequest` / `UpdatePlatformCategoryRequest`
  (`Features/Catalog/Dtos/PlatformCategoryDto.cs`).
- New `IProviderCategoryService` / `ProviderCategoryService`. Validation: name required + ≤255;
  business-type allow-list (inlined, matches `Tenant.UpdateBusinessType`) + dedupe + lower-case;
  parent must exist + no cycle (walk ParentId up, reject on self/loop); DELETE blocked by
  `HasActiveChildrenAsync`. `ItemCount` = one grouped query over `_db.Items` (provider_bypass →
  platform-wide).
- `ICategoryRepository` / `CategoryRepository` extended: `GetAllAsync` (incl. inactive,
  SortOrder→Name), `GetByIdAsync`, `ActiveExistsAsync`, `HasActiveChildrenAsync`,
  `CountItemsByCategoryAsync`, `AddAsync`, `Update`, `SaveChangesAsync`.
- DI: `IProviderCategoryService → ProviderCategoryService` in `Application/DependencyInjection.cs`.

**3. `ItemService` — validate `CategoryId` on create/update**
- Ctor `ItemService(IItemRepository, ICategoryRepository)`. Create + Update: `CategoryId` set and
  not `ActiveExistsAsync` → `(null, "Category not found or inactive.")`.

**4. `uncategorized` filter + parent subtree expansion**
- `bool? uncategorized = null` appended (before `ct`) to `IItemService`/`IItemRepository`
  `GetAllAsync` + `GetPagedAsync`; `ItemsController.GetAll` `[FromQuery] bool? uncategorized`.
- `ItemRepository`: new `ApplyCategoryFilterAsync` — `uncategorized==true` → `CategoryId==null`;
  else a set `categoryId` pulls the `platform_categories` (Id,ParentId) pairs once and closes the
  descendant set in memory, filtering items to that whole subtree.
- Fakes updated: `PosServiceTests.FakeCatalogRepo`, `FiscalizationRetryTests.RetryFakeCatalogRepo`.
  NSubstitute call-sites updated: `ItemServiceTests`, `ItemsControllerTests`, `WriteOffServiceTests`
  (index 4 = `ids` preserved). `new ItemService(...)` in 2 Marketplace RLS integration tests +
  `ItemServiceTests` ctor now pass a `CategoryRepository`/substitute.

**5. Analytics** — left as exact-match `CategoryId == categoryId.Value` in
`AnalyticsRepository.GetCategoryDetailAsync` / `GetByCategoryAsync` (drill-down/grouping report,
not the catalog filter; subtree semantics there would change report meaning + the "uncategorized"
branch). Noted, not in scope.

## Tests (new: +26 → 2200 total)
- `ProviderCategoryServiceTests` (16): create valid/blank/bad-bt/empty-bt/parent-missing/parent-ok,
  update valid/not-found/self-cycle/descendant-cycle, delete not-found/has-children/leaf, ItemCount.
- `CategoryServiceTests` (4): auto_service hides retail-only, retail sees retail+all-types,
  null tenantId sees all, case-insensitive.
- `ItemServiceTests` (+2): create/update bad CategoryId.
- `ItemRepositoryGetPagedTests` (+4): uncategorized-only, parent subtree, leaf exact, GetAllAsync subtree.
- `ItemsControllerTests` (+1): `uncategorized` plumbs through.

## Verification
- `dotnet build ShelfGuard.sln` — 0 errors (1 pre-existing warning in MarketplaceServiceTests).
- `dotnet test` filtered (`Catalog|Provider|Item|Analytics|WriteOff|Pos`) — 797/797.
- Full `dotnet test` — **2200/2200, 0 skipped, 0 regressions**.
- curl smoke (dev :5000, retail `manager@demo.local` + provider `admin@shelfguard.local`):
  `GET /api/categories` retail → 86; provider `GET /api/provider/categories` → itemCount populated;
  POST child+`businessTypes:["retail"]` → 201; POST bad bt → 400; POST as manager → 403;
  PUT rename + retag `["auto_service"]` → 200 then absent from retail `GET /api/categories`;
  PUT self-parent → 400 cycle; DELETE parent-with-child → 400; DELETE leaf → 204; DELETE unknown → 404.
  `GET /api/items?uncategorized=true` → 18 (of 217); reparent Ванилин under Батончики →
  `?category_id=<Батончики>` 4→5, leaf still 1, restored → 4. `POST /api/items` bogus categoryId → 400.
  Dev DB left clean (test category hard-deleted, reparent restored).

## Notes
- Backend :5000 was stopped for the build and **restarted** (preview `backend-dev`).
- **Not committed** (concurrent sessions on `main`).
- openapi.json regen pending (new provider endpoints + `uncategorized` param) — same batch as B1.
- Downstream (B3 frontend): `GET /api/categories` unchanged shape, now business-type filtered;
  `GET/POST/PUT/DELETE /api/provider/categories` with `PlatformCategoryDto`;
  `GET /api/items?uncategorized=true`; `category_id` now expands to subtree.
