# TASK-354 — Backend: Block 4 pre-launch audit — Receipts/Transfers/WriteOffs

**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-353

Block 4 of the pre-launch audit (`eager-pondering-tower.md`). Reviewed the full
document workflows in `Features/Receipts`, `Features/Transfers`, `Features/WriteOffs`
(services, repositories, controllers, EF model).

## Found and fixed

**P0 — WriteOffs approval never touched stock for the only creation path that exists
in production.** `WriteOffService.ApproveAsync` had `if (item.ProductStockId is null)
continue;` — silently skipping deduction and movement logging. The mobile app's
"quick write-off" screen (`mobile/app/(app)/write-offs/create.tsx`, the only UI that
creates write-offs — web has list/approve/reject only, no create form) sends
`{ productId, quantity }` with no `productStockId`. So every write-off approved
through the real app set `status=approved` and computed `TotalLossAmount`, but never
decremented `product_stock` and never wrote a `stock_movements` row — inventory and
the audit trail silently diverged from reality. Also violated the FEFO rule (no batch
selection at all for this path).
Fix: no-batch items now FEFO-consume across the product's batches at the write-off's
store (`IWriteOffRepository.GetFefoOrderedAsync`, same query shape as
`StockRepository.GetFefoOrderedAsync`), nearest-expiry first, same as any other stock
consumption in the codebase.

**P1 — silent partial-deduct on insufficient stock (both branches).** The old code did
`Math.Min(item.Quantity, stock.Quantity)` and always succeeded, leaving
`WriteOffItem.Quantity`/`LossAmount` (computed from the *requested* quantity) permanently
inconsistent with the real amount removed. Fix: both the explicit-batch branch and the
new FEFO branch now hard-fail the whole `ApproveAsync` call with a clear error
("Insufficient quantity in batch …" / "Insufficient stock for product …") when requested
quantity exceeds what's actually available — no partial state, nothing is persisted
(the loop returns before `SaveChangesAsync`, so in-memory EF tracking changes are
discarded). This directly satisfies the audit's "неможливість списати більше ніж є в
наявності" requirement, which previously did not hold at all.

**Indexes:** `stock_receipts` and `stock_transfers` had FK-column indexes
(`DestinationStoreId`/`SupplierId`, `FromStoreId`/`ToStoreId` — EF auto-indexes from
convention) but **no index with `TenantId` at all**, unlike `WriteOff` which already had
`idx_write_offs_tenant_store_status`. Since every query on these RLS-protected tables is
implicitly filtered by `TenantId`, this was a real seq-scan gap. Added
`idx_stock_receipts_tenant_store_status` and (two, since `stock_transfers` filters via
`FromStoreId == x || ToStoreId == x`) `idx_stock_transfers_tenant_from_status` /
`idx_stock_transfers_tenant_to_status`. Migration
`20260714210933_AddStockReceiptsTransfersTenantIndexes` (additive), applied to dev DB
and verified present via `\di`.

## Reviewed, no changes needed

- **Receipts**: `QuantityOrdered > 0` validated at create; `ExpiryDate` required before
  `ReceiveAsync` creates the `product_stock` batch; batch created with `BatchNumber`/
  `ExpiryDate` from the document as specified. `stock_movements` row generated on receive.
  No past-date restriction on `ExpiryDate` — not a spec requirement, left as-is (backdated
  receipts are a legitimate real-world case, e.g. damaged-in-transit deliveries).
- **Transfers**: source deduction is explicit-batch (not FEFO — caller picks
  `ProductStockId`), already validates batch belongs to `FromStoreId` and
  `sourceStock.Quantity >= itemReq.Quantity` (clean 400 otherwise — already correct,
  already tested: `CreateAsync_InsufficientStock_ReturnsError`). `expiry_date`/
  `batch_number` copied as-is end to end (Block 3 confirmed at the service level; this
  block re-confirmed the full create→confirm workflow keeps quantities and batch
  identity consistent). `stock_movements` logged on both the outbound (create) and
  inbound (confirm) legs.
- **N+1**: none in any of the three modules' list endpoints — `ReceiptRepository`/
  `TransferRepository`/`WriteOffRepository` all eager-`.Include()` items+product+store
  in `GetAllAsync`/`GetPagedAsync`, single query.
- **FK indexes**: `ProductId`, `ProductStockId`, parent-id FKs (`ReceiptId`/`TransferId`/
  `WriteOffId`) all have EF convention auto-indexes — verified present in the model
  snapshot for all three item tables.
- **Duplication** (flagged only, not touched per scope — candidate for Block 15):
  Receipts/Transfers/WriteOffs are three near-identical hand-rolled "document + items"
  stacks (service/repository/controller each ~200 lines of the same
  GetAll/GetPaged/GetById/Add/Update/SaveChanges shape). No shared abstraction exists.
  Worth a shared base repository/service if a fourth document type is ever added.

## Not fixed (out of scope / low severity, noted for later)

`CreateTransferRequest.ToStoreId` / `CreateReceiptRequest.DestinationStoreId` /
`CreateWriteOffRequest.StoreId` are not looked up against `Locations` before use —
relies on the DB FK constraint + RLS to reject a bogus/cross-tenant id, which means a
bad id surfaces as an unhandled 500 (FK violation) instead of a clean 400. Same pattern
across all three modules, pre-existing, not a regression from this block.

## Tests added (WriteOffs, `ShelfGuard.Tests/WriteOffs/WriteOffServiceTests.cs`)

Replaced `ApproveAsync_ItemWithoutStockRef_NoStockDeduction` (asserted the buggy
"nothing happens" behavior) with:
- `ApproveAsync_ItemWithoutStockRef_FefoConsumesNearestExpiryBatchAndLogsMovement`
- `ApproveAsync_ItemWithoutStockRef_InsufficientStock_ReturnsErrorAndDoesNotSave`
- `ApproveAsync_ExplicitStockRef_InsufficientQuantity_ReturnsErrorWithoutMutating`

Receipts/Transfers already had adequate coverage for insufficient-quantity and
stock_movements generation (`CreateAsync_InsufficientStock_ReturnsError`,
`ReceiveAsync_ValidReceipt_CreatesStockAndMovements`,
`ConfirmAsync_Valid_CreatesDestinationStockAndMovements`) — no gap, nothing added there.

## Build/test status

`dotnet build`: 0 errors, 1 pre-existing unrelated warning (`MarketplaceServiceTests.cs`).
`dotnet test`: 817/817 green (was 815 after TASK-353; net +2 — one test rewritten into
three, minus the one removed).
Migration applied to dev DB (not prod — per plan, prod stays untouched this block).

## Needs a decision

None — no product/UX ambiguity encountered in this block. The FEFO-on-approve fix and
the insufficient-stock hard-fail are both direct implementations of stated CLAUDE.md
rules ("FEFO is sacred" / audit brief's explicit ask), not judgment calls.
