# TASK-585: Marketplace order delay reason — service/API layer

**Status:** done · **Agent:** backend-developer · **Depends on:** TASK-584, database-engineer schema (handoff `585-to-backend_database-engineer.md`)

## What changed

- `Dtos/CooperationDtos.cs`: `MarketplaceOrderDto` gains `string? DelayReason` (after `DeliveredAt`); new `SetOrderDelayReasonDto(string Reason)`.
- `IMarketplaceOrderService` / `MarketplaceOrderService`: new `SetDelayReasonAsync(supplierTenantId, orderId, reason, ct)`.
  - Validates: non-empty reason → order exists & belongs to supplier tenant → order.Status == Shipped.
  - New error constants `DelayReasonRequiredError`, `OnlyShippedCanHaveDelayReasonError`.
  - Sets `order.DelayReason` (trimmed) + `UpdatedAt`, enqueues client-tenant notification (`marketplace_order.delay_reason_added`) under `_tenantSessionOverride.ExecuteAsync(order.ClientTenantId, ...)` — same cross-tenant RLS pattern as TASK-584's Shipped branch / TASK-582's `MarkSignedAsync` fix. No new DI.
  - `ToDto` passes through `DelayReason`.
- `SupplierCabinetCooperationController`: `POST /api/supplier-cabinet/orders/{id}/delay-reason` — mirrors `UpdateOrderStatus` action shape exactly (404 on `OrderNotFoundError`, 400 on any other error, 200 with dto on success).
- Tests: 10 new cases in `MarketplaceOrderServiceTests.cs` — empty/whitespace reason (3), order not found, foreign supplier tenant, not-shipped status (4 statuses), happy path (sets reason + asserts `ITenantSessionOverride`/notification calls, mirroring the TASK-584 shipped-notification assertion style).
- Docs: `.claude/docs/api-contracts.md` — added the new endpoint line under Supplier Cooperation section; also filled in the `estimatedDeliveryDays`/status-branch detail on the existing `/orders/{id}/status` line that TASK-584 had left undocumented.

## Not touched (by design)

- `MarketplaceOrder` entity, migrations — schema layer already done.
- `frontend/` — out of scope, follow-up session.

## Verification

- `dotnet build` (backend, no-build cache clean): 0 errors, 1 pre-existing unrelated warning.
- `dotnet test`: **1765/1765 passed** (1755 baseline + 10 new).
- Live-DB spot check skipped by design — same `ITenantSessionOverride` code path already integration-verified by TASK-584 on this exact service.
