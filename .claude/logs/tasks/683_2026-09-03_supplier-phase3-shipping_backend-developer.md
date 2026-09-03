# TASK-683 — Supplier Phase 3: batch-consuming shipment + buyer handoff (D4)

**Agent:** backend-developer · **Plan:** `1-partitioned-book.md` Phase 3 (D4) · **Status:** review, not committed

## Migration `20260903071530_AddMarketplaceOrderItemBatches`

New `marketplace_order_item_batches` (`Id`, `OrderItemId` → `marketplace_order_items` CASCADE,
`OrderId`, `SupplierTenantId`, `ClientTenantId`, `SupplierStockId` → `supplier_stock` SET NULL,
`ExpiryDate date`, `BatchNumber varchar(100)`, `Qty numeric(12,3)`, `CreatedAt`) + indexes on
`OrderId`/`OrderItemId`/`SupplierStockId`. Plus `marketplace_orders.SourceWarehouseId` (FK →
`locations` RESTRICT) and `marketplace_order_receipt_items.SourceOrderItemBatchId` (FK SET NULL).

**Split RLS, inverted vs ADR-033** (hand-written SQL, FORCE RLS): `tenant_isolation` FOR ALL +
WITH CHECK on `SupplierTenantId`; `client_read` FOR SELECT on `ClientTenantId`;
`provider_bypass` IN ('provider','provider_admin'); `worker_bypass`. NULLIF-guarded, fail-closed.

Applied to dev/test DB `:5435/crm` via idempotent script; verified in `pg_policies` (4 policies,
correct cmd/qual/with_check) and `pg_class` (`relrowsecurity` + `relforcerowsecurity` = t).
Snapshot regenerated. **Not applied to prod.** Note: like Phase 2's tables, the new table is owned
by `crm`, not `shelfguard_app_dev` — prod deploy must ensure the app role has grants (same
pre-existing condition as `supplier_stock*`, not introduced here).

## Backend

- `Domain/Entities/MarketplaceOrderItemBatch.cs` (new); `MarketplaceOrderItem.Batches` nav;
  `MarketplaceOrder.SourceWarehouseId`; `MarketplaceOrderReceiptItem.SourceOrderItemBatchId`.
- `AppDbContext` — entity config + `DbSet`; `MarketplaceOrderRepository` includes
  `Items.ThenInclude(Batches)` on all four reads + `AddOrderItemBatchAsync`;
  `MarketplaceOrderReceiptRepository.GetOrderItemBatchesAsync` (client-session read).
- `MarketplaceOrderService` += `ITenantRepository` + `ISupplierStockRepository`.
  New `ShipOrderAsync(supplierTenantId, orderId, ShipOrderRequest, performedByUserId)` and
  `GetShipSuggestionAsync(supplierTenantId, orderId, warehouseId?)`.
- **One ship code path**: `UpdateOrderStatusAsync`'s `confirmed→shipped` branch now delegates to
  `ShipOrderAsync` with an empty request (no warehouse → nothing consumed), so the legacy
  `POST /orders/{id}/status {status:"shipped"}` keeps its exact behaviour.
- `MarketplaceOrderReceiptService.GetOrCreateDraftAsync` — N receipt items per line when the line
  has batches (prefilled `QuantityOrdered`/`ExpiryDate`/`BatchNumber`/`SourceOrderItemBatchId`),
  fallback to 1 item/line otherwise. `ReceiveAsync` untouched.
- `SupplierCabinetCooperationController` += `POST orders/{id}/ship`,
  `GET orders/{id}/ship-suggestion?warehouseId=`, both `[RequireModule("supplier_inventory")]`
  per-action + `warehouse_management` permission.
- DTOs: `MarketplaceOrderItemBatchDto`, `ShipAllocationDto`, `ShipLineDto`, `ShipOrderRequest`,
  `ShipOrderResultDto`, `ShipSuggestion{,Line,Allocation}Dto`; `MarketplaceOrderDto` +=
  `SourceWarehouseId`/`ExpectedDeliveryDate`, `MarketplaceOrderItemDto` += `Batches`,
  `MarketplaceOrderReceiptItemDto` += `SourceOrderItemBatchId`.

## Two deliberate deviations from the brief

1. **`SupplierStockService.FefoConsumeAsync` is NOT called from the ship path.** It commits per
   call, so a failure on line 3 would leave lines 1–2's stock consumed for an order that never
   shipped, and a retry would double-consume. The same FEFO walk is done inline over
   `ISupplierStockRepository` primitives with **no** intermediate save, so stock decrements +
   `ship` movements + batch rows + the order's status change commit in ONE transaction under the
   supplier session. The outbox row stays a separate best-effort commit under the client override
   (it must — `notification_queue` is single-tenant RLS). This ordering is also load-bearing for
   RLS: batch inserts flushed inside the client override would fail their WITH CHECK with 42501.
2. **Shortfall vs malformed request are separated.** Under-coverage → ships + warning (user
   decision). An unknown order line, or a batch from another warehouse/item → hard 400, because
   that shape of mistake would otherwise ship goods with no batch record at all.

## Verification

- `dotnet build -c Release` — 0 errors (re-run immediately before finishing).
- Full suite `dotnet test -c Release`: **2257 passed, 0 failed.**
- Requested filter (`MarketplaceOrder|SupplierStock|Rls|Receipt`): 238 passed.
- New `MarketplaceOrderItemBatchRlsIntegrationTests` (real Postgres, `rls_audit_test_role`
  NOSUPERUSER NOBYPASSRLS), 5 tests: supplier inserts+reads its own; client SELECTs but gets
  **42501** on INSERT and **0 rows** on UPDATE/DELETE; a client row naming itself as supplier is
  invisible to the real supplier (documented, inert); third supplier tenant + RESET session see
  0 rows; policy-shape assertions (FOR ALL+WITH CHECK on SupplierTenantId, FOR SELECT with no
  WITH CHECK on ClientTenantId, NULLIF, no IS-NULL-OR, FORCE RLS).
- `AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass` green with
  the new table.
- Unit tests: 12 new `ShipOrderAsync`/`GetShipSuggestionAsync` cases (module off; allocations with
  module off rejected; not-confirmed; ETA validation + date↔days derivation; full FEFO coverage;
  shortfall ships with warning; explicit allocations beat FEFO; foreign-warehouse batch rejected;
  unknown warehouse; foreign tenant; legacy status endpoint routes through and is unchanged) and
  4 new receipt cases (3 batches → 3 prefilled items; 0 batches → 1 item/line; mixed lines;
  2 sublines → 2 `ProductStock` + order delivered).

## Follow-ups / debt

- `backend/openapi.json` regen — still deferred (TASK-670..674 + Phase 1/2/3).
- Prod: `dotnet ef database update` for Phase 1+2+3 migrations, plus app-role grants on the
  `crm`-owned supplier tables.
- With the module ON, a supplier that ships via the legacy `/status` endpoint ships without
  consuming stock. Frontend must route to `/ship` when the module is on. Documented in the
  ADR-033 amendment.
- Docs updated: `.claude/docs/api-contracts.md` (new section + receipt DTO), ADR-033 amendment in
  `.claude/docs/decisions.md`. Mobile handoff:
  `.claude/logs/handoffs/phase3-mobile-receipt-batches.md`.
