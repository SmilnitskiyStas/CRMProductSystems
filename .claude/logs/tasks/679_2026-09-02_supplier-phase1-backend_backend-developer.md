# TASK-679 — Supplier-portal expansion Phase 1 (backend + worker)

**Status:** review · **Agent:** backend-developer · Plan: `.claude/plans/1-partitioned-book.md` Phase 1

## What changed

### Migration `AddSupplierExpansionFoundations` (`20260902203915_...`)
EF-generated, additive only — 3 `AddColumn` / 3 `DropColumn`, **no RLS changes** (new nullable
columns inherit the existing triad on `marketplace_orders` / `users`):
- `marketplace_orders.CreatedByUserName varchar(255) NULL` (#4)
- `marketplace_orders.ExpectedDeliveryDate date NULL` (D5 — column landed now, endpoint in Phase 4)
- `users.SupplierOrdersLastViewedAt timestamptz NULL` (#3 seen-marker, badge endpoint in Phase 6a)

Domain: `MarketplaceOrder.CreatedByUserName` / `.ExpectedDeliveryDate` (public set);
`User.SupplierOrdersLastViewedAt` (private set) + `User.MarkSupplierOrdersViewed()`.
EF config in `AppDbContext` (User ~L247, MarketplaceOrder ~L2099); snapshot regenerated
(clean 3-property diff). **Not applied to any database.**

Generated via `dotnet ef migrations add ... --startup-project ShelfGuard.Infrastructure` (the
`--startup-project ShelfGuard.Api` path is blocked — another session's dev server holds a lock on
`ShelfGuard.Api/bin/Debug/net8.0/*.dll`; Infrastructure has an `IDesignTimeDbContextFactory`).

### Supplier warehouses — `Application/Features/SupplierInventory/` (new)
`ISupplierWarehouseService` + `SupplierWarehouseService` — thin wrapper over `ILocationService`:
`ListAsync` / `CreateAsync` / `UpdateAsync` / `DeactivateAsync(tenantId, …)`. DTOs
`SupplierWarehouseDto` / `CreateSupplierWarehouseRequest` / `UpdateSupplierWarehouseRequest`.
DI in `ShelfGuard.Application/DependencyInjection.cs` (services live there, not Infrastructure).

**Warehouse-type field decision:** `Location` has both `Type` and a dead `LocationType`
property (LoyaltyService.cs:378-383 documents `LocationType` is never read by Application, and
`LocationService` writes `CreateLocationRequest.LocationType` onto entity `Type`, maps
`LocationDto.LocationType` back from entity `Type`). So a warehouse = pass
`CreateLocationRequest{ LocationType = "warehouse" }` → entity `Type = "warehouse"` (accepted by
`LocationService.IsValidLocationType`); list/guard on `LocationDto.LocationType == "warehouse"`.
**No change to `LocationService` / `CreateLocationRequest` needed.** Entity `LocationType` stays
at its dead default, consistent with every other location. `store_scope` RESTRICTIVE RLS is on
location-scoped *data* tables, NOT on `locations` itself, so a supplier_admin with zero
`user_locations` reads/writes its own warehouses fine.

`UpdateAsync`/`DeactivateAsync` do an app-layer `BelongsToTenantAsync` + a warehouse-type guard
(defence in depth per the TASK-392b convention) → `WarehouseNotFoundError` otherwise.

### `SupplierCabinetWarehousesController` (new)
`[Route("api/supplier-cabinet/warehouses")]`, `[Authorize(Policy = AppPolicies.SupplierCabinet)]`
+ `[RequireModule("supplier_inventory")]`; every action gated
`SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.WarehouseManagement)`.
`GET ""` / `POST ""` / `PUT "{id}"` / `POST "{id}/deactivate"`. Own `ResolveTenantId()` copied
from `SupplierCabinetController` (no shared helper exists).

### #4 — surface the order creator
`MarketplaceOrderService` — added `IUserRepository` ctor param (appended last; updated the 2 RLS
integration-test `BuildOrderService` factories + `MarketplaceOrderServiceTests` ctor).
`CreateOrderAsync` resolves the placing user under the client session
(`_users.GetByIdAsync(userId)`) → `order.CreatedByUserName = creator?.FullName`.
`MarketplaceOrderDto` += `CreatedByUserId` + `CreatedByUserName` (positional, before `Items`);
`ToDto` maps them.

### #3 — supplier gets notified of a new order
`CreateOrderAsync` after the order save now enqueues a `marketplace_order.created` outbox row
**to the supplier tenant** (`order.SupplierTenantId`) wrapped in
`_tenantSessionOverride.ExecuteAsync(order.SupplierTenantId, …)` — same cross-tenant-outbox
pattern as the shipped branch (notification_queue is session-tenant-only, CreateOrderAsync runs
on the client session). New `EnqueueCreatedNotificationAsync` helper; title
`"Нове замовлення {OrderNumber} від «{clientName}»"`, payload
`{ orderId, orderNumber, clientName, totalAmount, itemCount }`. Best-effort (separate step, not
folded into the order insert) — a failed enqueue must not fail an already-persisted order.

### Worker — `notification-dispatch.job.ts`
`DISPATCH_EVENT_ROLES` += 3 entries:
- `marketplace_order.created` → `{ roles: ["supplier_admin"], channels: ["telegram","push"] }`
- `marketplace_order.shipped` → `{ roles: ["merchandiser","store_manager","network_manager","enterprise_admin"], channels: ["telegram","push"] }` **(was silently dropped — no matrix row)**
- `marketplace_order.delay_reason_added` → same roles/channels **(was silently dropped)**

Role choice for shipped/delay: matched `receipt.created` exactly (a marketplace order arriving IS
an incoming delivery) → **`merchandiser`, not `storekeeper`**. `receipt.created` also omits
`storekeeper` — flag for product if the warehouse-receiving audience should include storekeeper.
`formatText` icons added for the 3 types (body already falls back to `row.title`, which the
backend sets).

### `NotificationService.ValidEventTypes` += the 3 event types (toggleable in settings).

### `.claude/docs/api-contracts.md` — warehouse endpoints + `MarketplaceOrderDto` new fields +
`marketplace_order.created` note.

## Build / tests

- `dotnet build -c Release` (whole backend solution) — **clean, 0 err** (Debug blocked by the
  other session's running API holding DLL locks; Release output path is separate).
- `worker` `npx tsc --noEmit` — **clean**.
- New unit tests: `SupplierWarehouseServiceTests` (8, list-filter / create-forces-type /
  ownership-reject / type-reject / deactivate) + 2 in `MarketplaceOrderServiceTests`
  (`CreatedByUserName` snapshot + supplier notification; unknown-creator → null). All green.
- Filtered run `~Marketplace|~Tenant|~Location|~Supplier|~Notification` (Release): **689 passed,
  5 failed**. `RlsCrossTenantIntegrationTests` audit — **green (6/6)**.

### ⚠️ The 5 failures are ALL the pending migration, not logic
All 5 are live-Postgres integration tests (`MarketplaceOrderCatalogConflictsRlsIntegrationTests`,
`MarketplaceProviderBypassScopeRlsIntegrationTests`) failing with
`42703: column "SupplierOrdersLastViewedAt" of relation "users" does not exist` — they seed real
`users`/`marketplace_orders` rows and EF now emits the new columns. The local test DB
(`localhost:5435/crm`) needs `AddSupplierExpansionFoundations` applied. Task said "do NOT apply to
any database" and `dotnet ef database update` is classifier-blocked, so **left pending** — whoever
runs the RLS regression pass must `dotnet ef database update` against the test DB first (standard
for every migration-adding task in this repo). Before this change those 5 were green.

## Deviations / notes

- DI registered in `ShelfGuard.Application/DependencyInjection.cs` (task said Infrastructure — but
  that file only registers repositories; all Application services are in the Application DI file).
- `GET /api/settings/modules` already works for `supplier_admin` — it's `[Authorize]`-only and
  resolves the tenant from the JWT. No backend change needed (plan's "впустити supplier_admin" is
  already satisfied).
- Migration NOT applied to any DB (dev, test, or prod).

## For the Phase 1 frontend agent

- **Warehouse endpoints:** `GET/POST /api/supplier-cabinet/warehouses`,
  `PUT /api/supplier-cabinet/warehouses/{id}`, `POST /api/supplier-cabinet/warehouses/{id}/deactivate`.
  Gate: module `supplier_inventory` + permission `warehouse_management`.
- **`SupplierWarehouseDto { id, name, address?, regionCode?, isActive }`**;
  `CreateSupplierWarehouseRequest { name, address?, regionCode? }`;
  `UpdateSupplierWarehouseRequest { name, address?, regionCode?, isActive }`. `regionCode` = ISO
  3166-2:UA code (reuse the existing `GET /api/geo/regions` picker).
- **`MarketplaceOrderDto`** now has `createdByUserId?: string`, `createdByUserName?: string` —
  add to `features/marketplace/types.ts` + `features/supplier-cabinet/types.ts`, render a
  "Замовив" column in both order tables.
- **i18n — 3 new notification event types** for the settings toggle list + notification rendering:
  `marketplace_order.created` (supplier side — "Нове замовлення"),
  `marketplace_order.shipped` (client side — "Замовлення відправлено"),
  `marketplace_order.delay_reason_added` (client side — "Затримка доставки").
- `GET /api/settings/modules` already returns for `supplier_admin` — safe to drop the
  `&& !isSupplierAdmin` guard on `useModules`.
