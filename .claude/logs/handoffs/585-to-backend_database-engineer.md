# Handoff: TASK-585 schema → backend-developer

## New field

`backend/ShelfGuard.Domain/Entities/MarketplaceOrder.cs`:
```csharp
/// <summary>Supplier-entered explanation when delivery runs past the estimated window.</summary>
public string? DelayReason { get; set; }
```
- Type: `string?`, nullable, free-text, no max-length validation enforced beyond the DB column (2000 chars).
- Located right after `DeliveredAt` (TASK-584).
- EF config in `AppDbContext.cs` (`builder.Entity<MarketplaceOrder>`): `HasMaxLength(2000).IsRequired(false)` — same as `CancelReason`.
- Column: `marketplace_orders.DelayReason`, `character varying(2000)`, nullable, no default. Migration `20260820193144_AddMarketplaceOrderDelayReason`. Applied to local dev DB.

## Not touched (by design — your scope)

- `ShelfGuard.Application/Features/Marketplace/MarketplaceOrderService.cs` (or wherever the order status-transition/update logic lives) — no read/write of `DelayReason` yet.
- DTOs — no `DelayReason` on any request/response model yet.
- Controller — no endpoint/param wiring yet.

## Suggested shape for your slice (not binding, your call)

- Likely a supplier-only "set delay reason" action, separate from status transitions (order can already be `shipped`, sitting past `ShippedAt + EstimatedDeliveryDays`, not yet `delivered` — this doesn't need a new status, just an editable field on an existing shipped order).
- Client-visible on read (same order the client already sees `ShippedAt`/`EstimatedDeliveryDays` on) — check `MarketplaceOrderDto` (or equivalent) for where those three TASK-584 fields were exposed and mirror the pattern for `DelayReason`.
- Authorization: only the supplier tenant should be able to set/update it (parallel to how `CancelReason` is presumably supplier- or client-set depending on who cancels — check that existing logic for precedent).

## Verification already done (schema layer)

- `dotnet build` clean, `dotnet test` 1755/1755 passed, migration applied and column verified via psql. RLS unchanged (existing two-column policy + provider_bypass + worker_bypass cover it).
