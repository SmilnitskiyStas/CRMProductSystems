# TASK-685 — Supplier Phase 4: "in transit" for auto-order + mutable delivery date (D5 / п.2)

**Agent:** backend-developer · **Plan:** `1-partitioned-book.md` Phase 4 · **Status:** review, not committed

## The bug fixed

An open B2B marketplace order (buyer side, not yet received) was invisible to
`OrderCalcService`/`AiOrderService`, so the replenishment engine kept re-recommending goods
already on the way. Plus: the supplier can now reschedule a shipped order's delivery date
repeatedly.

## Migration `20260903112807_AddMarketplaceOrdersReplenishmentIndex`

Pure DDL — no tables/columns/RLS. One hand-written raw-SQL partial index (same treatment as
`ix_marketplace_orders_metrics`):
`ix_marketplace_orders_open_by_dest ("ClientTenantId","DestinationStoreId","Status") WHERE "Status" IN ('new','confirmed','shipped')`.
Snapshot unchanged (partial indexes are not model-tracked). **Applied to dev DB `:5435/crm`**
via idempotent script; verified in `pg_indexes`. **Not applied to prod.**

## Backend

- `IOrderCalcRepository` / `OrderCalcRepository` += `GetOpenMarketplaceInTransitAsync(storeId,
  productIds, tenantId, ct)` — `marketplace_order_items ⋈ marketplace_orders ⋈ items` on
  `oi.SupplierItemId == it.SourceSupplierItemId`, `o.DestinationStoreId == storeId`, status in
  new/confirmed/shipped, `it.TenantId == tenantId`, **`oi.Unit == it.Unit`** (unit-mismatch rows
  excluded — plan п.2), GROUP BY `it.Id` SUM `oi.Qty` → `Dictionary<Guid,decimal>`. Same
  LINQ shape as the existing `GetInTransitAsync`.
- `OrderCalcService` — injects `ITenantContext`; `transit = draftReceipts[pid] +
  openMarketplace[pid]` folded into the **single** `InTransit` term the formula already
  subtracts (no new formula input). Skips the marketplace query when `TenantContext.TenantId` is
  null (never happens on the authorized `/api/orders/calculate` + AI-order endpoints).
- `OrderLineDto` += `InTransitFromMarketplace` (trailing, default `0m`) — the marketplace slice
  of `InTransit`, for the frontend order-review source-breakdown tooltip.
- `AiOrderService` — comment only; it reads in-transit exclusively through
  `_orderCalc.CalculateAsync`, so the combined figure flows into the Claude context unchanged.
- `MarketplaceOrderService.SetExpectedDeliveryDateAsync(supplierTenantId, orderId, date)` —
  mirrors `SetDelayReasonAsync`: guards order-exists + own-supplier, `Status == shipped`, date
  not in the past; **no "already set" guard** (repeatable). Sets `ExpectedDeliveryDate` +
  `UpdatedAt`; new `EnqueueDeliveryRescheduledNotificationAsync` (EventType
  `marketplace_order.delivery_rescheduled`, `Channel "system"`, targets `order.ClientTenantId`)
  runs inside `_tenantSessionOverride.ExecuteAsync(order.ClientTenantId, …)` exactly like the
  delay-reason path. Error consts `OnlyShippedCanRescheduleError`, `RescheduleDateInPastError`.
  (Ship-branch already sets `ExpectedDeliveryDate` at ship time — done in Phase 3, untouched.)
- `IMarketplaceOrderService` += method. `CooperationDtos` += `SetOrderExpectedDeliveryDateDto`.
- `SupplierCabinetCooperationController` += `POST orders/{id}/expected-delivery-date` — mirrors
  the `delay-reason` action (class-level `SupplierCabinet` policy + `marketplace_supplier`
  module only; **no** `supplier_inventory`/`warehouse_management` gate — it is a delivery-comms
  action, not a warehouse op).
- `NotificationService.ValidEventTypes` += `marketplace_order.delivery_rescheduled`.

## tenantId-for-items-join decision

`items` **has** the canonical `tenant_isolation` RLS policy (confirmed:
`FixFailOpenTenantIsolationOnReset` lists `items`), and `OrderCalcService.CalculateAsync` runs on
the buyer's own staff session, so ambient RLS already scopes the join to the buyer tenant.
**Kept the explicit `it.TenantId == tenantId` filter anyway** (signature takes `tenantId` from
`ITenantContext`): it is a cross-tenant marketplace join, the predicate is index-backed
(`idx_items_tenant_category_segment_active` leads on `TenantId`) and cheap, and it keeps the
query correct if ever reached from a bypass/worker session where `items` RLS is off.
`marketplace_orders`' RLS is OR-based on Supplier/Client tenant, so the buyer session naturally
sees only its own orders — no override needed.

## Worker

`worker/src/jobs/notification-dispatch.job.ts` — `marketplace_order.delivery_rescheduled` added
to `DISPATCH_EVENT_ROLES` (same as `marketplace_order.shipped`:
`["merchandiser","store_manager","network_manager","enterprise_admin"]`, `["telegram","push"]`)
+ `formatText` icon `📅`.

## Verification

- `dotnet build -c Release` — 0 errors.
- `dotnet test -c Release --filter "FullyQualifiedName~OrderCalc|~AiOrder|~MarketplaceOrder"` —
  **147 passed, 0 failed**. `~RlsCrossTenantIntegrationTests` — 6 passed (audit green, no new
  tables). `~Notification` — 19 passed.
- New tests: `OrderCalcServiceTests` (mock — marketplace lowers Raw; draft + marketplace
  additive; breakdown 0 with no order; missing tenant skips the query),
  `OrderCalcRepositoryOpenMarketplaceInTransitTests` (InMemory — open counts, delivered/cancelled
  don't, unit-mismatch excluded, other store/tenant excluded, multi-line summed),
  `MarketplaceOrderServiceTests` +5 (reschedule happy path + notify, non-shipped rejected, past
  date rejected, foreign supplier 404, repeatable).
- Worker `npx tsc --noEmit` — clean.

## Not done / debt

- Frontend (types + editable date on shipped orders + in-transit tooltip + i18n) — Phase 4
  frontend agent.
- `backend/openapi.json` regen — shared debt (TASK-670..).
- Prod migration not applied. Not committed. `mobile/` untouched.
