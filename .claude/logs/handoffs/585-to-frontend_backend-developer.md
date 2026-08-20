# Handoff: TASK-585 service/API → frontend-developer

## New endpoint

```
POST /api/supplier-cabinet/orders/{id}/delay-reason
  body: { "reason": "string" }
  -> 200 MarketplaceOrderDto
  -> 400 { "error": string }   -- empty/whitespace reason, or order.status != "shipped"
  -> 404 { "error": string }   -- order not found / belongs to a different supplier tenant
```

Auth: same `SupplierCabinet` policy + `marketplace_supplier` module as every other action in
`SupplierCabinetCooperationController` — supplier-side only. There is **no** client-side
endpoint to set this; clients only ever read it back on the order they already fetch via
`GET /api/marketplace/my-orders` (client) or `GET /api/supplier-cabinet/orders` (supplier) —
`DelayReason` rides along on the existing `MarketplaceOrderDto` for both.

## DTO shapes

`MarketplaceOrderDto` (`Features/Marketplace/Dtos/CooperationDtos.cs`) gained one new field,
placed right after `DeliveredAt`:

```ts
{
  // ...existing fields unchanged (id, orderNumber, agreementId, supplierTenantId,
  // clientTenantId, supplierName, clientName, status, comment, cancelReason,
  // totalAmount, createdAt, updatedAt, shippedAt, estimatedDeliveryDays, deliveredAt)
  delayReason: string | null,   // NEW — supplier's free-text explanation, null until set
  items: MarketplaceOrderItemDto[]
}
```

Request body:
```ts
{ reason: string }   // SetOrderDelayReasonDto — required, non-blank (trimmed server-side)
```

## Error strings (for message mapping, `MarketplaceOrderService` constants)

- `DelayReasonRequiredError` = `"Вкажіть причину затримки доставки."` — reason empty/whitespace → 400
- `OnlyShippedCanHaveDelayReasonError` = `"Причину затримки можна вказати лише для відправленого замовлення."` — order.status != shipped → 400
- `OrderNotFoundError` = `"Замовлення не знайдено."` → 404

## Suggested UX (not binding — your call)

- Supplier cabinet order detail/list: show a "record delay" action only when
  `status === "shipped"` and (optionally) `now > shippedAt + estimatedDeliveryDays` days —
  same overdue-window logic the user described; the backend doesn't gate on the date itself,
  only on status, so you decide whether to hide/disable the action client-side before that
  window passes or leave it always-available while shipped.
- Once `delayReason` is non-null, both supplier and client order views should surface it
  (e.g. a warning banner near the ETA) — client already sees `shippedAt`/`estimatedDeliveryDays`
  from TASK-584, `delayReason` sits right next to them on the same DTO.
- Client gets a proactive notification (`marketplace_order.delay_reason_added` outbox event,
  title `"Затримка доставки: {orderNumber}"`) the same way TASK-584's `marketplace_order.shipped`
  worked — no client-side polling needed to know something changed, but the DTO is still the
  source of truth for display.

## Verification done

`dotnet build` clean, `dotnet test` 1765/1765 passed (10 new tests for `SetDelayReasonAsync`).
No frontend files touched.
