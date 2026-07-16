# TASK-361 — Backend: Block 10 pre-launch audit — Auto Service & Production

**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-360

Block 10 of the pre-launch audit (`C:\Users\stass\.claude\plans\eager-pondering-tower.md`).
Scope: `Features/AutoService/` (customers, vehicles, work orders, service catalog) and
`Features/Production/` (recipes, production orders).

## Module activation

`AutoServiceController`/`ProductionController` both class-level `[Authorize]` +
`[RequireModule("auto_service")]` / `[RequireModule("production")]`. `RequireModuleFilter`
(`ShelfGuard.Infrastructure/Authorization/RequireModuleAttribute.cs`) correctly 403s when
the tenant lacks the module, 403s on missing/invalid `tenant_id` claim, and only bypasses
for the `provider` role (impersonation still goes through the real check). Already covered
by 5 existing tests in `RequireModuleFilterTests.cs` — no gaps found. `Tenant
.DefaultModulesForBusinessType` maps `auto_service`→`["auto_service","procurement"]`,
`production`→`["inventory","procurement","production"]`, matching v4-spec's module table.

**Old table/column names:** grepped both feature directories + their repositories for
`FROM stores`/`catalog_products`/raw SQL — none found, everything goes through EF LINQ.
`ProductStock.StoreId` / `StockEvent.StoreId` are the pre-existing repo-wide convention
(C# property kept as `StoreId`, mapped via `.HasColumnName("LocationId")` in
`AppDbContext`) — not a bug, matches every other module already audited in this series.

## Found + fixed (P1)

**Production: output batch could silently get a meaningless 10-year placeholder expiry.**
`ProductionService.CompleteOrderAsync` computed the produced batch's `ExpiryDate` from
`outputItem.ShelfLifeDays`, but `ShelfLifeDays` is nullable/unvalidated at `Item` creation —
if unset, the code fell back to `DateTime.UtcNow.AddYears(10)`. Not literally `null` (the
audit brief's specific concern), but functionally the same bug in disguise: FEFO/expiry
tracking for that batch is silently defeated and never surfaces to the user, inconsistent
with `ReceiptService`'s stricter pattern (hard-requires a real `ExpiryDate` per item before
confirming a receipt, no placeholder). Fixed: `CompleteOrderAsync` now validates
`outputItem.ShelfLifeDays` up front (before any ingredient consumption, atomic guarantee
preserved) and returns `422` with a clear message if missing/`<=0`, instead of consuming
raw materials and creating stock with a fake decade-out expiry. Removed the `AddYears(10)`
fallback and the duplicate `GetItemByIdAsync` call later in the method (reuses the
validated `outputItem`). One new test
(`CompleteOrderAsync_OutputItemMissingShelfLifeDays_Returns422_NoPartialWrites`) — asserts
422, no consumption, no stock event, order stays `Planned`.

## Reviewed, correct, no changes

- Production FEFO consumption correctly scoped to `order.LocationId`
  (`ProductionRepository.GetFefoOrderedAsync`) — matches Block 3's FEFO rules.
- No N+1 in either module's list endpoints — `GetWorkOrdersAsync`/`GetOrdersAsync` both
  eager-`.Include()` their navigation properties.
- RLS verified live against the dev DB (`pg_policies`, not just migration text) for
  `as_customers`, `as_vehicles`, `as_work_orders`, `as_work_order_lines`,
  `as_service_catalog`, `production_orders`, `recipes` — all carry the canonical
  `tenant_isolation` (NULLIF guard) + `provider_bypass` + `worker_bypass` fail-closed
  pattern from Block 2 (TASK-352). `recipe_ingredients`/`production_order_consumptions`
  deliberately have no `TenantId`/RLS of their own (documented in the entity XML comments
  — "tenant scope inherited from parent via JOIN"); verified no repository code path
  queries them independently of an already-tenant-scoped parent, so the pattern holds.
- Work order completion (`AutoServiceService.CompleteWorkOrderAsync`) mirrors Production's
  atomic pre-validate-then-consume FEFO pattern, already covered by
  `AutoServiceServiceTests.CompleteWorkOrderAsync_*` (insufficient stock / happy path).

## Flagged, not fixed

**KI-018 (medium, needs a product decision):** `AutoService` has no location concept at
all — `AsWorkOrder` carries no `LocationId`, so `AutoServiceRepository
.GetFefoOrderedAsync(itemId, ct)` consumes spare-part stock FEFO across the *entire
tenant*, not the location the work was actually done at. Production doesn't have this gap
(it scopes to `order.LocationId`); Auto Service does. Invisible for single-location
tenants, a real cross-location stock leak for auto-service chains — which v4-spec
explicitly supports via `location_type = auto_service`. Fixing this needs a schema
migration (`AsWorkOrder.LocationId`) plus API contract changes across create/complete;
deliberately out of scope for this audit block. Full writeup in `known-issues.md` KI-018.

