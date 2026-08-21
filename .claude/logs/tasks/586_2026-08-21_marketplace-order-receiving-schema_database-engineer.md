# TASK-586 (stage 2/4) — Marketplace order receiving: schema layer

**Status:** done · **Agent:** database-engineer · **Updated:** 2026-08-21

## What shipped

Per ADR-033 / handoff `.claude/logs/handoffs/586-to-database_project-architect.md`, verbatim spec,
no deviations:

- **New entities** `backend/ShelfGuard.Domain/Entities/MarketplaceOrderReceipt.cs` and
  `MarketplaceOrderReceiptItem.cs` — field lists match the handoff's tables 2/3 exactly
  (`ProductId` nullable, `QuantityOrdered`/`QuantityReceived` at `numeric(12,3)` precision,
  `Status` "draft"/"received" only, no "cancelled").
- **`MarketplaceOrder.DestinationStoreId`** (`Guid?`) added to
  `backend/ShelfGuard.Domain/Entities/MarketplaceOrder.cs` — nullable at the DB, FK →
  `locations.Id` RESTRICT, `IsRequired(false)` in EF config. No app-layer validation added
  (backend-developer's stage per the handoff).
- **EF config** in `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs`: two new entity
  blocks (`marketplace_order_receipts`, `marketplace_order_receipt_items`), matching column
  types/FKs/delete behaviors from the handoff table verbatim, plus the **unique index on
  `MarketplaceOrderId`**.
- **Migration** `backend/ShelfGuard.Infrastructure/Migrations/20260821151649_AddMarketplaceOrderReceiving.cs`
  — EF-generated `CreateTable`/`AddColumn` calls, hand-edited to append the exact RLS SQL from
  the handoff's section 4: `tenant_isolation` (client, `FOR ALL` + `WITH CHECK`),
  `supplier_read` (`FOR SELECT` only), `provider_bypass` (`IN ('provider','provider_admin')`),
  `worker_bypass` — on both new tables. `Down()` drops the policies before dropping the tables.
  This intentionally does **not** copy `marketplace_orders`' OR-based single-policy pattern —
  supplier gets read-only, not write, per ADR-033 Decision 3.

## Deviations from spec

None on the field/type/FK/RLS spec. One implementation-detail call the handoff left open (EF
column naming, not covered by the handoff's field list): `DestinationStoreId` on both
`MarketplaceOrder` and `MarketplaceOrderReceipt` is stored under its own literal column name
(`DestinationStoreId`), **not** remapped to `DestinationLocationId` the way `StockReceipt`/
`StockTransfer` do. That remapping (`HasColumnName("...LocationId")` while keeping the C# property
name `...StoreId`) is documented in `AppDbContext.cs` (~line 246) as debt specific to entities
that existed *before* the v4 Store→Location rename and needed their physical column preserved.
These are brand-new columns with no legacy column to preserve, and the newer post-rename
convention (`AddUserLocations`, TASK-392) uses matching property/column names — followed that
instead. Purely a physical-naming choice; C# property name, FK target, and behavior are unchanged
from the spec.

## Verification

- `dotnet build` — clean, 0 errors.
- Migration applied to local dev DB (`crmproductsystems-postgres-1`, port 5435). Confirmed via
  `\d` both new tables' columns/FKs/indexes match the spec exactly, and `marketplace_orders`
  gained the nullable `DestinationStoreId` column + FK + index.
- `RlsCrossTenantIntegrationTests` (all 6, including
  `AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass`) — pass. Ran
  against the fixture's default superuser test connection (`crm`/port 5435), **not** the
  `shelfguard_app_dev` app-role connection used for `dotnet ef` — that role lacks `CREATEROLE`/
  `SUPERUSER` and the audit-role fixture's `SET ROLE` fails against it (misleading "permission
  denied to set role" on first attempt, unrelated to the new migration — a connection-string
  mixup, not a product bug). Full suite: 1765/1765 pass.
- Pre-deploy informational query (handoff section "Before you consider the migration done", #2):
  **0 rows** against local dev. Not meaningful as a signal — `marketplace_orders` is empty in
  this dev DB (0 rows total, any status). The query itself works but needed adjusting for this
  schema's real column identifiers: the handoff's snippet uses lowercase snake_case
  (`order_number`, `client_tenant_id`) which doesn't resolve — every column here is
  quoted PascalCase, e.g.:
  ```sql
  SELECT "Id", "OrderNumber", "ClientTenantId"
  FROM marketplace_orders
  WHERE "Status" = 'shipped' AND "DestinationStoreId" IS NULL;
  ```
  Flagging this quoting gotcha for whoever runs the real pre-deploy check against prod.

## Next

backend-developer stage: `CreateOrderAsync` validation for `DestinationStoreId`, new
`MarketplaceOrderReceiptService`, controller actions per ADR-033 Decision 5, `AllowedTransitions`
change per Decision 4. See handoff `.claude/logs/handoffs/586-to-backend_database-engineer.md`.
