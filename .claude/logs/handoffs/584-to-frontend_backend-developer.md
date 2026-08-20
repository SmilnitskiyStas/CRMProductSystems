# Handoff: TASK-584 backend-developer → frontend-developer

## Final DTO shapes

`backend/ShelfGuard.Application/Features/Marketplace/Dtos/CooperationDtos.cs`

```csharp
public record MarketplaceOrderDto(
    Guid Id,
    string OrderNumber,
    Guid AgreementId,
    Guid SupplierTenantId,
    Guid ClientTenantId,
    string SupplierName,
    string ClientName,
    string Status,
    string? Comment,
    string? CancelReason,
    decimal TotalAmount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ShippedAt,          // NEW — set when Status transitions to "shipped"
    int? EstimatedDeliveryDays,         // NEW — supplier-entered whole days, set at same time as ShippedAt
    DateTimeOffset? DeliveredAt,        // NEW — set when Status transitions to "delivered"
    IReadOnlyList<MarketplaceOrderItemDto> Items);

public record UpdateMarketplaceOrderStatusDto(
    string Status,
    string? Reason = null,
    int? EstimatedDeliveryDays = null);  // NEW — REQUIRED (must be > 0) when Status = "shipped"
```

All three new `MarketplaceOrderDto` fields are `null` for every status prior to their
respective transition (e.g. `ShippedAt`/`EstimatedDeliveryDays` are `null` while status is
`new`/`confirmed`; `DeliveredAt` is `null` until status is `delivered`). Once set, they never
get cleared or overwritten — `expiry`/ETA math is meant to be computed client-side from
`ShippedAt + EstimatedDeliveryDays`, per the approved plan (not stored as a separate field).

## Validation behavior to build the UI against

- `POST /api/supplier-cabinet/orders/{id}/status` with `{ status: "shipped" }` and
  `estimatedDeliveryDays` missing, `null`, `0`, or negative → `400 Bad Request` with
  `{ error: "Вкажіть орієнтовну кількість днів до доставки." }`
  (`MarketplaceOrderService.EstimatedDeliveryDaysRequiredError`). No state change on the
  order in this case — still safe to retry the same call with a corrected value.
- `POST .../status` with `{ status: "shipped", estimatedDeliveryDays: 3 }` on an order
  currently `confirmed` → `200 OK`, response `MarketplaceOrderDto` has `status: "shipped"`,
  `shippedAt` set to now, `estimatedDeliveryDays: 3`.
- `POST .../status` with `{ status: "delivered" }` on an order currently `shipped` → `200
  OK`, `deliveredAt` set to now. No new required fields for this transition.
- Controller (`SupplierCabinetCooperationController.UpdateOrderStatus`) needed **zero**
  changes — the DTO binds straight from `[FromBody]`, so no endpoint/route/shape surprises
  versus what you already know from `IMarketplaceOrderService`.

## Side effect you don't need to build anything for, but should know about

Shipping an order now also enqueues a `notification_queue` row
(`EventType = "marketplace_order.shipped"`, `TenantId = order.ClientTenantId`,
`Channel = "system"`) that the worker's existing notification-dispatch job turns into a
real per-user notification for the client tenant. This is a background/backend concern —
no frontend action required — but it means the client may see a notification arrive
independently of them reloading the orders page, so don't be surprised if e2e/manual
testing shows a notification badge changing around the same time as a Ship action.

## What's NOT done (yours, per the plan at `C:\Users\stass\.claude\plans\abundant-popping-ladybug.md`)

Everything under "Frontend" in the plan: types in
`frontend/features/marketplace/types.ts` and `frontend/features/supplier-cabinet/types.ts`,
the new `EstimateDeliveryModal.tsx` (or similar) replacing the bare `transition(order,
"shipped")` call in `CabinetOrdersTab.tsx`, the client-side ETA/"in transit" display in
`app/(dashboard)/marketplace/orders/page.tsx` (including the compact badge-adjacent label
so it's visible without expanding the row — this is the part that most directly answers
the original "nowhere shows it's on the way" complaint), and i18n entries. Full detail is
in the plan document.

## Verification done on this side

`dotnet build` clean, `dotnet test` 1755/1755 passed (includes new coverage for this
change). See `.claude/logs/tasks/584_2026-08-20_marketplace-order-shipping-logic_backend-developer.md`
for detail.