**Minor, not fixed (low severity, noted only):**
- No composite indexes beyond single-column `TenantId`/`Status`/`RecipeId` on
  `as_work_orders`/`production_orders` for the `MechanicUserId`/`LocationId` list filters —
  same low-severity pattern already accepted in Block 3 for small per-tenant tables, not
  worth a migration on its own.
- `WorkOrderUpdateDto.MechanicUserId`/`ProductionOrderCreateDto`'s implicit `CreatedBy` are
  not explicitly validated against the caller's tenant before being persisted as an FK — in
  practice not exploitable as a cross-tenant data leak (RLS on `users` blocks the `Include`
  hydration, so a forged cross-tenant id just renders as a null mechanic name), but noted
  for completeness.

## Build/test status

`dotnet build`: 0 errors, 1 pre-existing unrelated warning (`MarketplaceServiceTests.cs`).
`dotnet test`: **869/869 green** (was 868).

---

## Addendum (same day) — KI-018 remediation plan (planning only, no code)

User confirmed directly in chat: plan the KI-018 fix now, implement later. Researched exact
scope below; **no code changed** for this section, per the instruction. Style mirrors the
KI-015 (per-store POS shifts) plan from TASK-356's addendum.

### Exact current scope (verified by reading the code, not assumed)

- `AsCustomer` (`as_customers`) and `AsVehicle` (`as_vehicles`) — **no `LocationId` at all**,
  and none needed: a customer/vehicle isn't tied to a physical bay, it can be serviced at
  any of the tenant's locations across its lifetime. Confirmed no sibling entity in the
  module carries one either.
- `AsWorkOrder` (`as_work_orders`) — **no `LocationId`**. This is the right place for it:
  it's the order-level entity, exactly mirroring how `ProductionOrder.LocationId` (not
  `Recipe.LocationId`) already works for the sibling Production module.
- `AsWorkOrderLine` (`as_work_order_lines`) — no `LocationId`, and doesn't need one either,
  mirroring `ProductionOrderConsumption` (no `LocationId`, inherits scope from its parent
  order). Lines don't need their own copy once the parent order carries it.
