# TASK-586 (stage 3/4) — Marketplace order receiving: DTO/service/API layer

**Agent:** backend-developer · **Status:** done
**Depends on:** TASK-586 stage 2 (database-engineer) — schema/RLS already in place
**Spec:** ADR-033 (`.claude/docs/decisions.md`), handoff `.claude/logs/handoffs/586-to-backend_database-engineer.md`

## What changed

**`MarketplaceOrderService.cs` / `IMarketplaceOrderService.cs`**
- `CreateOrderAsync`: new `request.DestinationStoreId is null` → 400 branch
  (`DestinationStoreRequiredError`), same shape as the existing `EmptyOrderError` check. Sets
  `order.DestinationStoreId` on creation.
- `AllowedTransitions`: the `[MarketplaceOrderStatus.Shipped]` key is removed entirely (not
  emptied) — a `status:"delivered"` update on a shipped order now falls through to the existing
  generic "transition not possible" error, no new error branch.
- New `ListAwaitingReceiptForClientAsync` — reuses the existing private `ToDtosAsync`/tenant-name
  mapping so `MarketplaceOrderReceiptService` doesn't duplicate it.
- `MarketplaceOrderDto`/`CreateMarketplaceOrderDto` (`CooperationDtos.cs`) gained
  `DestinationStoreId`.

**New: `MarketplaceOrderReceiptService.cs` / `IMarketplaceOrderReceiptService.cs`**
Separate service (not a method bag on `MarketplaceOrderService`, per ADR-033 Decision 4), mirrors
`ReceiptService`'s create-draft/update-item/finalize shape:
- `ListAwaitingReceiptAsync` — delegates to `IMarketplaceOrderService.ListAwaitingReceiptForClientAsync`.
- `GetOrCreateDraftAsync(clientTenantId, orderId, userId, ct)` — idempotent; pre-populates one
  `MarketplaceOrderReceiptItem` per order line. Validates: order belongs to caller, order is
  `Shipped`, `DestinationStoreId` is set. **Diverged from the brief's literal signature** by
  adding a `userId` param (brief listed only 2 params) — needed to populate
  `MarketplaceOrderReceipt.CreatedByUserId`, which the entity itself documents as "client-side
  user who started the draft"; `ReceiptService.CreateAsync` takes the analogous `createdBy`
  explicitly too.
- `GetAsync` / `UpdateItemAsync` / `ReceiveAsync` — all **order-centric** (take `orderId`, not a
  separately-surfaced `receiptId`), resolving the receipt internally via
  `GetByOrderIdAsync(orderId)`. This differs from the brief's literal parameter naming
  (`receiptId`) but matches ADR-033 Decision 5's explicit routing intent ("mobile never needs to
  learn or persist a second id") and avoids a redundant controller-side lookup.
- `UpdateItemAsync` field semantics mirror `ReceiptService.UpdateItemsAsync` exactly:
  `QuantityReceived`/`DiscrepancyNotes` overwrite directly (omit = clear), `ProductId`/
  `ExpiryDate`/`BatchNumber` merge with the existing value when omitted. `ProductId` is validated
  against `IItemRepository.GetByIdAsync` (RLS-scoped to the caller's tenant — defense in depth
  against a cross-tenant id, even though the only legitimate source is the tenant-scoped
  barcode-lookup endpoint).
