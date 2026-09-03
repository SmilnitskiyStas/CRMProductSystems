# TASK-688 — Supplier portal expansion Phase 6a/6b/6e (backend)

**Status:** review (NOT committed) · **Agent:** backend-developer · Plan `1-partitioned-book.md` Phase 6
**Base:** HEAD `05cc2445` (Phases 1–5 done). 6c/6d = separate agent.

## 6a — "new order arrived" badge (#3)

- `IMarketplaceOrderRepository.CountUnseenForSupplierAsync(supplierTenantId, DateTimeOffset? since, ct)`
  → non-cancelled orders; `since` null → all non-cancelled, else `CreatedAt > since`. Cancelled
  excluded on both paths (decision — not an actionable "new order").
- `IMarketplaceOrderService` += `GetUnseenOrderCountForSupplierAsync(supplierTenantId, userId, ct)`
  (loads user, converts `SupplierOrdersLastViewedAt` → `DateTimeOffset`, delegates to repo) and
  `MarkSupplierOrdersSeenAsync(userId, ct)` (`user.MarkSupplierOrdersViewed()` + save). No
  cross-tenant override — the user row is in the supplier's own tenant.
- `SupplierCabinetCooperationController`: `GET /orders/unseen-count → { count }`,
  `POST /orders/mark-seen → 204`. Class gate `SupplierCabinet` + `marketplace_supplier`, no extra
  permission. New `UnseenOrdersCountDto` in `CooperationDtos.cs`.

## 6b — supplier demand analytics (#7)

New `Application/Features/SupplierAnalytics/`:
- `ISupplierAnalyticsService` (one method `GetAsync(supplierTenantId, from, to, ct)`), impl does
  the roll-up in memory (marketplace volume is low). Range capped at 366 days (clamps `from`
  forward, echoes effective window); `from>to` swapped. Prev-window = equal-length window ending
  the day before `from`. Reuses `PeriodMetricDto.Of` (TASK-336).
- `ISupplierAnalyticsRepository` + `SupplierAnalyticsRepository` (Infrastructure):
  `GetOrderLinesAsync` (`marketplace_order_items ⋈ marketplace_orders`, `SupplierTenantId == me`,
  `Status != cancelled`, `CreatedAt` in `[from, to+1)`), `GetAvailableCatalogAsync`
  (`supplier_items WHERE TenantId == me AND IsAvailable`).
- `SupplierAnalyticsDto`: `from,to`, `totalRevenue/orderCount/itemsSold` + `*Delta` PeriodMetricDto,
  `topItems`/`slowItems` (`SupplierAnalyticsItemDto { supplierItemId?, itemName, qtySold, revenue,
  orderCount }`, top10 by Σqty desc / slow10 by Σqty asc incl. zero-demand via LEFT JOIN onto the
  available catalog), `byBuyer` (`{ clientTenantId, clientName, orderCount, revenue }`, names via
  `ISupplierChatRepository.GetTenantDisplayNameAsync`), `revenueTrend` (`{ date, revenue, orderCount }`
  daily).
- `SupplierCabinetAnalyticsController`: `GET /api/supplier-cabinet/analytics?from=&to=`. Class gate
  `SupplierCabinet` + `marketplace_supplier`; action gate `SupplierPermissions.AnalyticsView`.
  Default window last 30 days.
- DI: `Application/DependencyInjection.cs` + `Infrastructure/DependencyInjection.cs`.

## 6e — SupplierItem ↔ platform category + category search (#8)

- Migration `20260903124945_AddSupplierItemPlatformCategory`: `supplier_items.PlatformCategoryId uuid NULL`
  FK → `platform_categories("Id")` ON DELETE SET NULL, index `(TenantId, PlatformCategoryId)`.
  EF folded the standalone `IX_supplier_items_TenantId` into the new composite (leading col still
  serves the FK + RLS `tenant_isolation`). No RLS change. **Applied to dev DB `:5435/crm`** via
  idempotent script (docker exec psql) — `__EFMigrationsHistory` row present, column/indexes/FK
  verified. NOT prod.
- `SupplierItem.PlatformCategoryId` (`Guid?`, public set) + `PlatformCategory?` nav; EF config +
  snapshot regenerated.
- `SupplierItemDto` += `platformCategoryId`, `platformCategoryName` (last two positional params —
  all existing `new SupplierItemDto(...)` positional calls still compile). `AdminAddSupplierItemDto`
  / `AdminUpdateSupplierItemDto` += `platformCategoryId?`.
- `MarketplaceService` ctor += `ICategoryRepository`. `AdminAddSupplierItemAsync` /
  `AdminUpdateSupplierItemAsync` validate the FK (must exist + `IsActive`, else tuple error
  `PlatformCategoryNotFoundError`), persist it; update patch semantics: null/omit = leave,
  `Guid.Empty` = clear, other = validate+set. `MarketplaceRepository` reads Include
  `PlatformCategory` (3 query sites). Both `ToItemDto` (MarketplaceService + SupplierCabinetService)
  emit the two new fields.
- `ICategoryRepository.SearchActiveAsync(tenantId, term, limit, ct)` → `CategorySearchRow`
  (`{ Id, Name, ParentName?, ItemCount }`), `EF.Functions.ILike` over active `platform_categories`
  + parent self-join + per-tenant `items` count. `ICategoryService.SearchAsync` clamps limit 1..50
  (default 20). `CategoriesController` `GET /api/categories/search?q=&limit=` → `CategorySearchResultDto[]`.
  All active categories regardless of business type (plan decision).

## Verification

- `dotnet build -c Release` clean (0 errors).
- `dotnet test -c Release --filter "SupplierAnalytics|MarketplaceOrder|Category|Marketplace|RlsCrossTenant"`
  → **465 passed, 0 skipped** (integration tests ran against dev Postgres). RLS audit
  (`AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass`) green.
- Adjacent buckets `SupplierCabinet|SupplierInventory|Analytics|SupplierItem` → 423 passed.
- New tests: `SupplierAnalyticsServiceTests` (8 facts — totals, period deltas, top/slow/zero-demand,
  byBuyer, trend, 366-day clamp), `SupplierAnalyticsRepositoryIntegrationTests` (3 — window/cancelled/
  own-supplier filter, inclusive boundary, available-catalog), 6a facts in `MarketplaceOrderServiceTests`
  (5 — null marker, marker passthrough, mark+save, mark→count 0, unknown user no-op),
  6e facts in `MarketplaceServiceTests` (7 — persist+name, unknown/inactive FK reject, set/clear,
  omit leaves), `CategoryServiceTests` (limit clamp + mapping), `CategoryRepositorySearchIntegrationTests`
  (3 — ILIKE case-insensitive + inactive excluded + parentName, per-tenant item count, blank term).
- 3 `new MarketplaceService(...)` sites in `MarketplaceProviderBypassScopeRlsIntegrationTests` +
  the `MarketplaceServiceTests` ctor updated for the new `ICategoryRepository` param.

## Not done / notes

- openapi.json — shared debt (deferred since TASK-670), not regenerated.
- `mobile/`, worker, `supplier_metrics*` untouched (6c/6d = other agent).
- `AppDbContextModelSnapshot.cs` also touched by the 6c/6d migration — re-verify build before commit
  if both land.
