# Handoff: TASK-584 database-engineer → backend-developer

## What's ready

`MarketplaceOrder` entity (`backend/ShelfGuard.Domain/Entities/MarketplaceOrder.cs`) now
has 3 new nullable properties, added right after `TotalAmount`:

```csharp
public DateTimeOffset? ShippedAt { get; set; }
public int? EstimatedDeliveryDays { get; set; }
public DateTimeOffset? DeliveredAt { get; set; }
```

Migration `20260820131503_AddMarketplaceOrderShippingFields` applied to local dev DB.
Columns exist in `marketplace_orders`: `ShippedAt timestamptz NULL`,
`EstimatedDeliveryDays integer NULL`, `DeliveredAt timestamptz NULL`. No new indexes.
RLS unchanged — new columns inherit the table's existing `tenant_isolation` /
`provider_bypass` / `worker_bypass` policies automatically (verified).

## What's NOT done (your part, per the plan at
`C:\Users\stass\.claude\plans\abundant-popping-ladybug.md`)

1. `MarketplaceOrderService.UpdateOrderStatusAsync`
   (`backend/ShelfGuard.Application/Features/Marketplace/MarketplaceOrderService.cs`,
   ~lines 168-197) — add the `Shipped`/`Delivered` branches per the plan's design
   section (validate `EstimatedDeliveryDays > 0` required on transition to `Shipped`,
   set `ShippedAt`/`DeliveredAt` = `DateTimeOffset.UtcNow`, set
   `EstimatedDeliveryDays` from the request).
2. `UpdateMarketplaceOrderStatusDto` and `MarketplaceOrderDto`
   (`backend/ShelfGuard.Application/Features/Marketplace/Dtos/CooperationDtos.cs`) —
   add `EstimatedDeliveryDays` (request) and `ShippedAt`/`EstimatedDeliveryDays`/
   `DeliveredAt` (response); update `ToDto` mapping.
3. New constant error string `EstimatedDeliveryDaysRequiredError` (per plan: "Вкажіть
   орієнтовну кількість днів до доставки.").
4. Notification enqueue for `marketplace_order.shipped` via `ITenantSessionOverride` +
   `INotificationRepository`, targeting `order.ClientTenantId` — reuse the ADR-018
   pattern already in `SupplierAgreementService.cs:385-410` (same cross-tenant RLS
   guard that TASK-582 just fixed — don't regress it).
5. Verify `SupplierCabinetCooperationController` (`POST orders/{id}/status`) doesn't
   need changes — plan believes it binds straight to the DTO, but confirm.
6. Tests for `MarketplaceOrderService` covering the new validation/branches
   (`backend/ShelfGuard.Tests/`).

## Note on running `dotnet ef` / touching the dev DB

The design-time factory (`AppDbContextFactory.cs`) ignores
`appsettings.Development.json`; it only reads env var
`ConnectionStrings__DefaultConnection`. If you need to run further EF commands against
the local dev DB (docker `crmproductsystems-postgres-1`, port 5435), export:
```
ConnectionStrings__DefaultConnection="Host=localhost;Port=5435;Database=crm;Username=shelfguard_app_dev;Password=307823f594357b97c27a046f33bc5549ad09"
```
first (value copied from `appsettings.Development.json`).