- `ReceiveAsync` gate: every item needs `ProductId` + `QuantityReceived` + `ExpiryDate` (extends
  `ReceiptService.ReceiveAsync`'s expiry-only gate). Creates `ProductStock`/`StockMovement` per
  item, sets receipt `received`, sets `order.Status = Delivered` / `DeliveredAt` — the only
  remaining writer of that transition in the codebase. One shared `AppDbContext` behind
  `IMarketplaceOrderRepository`/`IMarketplaceOrderReceiptRepository` (both scoped, same request)
  means a single `SaveChangesAsync` call flushes both the order and the receipt/stock/movement
  changes atomically.
- **No supplier notification enqueued on finalize.** The task brief offered this as a judgment
  call ("recommended: yes... or skip if ADR-033 says otherwise") — ADR-033's own Consequences
  section explicitly states this is **not** part of the design ("the plan only asked for a
  read-only supplier-cabinet display... not a push/outbox notification... not built"), so it was
  skipped per the ADR's explicit instruction, not built speculatively.
- **Field-naming discrepancy resolved in favor of the ADR:** the task brief's prose said
  `ProductStock.SourceType = "marketplace_order"` / `SourceId = order.Id`, but ADR-033 Decision
  5's endpoint (e) table says `SourceType = "marketplace_order_receipt"` / `SourceId = receipt.Id`
  (and the matching `StockMovement.ReferenceType`/`ReferenceId`). Followed the ADR table — it's
  the document the task explicitly said to treat as authoritative for endpoint (e), and it
  matches `ReceiptService`'s own precedent of referencing the receipt row, not an order/supplier
  id. Flagging this divergence explicitly in case it needs reconciling upstream.
- `MovementType` stays `"receipt"` (not a new value) — same real-world event as a regular
  delivery; `ReferenceType`/`ReferenceId` already distinguish the marketplace origin.

**New: `IMarketplaceOrderReceiptRepository.cs` / `MarketplaceOrderReceiptRepository.cs`**
Dedicated repository (matches this codebase's established one-repo-per-feature convention, same
as `IReceiptRepository`/`IMarketplaceOrderRepository`) — not raw `AppDbContext` access from the
service.

**`IMarketplaceOrderRepository.cs` / `MarketplaceOrderRepository.cs`**
New `ListAwaitingReceiptForClientAsync` — `Status == Shipped AND ClientTenantId == X AND NOT
EXISTS (receipt for this order with Status != "draft")`. Since finalize always flips the order to
`Delivered`, `Status == Shipped` alone already implies no completed receipt exists; the NOT EXISTS
is the defensive, literal form of the spec (also covers an orphaned non-draft receipt row).

**Controller: `MarketplaceCooperationController.cs`**
New region, 5 endpoints (routes/verbs/DTOs match ADR-033 Decision 5's table verbatim):
```
GET  /api/marketplace/orders/awaiting-receipt
POST /api/marketplace/orders/{orderId}/receipt
GET  /api/marketplace/orders/{orderId}/receipt
PUT  /api/marketplace/orders/{orderId}/receipt/items/{itemId}
POST /api/marketplace/orders/{orderId}/receipt/finalize
```
Reads (a/c) — class-level `[Authorize]`+`marketplace` module only. Mutations (b/d/e) —
`[Authorize(Policy = AppPolicies.CanReceiveStock)]`, matching `ReceiptsController`'s own floor for
equivalent actions.

**`SupplierCabinetCooperationController.cs`** — `UpdateOrderStatus`'s XML doc updated to state
`delivered` is no longer reachable via this endpoint, and where it now lives.

**Tests:** new `MarketplaceOrderReceiptServiceTests.cs` (20 tests, NSubstitute — the actual
convention in this codebase; the brief said "Moq-based," but `MarketplaceOrderServiceTests.cs`
itself uses NSubstitute, so followed the real file). Covers: draft pre-population field-for-field,
idempotent get, order-not-shipped/no-destination-store validation, per-item update
merge/overwrite semantics + validation (foreign tenant, not-draft, unknown item, negative qty,
unknown product), finalize gate (rejects on any unresolved item), finalize success (right
`ProductStock`/`StockMovement` call counts and field values, order → Delivered). Also updated
`MarketplaceOrderServiceTests.cs`: `CreateMarketplaceOrderDto` call sites now pass
`DestinationStoreId`, new `CreateOrder_NoDestinationStoreId_ReturnsValidationError` test, the
`Shipped→Delivered` matrix case flipped to `false`, and `UpdateOrderStatus_Deliver_SetsDeliveredAt`
replaced with `UpdateOrderStatus_Deliver_NoLongerReachable_ReturnsError`.

**Docs:** `.claude/docs/api-contracts.md` marketplace section updated — the 5 new endpoints, the
supplier-cabinet status endpoint's note that `shipped→delivered` is gone, DTO shapes.

## Verification

- `dotnet build` — clean, 0 errors (1 pre-existing, unrelated warning in `MarketplaceServiceTests.cs`).
- `dotnet test` — **1785/1785 pass** (1765 existing + 20 new).
- Pre-deploy check run against **local dev DB** (docker `crmproductsystems-postgres-1`, via
  `psql -U crm -d crm`):
  ```sql
  SELECT "Id", "OrderNumber", "ClientTenantId"
  FROM marketplace_orders
  WHERE "Status" = 'shipped' AND "DestinationStoreId" IS NULL;
  ```
  **0 rows.** Uninformative, same as database-engineer's stage-2 finding — `marketplace_orders`
  is completely empty in local dev (`SELECT COUNT(*) FROM marketplace_orders` = 0).

  **⚠️ MUST be re-run against PRODUCTION before this change deploys**, per ADR-033's Consequences
  section: any order already sitting in `Status = 'shipped'` with `DestinationStoreId IS NULL`
  (true for every order shipped before this migration, since the column didn't exist) becomes
  **permanently un-receivable** through the new flow the moment this deploys — no self-service
  supplier fallback remains once `AllowedTransitions[Shipped]` is gone. If the prod query returns
  any rows, the fix is a cheap manual `UPDATE ... SET "DestinationStoreId" = <tenant's actual
  delivery store>` per affected row, done **before** shipping the `AllowedTransitions` change —
  not a schema fix, just data backfill for that one order. Orchestrator: please run the exact
  query above against prod and report back before any deploy of this stage.

## For frontend-developer / mobile handoff

See `.claude/logs/handoffs/586-to-frontend_backend-developer.md` — final DTO shapes, endpoint
contract, auth, error codes. Written to be self-contained enough to seed the eventual
mobile-facing handoff too (per the task brief).
