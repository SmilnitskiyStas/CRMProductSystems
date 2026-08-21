# Handoff: TASK-586 → backend-developer

**From:** database-engineer
**Full task log:** `.claude/logs/tasks/586_2026-08-21_marketplace-order-receiving-schema_database-engineer.md`
**Spec:** ADR-033 in `.claude/docs/decisions.md`; original database-engineer brief at
`.claude/logs/handoffs/586-to-database_project-architect.md`

## What exists now (schema layer, done)

Entities (`backend/ShelfGuard.Domain/Entities/`):
- `MarketplaceOrderReceipt` — `Id, MarketplaceOrderId, ClientTenantId, SupplierTenantId,
  DestinationStoreId, Status ("draft"|"received", default "draft"), CreatedByUserId?,
  ReceivedByUserId?, ReceivedAt?, CreatedAt, UpdatedAt`. Nav: `Order`, `DestinationStore`, `Items`.
- `MarketplaceOrderReceiptItem` — `Id, ReceiptId, MarketplaceOrderItemId, ClientTenantId,
  SupplierTenantId, ProductId? (nullable — resolved at scan time), ItemNameSnapshot,
  QuantityOrdered (numeric(12,3)), QuantityReceived? (numeric(12,3)), ExpiryDate? (DateOnly),
  BatchNumber?, DiscrepancyNotes?`. Nav: `Receipt`, `OrderItem`, `Product`.
- `MarketplaceOrder.DestinationStoreId` (`Guid?`) — new nullable column, FK → `locations.Id`
  RESTRICT. **No app-layer validation exists yet** — that's your job: `CreateOrderAsync` needs a
  `request.DestinationStoreId is null` → 400 branch (ADR-033 Decision 2, same shape as the
  existing `EmptyOrderError` check).

Tables: `marketplace_order_receipts`, `marketplace_order_receipt_items`. Migration
`20260821151649_AddMarketplaceOrderReceiving` — applied to local dev DB, RLS verified by
`RlsCrossTenantIntegrationTests`, full suite green (1765/1765).

`DbSet<MarketplaceOrderReceipt>` / `DbSet<MarketplaceOrderReceiptItem>` registered on
`AppDbContext` — no repository interfaces created (out of scope for this stage; not requested by
the schema-stage brief). You'll need to decide whether this feature gets a dedicated
`IMarketplaceOrderReceiptRepository` or goes through `AppDbContext` directly — check how
`MarketplaceOrderService`/`MarketplaceCooperationController` currently access `MarketplaceOrder`
for the existing convention to follow.

## RLS — what your service code must assume

- Client tenant session (`app.tenant_id` = `ClientTenantId`): full read/write on both tables via
  `tenant_isolation`, `WITH CHECK` enforced — inserts/updates must set `ClientTenantId` correctly
  or they'll be rejected, not silently misfiltered.
- Supplier tenant session: **read-only** via `supplier_read` (`FOR SELECT`). Any write attempt
  from a supplier-tenant session will be silently filtered/rejected by RLS — don't build a
  supplier-side write path for this data; ADR-033 explicitly rules it out.
- No `ITenantSessionOverride` needed for the client's own finalize path (client session already
  has native write access) — see ADR-033 Decision 4's closing note. You'd only need the override
  pattern for a *supplier*-tenant-targeted write (e.g. a future "notify supplier" queue row),
  which is out of current scope.

## Known gotcha for your own pre-deploy check before removing the supplier Deliver button

Column identifiers on `marketplace_orders` are quoted PascalCase, not snake_case — the query in
ADR-033/the database handoff needs adjusting before it'll actually run:

```sql
SELECT "Id", "OrderNumber", "ClientTenantId"
FROM marketplace_orders
WHERE "Status" = 'shipped' AND "DestinationStoreId" IS NULL;
```

Ran clean against local dev but returned 0 rows because `marketplace_orders` is empty there —
not a real signal. Run this for real against prod (per ADR-033 Consequences) before shipping the
`AllowedTransitions` change that removes the supplier's `Shipped → Delivered` transition.

## Not done (yours to build, per ADR-033 Decision 4/5)

- `MarketplaceOrderReceiptService` (new — not a method on `MarketplaceOrderService`).
- `MarketplaceCooperationController` new region: the 5 endpoints in Decision 5's table.
- `AllowedTransitions` — drop the `Shipped` key entirely.
- DTOs: `MarketplaceOrderReceiptDto`, `UpdateMarketplaceOrderReceiptItemRequest`.
- `CreateOrderAsync` validation branch for `DestinationStoreId`.