- **The only FEFO call that's wrong:** `IAutoServiceRepository.GetFefoOrderedAsync(Guid
  itemId, CancellationToken ct)` (`ShelfGuard.Domain/Interfaces/IAutoServiceRepository.cs`)
  has no location parameter — compare directly against the already-correct sibling
  `IProductionRepository.GetFefoOrderedAsync(Guid itemId, Guid locationId, ct)`. This is a
  module-local interface, **not** the shared `IStockRepository.GetFefoOrderedAsync(Guid
  productId, Guid storeId, ct)` used by POS/WriteOffs/Transfers (that one is already
  location-scoped and is not the bug — `AutoService` just never reused its pattern when the
  module was built). The only call site is `AutoServiceRepository.cs:162-166`
  (`_db.ProductStocks.Where(s => s.ProductId == itemId && s.Quantity > 0)` — no `StoreId`
  filter), invoked once, from `AutoServiceService.CompleteWorkOrderAsync`
  (`AutoServiceService.cs:406`).
- **RLS consequence: none.** Verified live (`pg_policies` on the dev DB) that every
  `LocationId`-bearing table in this codebase (`production_orders`, `location_zones`,
  `weather_data`, etc.) keeps its `tenant_isolation` policy keyed purely on `TenantId` —
  `LocationId` is never referenced in a WHERE/RLS qual, only ever joined back to
  `locations.TenantId` on the two tables that do that (`location_zones`, `weather_data`,
  neither relevant here). Adding `AsWorkOrder.LocationId` needs **zero RLS policy changes** —
  it's a plain application-level filter/FK, same as `ProductionOrder.LocationId` today.

### Plan

**1. DB — additive migration (`database-engineer` scope, ~30 min):**
- `as_work_orders` gets `LocationId uuid NULL` (nullable — existing rows have no location
  and can't be backfilled with a real answer; NOT NULL would break the migration on any
  tenant with existing work orders).
- FK → `locations.Id`, `ON DELETE RESTRICT` (matches `ProductionOrder.Location`'s existing
  `OnDelete(DeleteBehavior.Restrict)`).
- New index `(TenantId, LocationId)` — mirrors `ProductionOrder`'s indexing, supports the
  new list filter (below).
- Migration name suggestion: `AddLocationIdToAsWorkOrders` (parallel to the original
  `V4AutoServiceSchema`/`V4ProductionSchema` migrations that created these tables).
- `Domain/Entities/AsWorkOrder.cs`: add `public Guid? LocationId { get; set; }` +
  `public Location? Location { get; init; }` nav property (nullable — deliberately not
  required at the domain level yet, see open question below).

**2. Backend — repository + service layer (`backend-developer` scope, ~2-3h):**
- `IAutoServiceRepository.GetFefoOrderedAsync`: add `Guid? locationId` param, filter
  `s.StoreId == locationId.Value` when provided (nullable to support the "existing order,
  no location set" transition case — see open question).
- `AutoServiceRepository.GetFefoOrderedAsync`/`GetWorkOrdersAsync`: thread the new param
  through, add an optional `locationId` list filter to `GetWorkOrdersAsync` (mirrors
  `IProductionRepository.GetOrdersAsync(status, recipeId, locationId, ct)` exactly).
- `AutoServiceService.CreateWorkOrderAsync`: accept `dto.LocationId` (new field on
  `WorkOrderCreateDto`), validate it resolves to a real `Location` for the caller's tenant
  before creating the order (same shape as the existing `Vehicle` existence check already
  in that method).
- `AutoServiceService.CompleteWorkOrderAsync`: pass `order.LocationId` into
  `GetFefoOrderedAsync` instead of the current tenant-wide call.
- `IAutoServiceService`/DTOs: `WorkOrderCreateDto` gets `Guid LocationId` (or `Guid?` during
  a transition window — open question below); `WorkOrderListItemDto`/`WorkOrderDetailDto`
  gain `LocationId`/`LocationName` for display, same shape as `ProductionOrderListItemDto
  .LocationName` already has.
- `AutoServiceController`: `GetWorkOrders` gets a new `[FromQuery] Guid? locationId` param;
  `CreateWorkOrder` needs no route change (`WorkOrderCreateDto` already carries the body).
- Test fallout: `AutoServiceServiceTests.cs` — every `CreateWorkOrderAsync`/
  `CompleteWorkOrderAsync` test needs a `LocationId` on its builder; new tests for the
  actual fix (two locations, same item, work order at Location A must not draw down
  Location B's stock — this is the regression test that actually proves KI-018 is closed).

**3. Frontend — location selector (`frontend-developer` scope, ~2-3h):**
- `CreateWorkOrderModal.tsx` (`frontend/features/auto-service/components/`) currently has
  no location field at all (verified — just Vehicle/Mechanic/Notes). Needs a new field,
  sourced the same way KI-015's plan already identified for POS: the app-wide
  `useStoreContext` (`frontend/lib/useStoreContext.ts`, Zustand + persist,
  `selectedStoreId`) backing the existing `StoreSelector` in `TopBar.tsx` — **no new
  selector component needed**, just read `useStoreContext().selectedStoreId` and send it as
  `locationId` on create. This is the same wiring pattern already proven for
  `/stock`/Dashboard since TASK-281.
- `types.ts` (`CreateWorkOrderRequest`, `WorkOrder*` types) — add `locationId`/
  `locationName`.
- Work order list/kanban (`WorkOrderKanban.tsx`, `WorkOrderCard.tsx`) — optionally show
  location per card for multi-location tenants (low priority, cosmetic).

### Open question (needs a product call before implementation, not an engineering one)

**What happens to work orders created before the migration (existing `LocationId = NULL`
rows), and to `CompleteWorkOrderAsync` when `order.LocationId` is null?** Two options:
- (a) Treat `NULL` as "unscoped, tenant-wide FEFO" — preserves today's (buggy but working)
  behavior for old orders, only new orders (which the UI will always populate going
  forward) get the fix. Simple, backward compatible, no backfill needed.
- (b) Require every tenant to backfill/set a location before their next work order can be
  completed — matches the FEFO-is-sacred philosophy more strictly but is a harder cutover
  (a tenant mid-way through an open work order created before the migration would get a new
  422 they didn't expect).
Recommend (a) for the actual implementation — same "additive, never breaks an in-flight
document" principle already used for `PosShift.ClosingCash` and `ProductStock`'s xmin
concurrency token in this audit series — but this is a product/UX call, not something to
decide unilaterally while only planning.

### Risk / effort estimate

- **Total:** ~1 day (DB ~30min, backend ~2-3h, frontend ~2-3h) + test fallout, close to
  KI-015's POS per-store estimate but smaller — Auto Service has one FEFO call site to fix
  vs. POS's four fiscal-factory call sites + a settings screen.
- **Risk:** low. Purely additive (nullable column, no RLS change, no breaking API change if
  option (a) above is taken — old callers omitting `locationId` keep working). The only way
  this becomes risky is if a tenant already has multi-location auto-service data relying on
  today's (broken) tenant-wide pooling behavior as if it were a feature — worth a quick
  check of production data before shipping, not before planning.
- **Blocking dependency:** none — independent of any other in-flight sprint work.
