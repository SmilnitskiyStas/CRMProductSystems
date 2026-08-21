# Handoff: TASK-586 → database-engineer

**From:** project-architect
**Full brief:** `.claude/logs/tasks/586_2026-08-21_marketplace-order-receiving-adr_project-architect.md`
**ADR:** `.claude/docs/decisions.md` ADR-033 (read the full ADR before starting — this doc is the
verbatim spec extract, not a substitute for its rationale)

## What this is

New EF migration for two tables (`marketplace_order_receipts`, `marketplace_order_receipt_items`)
+ one new nullable column on the existing `marketplace_orders` table
(`destination_store_id`). Client-side confirmation of B2B marketplace order receipt — replaces
the supplier's one-click "Deliver" button.

## 1. `MarketplaceOrder.DestinationStoreId` (new column on existing table)

`Guid?` (nullable), FK → `locations.Id`, `ON DELETE RESTRICT`. **Nullable at the DB — do not make
NOT NULL.** Historical orders placed before this migration have no possible backfill value (ADR-033
Decision 2). Application-layer validation in `CreateOrderAsync` enforces it's set for every new
order going forward — that's backend-developer's job, not a DB constraint.

## 2. `MarketplaceOrderReceipt` → table `marketplace_order_receipts`

| Column | Type | Nullable | FK / Constraint |
|---|---|---|---|
| `Id` | `uuid` | no (PK) | `gen_random_uuid()` default |
| `MarketplaceOrderId` | `uuid` | no | FK → `marketplace_orders.Id`, `RESTRICT`. **UNIQUE index** (one receipt per order, v1 scope limit) |
| `ClientTenantId` | `uuid` | no | denormalized copy of the order's `ClientTenantId` |
| `SupplierTenantId` | `uuid` | no | denormalized copy of the order's `SupplierTenantId` — needed for the `supplier_read` RLS policy below |
| `DestinationStoreId` | `uuid` | no | FK → `locations.Id`, `RESTRICT`. Copied from the order at draft-creation time |
| `Status` | `character varying(20)` | no | default `'draft'`; values: `draft`, `received` — **no `cancelled`** (see ADR, "Rejected alternatives") |
| `CreatedByUserId` | `uuid` | yes | FK → `users.Id`, `SET NULL` |
| `ReceivedByUserId` | `uuid` | yes | FK → `users.Id`, `SET NULL` |
| `ReceivedAt` | `timestamp with time zone` | yes | |
| `CreatedAt` | `timestamp with time zone` | no | `NOW()` default |
| `UpdatedAt` | `timestamp with time zone` | no | `NOW()` default |

## 3. `MarketplaceOrderReceiptItem` → table `marketplace_order_receipt_items`

| Column | Type | Nullable | FK / Constraint |
|---|---|---|---|
| `Id` | `uuid` | no (PK) | `gen_random_uuid()` default |
| `ReceiptId` | `uuid` | no | FK → `marketplace_order_receipts.Id`, `CASCADE` |
| `MarketplaceOrderItemId` | `uuid` | no | FK → `marketplace_order_items.Id`, `RESTRICT` |
| `ClientTenantId` | `uuid` | no | denormalized |
| `SupplierTenantId` | `uuid` | no | denormalized |
| `ProductId` | `uuid` | **yes** | FK → `items.Id`, `SET NULL` — nullable because it's resolved at barcode-scan time, unlike `stock_receipt_items.ProductId` which is required |
| `ItemNameSnapshot` | `character varying(500)` | no | copy of `MarketplaceOrderItem.ItemName` at draft-creation time |
| `QuantityOrdered` | `numeric(12,3)` | no | matches `marketplace_order_items.Qty`'s precision, **not** `stock_receipt_items`' `numeric(10,2)` — must reconcile against the order line without rounding drift |
| `QuantityReceived` | `numeric(12,3)` | yes | same precision reasoning |
| `ExpiryDate` | `date` | yes | matches `stock_receipt_items.ExpiryDate` |
| `BatchNumber` | `character varying(100)` | yes | matches `stock_receipt_items.BatchNumber` |
| `DiscrepancyNotes` | `text` | yes | matches `stock_receipt_items.DiscrepancyNotes` |

## 4. RLS — exact SQL (both tables), from ADR-033 Decision 3

```sql
-- marketplace_order_receipts
ALTER TABLE marketplace_order_receipts ENABLE ROW LEVEL SECURITY;
ALTER TABLE marketplace_order_receipts FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON marketplace_order_receipts
  USING ("ClientTenantId" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
  WITH CHECK ("ClientTenantId" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);

CREATE POLICY supplier_read ON marketplace_order_receipts
  FOR SELECT
  USING ("SupplierTenantId" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);

CREATE POLICY provider_bypass ON marketplace_order_receipts
  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));

CREATE POLICY worker_bypass ON marketplace_order_receipts
  USING (current_setting('app.role', true) = 'worker');

-- marketplace_order_receipt_items — identical shape, same column names
ALTER TABLE marketplace_order_receipt_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE marketplace_order_receipt_items FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON marketplace_order_receipt_items
  USING ("ClientTenantId" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
  WITH CHECK ("ClientTenantId" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);

CREATE POLICY supplier_read ON marketplace_order_receipt_items
  FOR SELECT
  USING ("SupplierTenantId" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);

CREATE POLICY provider_bypass ON marketplace_order_receipt_items
  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));

CREATE POLICY worker_bypass ON marketplace_order_receipt_items
  USING (current_setting('app.role', true) = 'worker');
```

**Important — this is genuinely different from every existing two-tenant table in this feature
area** (`marketplace_orders`/`marketplace_order_items` use one `OR`-based `tenant_isolation`
policy with no `FOR` clause, which grants both tenants full read/write). Do not copy that pattern
here — the supplier must never get write access to receipt data. See ADR-033 Decision 3 for the
full "why."

## Before you consider the migration done

1. Run `RlsCrossTenantIntegrationTests.
   AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass` locally
   against the new migration — it audits `pg_policies` directly by exact policy name
   (`tenant_isolation` + NULLIF, `provider_bypass`, `worker_bypass`) and will fail if any is
   missing or misnamed.
2. **Pre-deploy check, not part of the migration itself:** run this against prod before the
   backend-developer stage removes the supplier's self-service `Shipped → Delivered` transition:
   ```sql
   SELECT id, order_number, client_tenant_id
   FROM marketplace_orders
   WHERE status = 'shipped' AND destination_store_id IS NULL;
   ```
   Any rows returned are orders that will become unreceivable through the new flow — see ADR-033
   Consequences for what to do if this returns non-zero rows (manual per-tenant `UPDATE`, not a
   generic backfill migration).
