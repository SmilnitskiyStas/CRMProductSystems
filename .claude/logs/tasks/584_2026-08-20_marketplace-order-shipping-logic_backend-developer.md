# TASK-584 (part 2/3) — Marketplace order shipping logic — backend-developer

**Status:** done

## What changed

- `Dtos/CooperationDtos.cs`: `UpdateMarketplaceOrderStatusDto` gained
  `EstimatedDeliveryDays` (int?, 3rd positional param). `MarketplaceOrderDto` gained
  `ShippedAt`, `EstimatedDeliveryDays`, `DeliveredAt` (inserted after `UpdatedAt`, before `Items`).
- `MarketplaceOrderService.cs`:
  - New constant `EstimatedDeliveryDaysRequiredError`.
  - `UpdateOrderStatusAsync`: `Shipped` transition now requires `EstimatedDeliveryDays > 0`
    (else returns the new error, no state change), sets `ShippedAt = UtcNow` +
    `EstimatedDeliveryDays`. `Delivered` sets `DeliveredAt = UtcNow`.
  - Added `INotificationRepository` + `ITenantSessionOverride` constructor deps (both already
    DI-registered). On the `Shipped` transition only, `_orders.Update` + a new
    `marketplace_order.shipped` outbox row (`EnqueueShippedNotificationAsync`) + the final
    `SaveChangesAsync` all run inside `_tenantSessionOverride.ExecuteAsync(order.ClientTenantId, ...)`
    — mirrors `SupplierAgreementService.MarkSignedAsync`'s TASK-582 fix exactly, so the same
    cross-tenant RLS-violation bug (unscoped insert with `TenantId = counterparty` while the
    session is authenticated as the other tenant → uncaught 42501 → masked 500/CORS) cannot
    recur here. `Delivered`/`Confirmed`/`Cancelled` keep the original unwrapped
    `Update` + `SaveChangesAsync` flow (no cross-tenant write needed).
  - `ToDto` passes through the three new fields.
- `SupplierCabinetCooperationController.cs`: verified, no change needed — `POST
  orders/{id}/status` binds `[FromBody] UpdateMarketplaceOrderStatusDto request` straight
  through; record JSON binding is additive.
- `MarketplaceOrderServiceTests.cs`: added `INotificationRepository` +
  `ITenantSessionOverride` substitutes (pass-through `ExecuteAsync`, same convention as
  `SupplierAgreementServiceTests`). New tests: ship without/with invalid
  `EstimatedDeliveryDays` (null/0/-1) → error, no state/SaveChanges change; ship with valid
  days → `Shipped` + `ShippedAt` set + `ITenantSessionOverride.ExecuteAsync` called with
  `order.ClientTenantId` + `NotificationQueue` enqueued with correct
  Tenant/User/Channel/Status/EventType; deliver → `DeliveredAt` set, override NOT invoked
  (no cross-tenant write on that path). Also fixed the pre-existing
  `UpdateOrderStatus_TransitionMatrix` theory to pass a valid `EstimatedDeliveryDays` (it
  exercises the `Confirmed → Shipped` allowed case).

## Verification

- `dotnet build` in `/backend`: clean, 0 errors (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`).
- `dotnet test`: **1755/1755 passed**, 0 failed, 59s — includes the live-Postgres
  `SupplierAgreementMarkSignedRlsIntegrationTests` (same override pattern, proves it works
  end-to-end against real RLS).
- Manually inspected `notification_queue`'s live schema/RLS in the local dev DB
  (`crmproductsystems-postgres-1`): `tenant_isolation` policy's `WITH CHECK` requires
  `TenantId = session app.tenant_id` — confirms the override is load-bearing, not
  precautionary. Did not add a new dedicated live-Postgres integration test for this path
  (optional per the brief) — the mock-level assertions plus the existing sibling
  integration test covering the identical pattern were judged sufficient.

## For next agent (frontend-developer)

See `.claude/logs/handoffs/584-to-frontend_backend-developer.md` for exact DTO shapes.
