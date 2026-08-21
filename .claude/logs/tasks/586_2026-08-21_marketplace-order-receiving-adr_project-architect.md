# TASK-586 (stage 1/4) — Marketplace order receiving: architecture decision record — project-architect

**Status:** done · **Agent:** project-architect (main session) · **Depends:** approved plan
`C:\Users\stass\.claude\plans\abundant-popping-ladybug.md`

ADR-only stage. No code, migrations, or entities touched — `.claude/docs/decisions.md` is the
only file this session edited. database-engineer / backend-developer / frontend-developer
implement in separate follow-up sessions.

## What was decided (ADR-033)

1. **New `MarketplaceOrderReceipt`/`MarketplaceOrderReceiptItem` entities**, not `StockReceipt`
   reuse — confirms the plan, adds a third rejection reason beyond the plan's own two
   (`StockReceipt` is single-tenant RLS; the new tables need cross-tenant read for the supplier,
   which would widen an already-shipped table's blast radius). Exact field lists (types,
   nullability, FK targets) written out in the ADR — verbatim spec for database-engineer.
2. **`MarketplaceOrder.DestinationStoreId`**: nullable `Guid` at the DB column (historical orders
   can't be backfilled with a real answer), required by `CreateOrderAsync` validation for every
   *new* order going forward — same nullable-forever pattern already used by ADR-017's
   `SupplierItem.category`. This is the one place this ADR corrected the plan's field sketch,
   which didn't flag the NOT-NULL-vs-historical-data tension.
3. **RLS**: split one `tenant_isolation` (client, full CRUD) from a new `supplier_read`
   (`FOR SELECT` only, supplier tenant) policy — **not** a copy of `marketplace_orders`' existing
   two-tenant `OR`-based policy, which (no `FOR` clause) actually grants both tenants full
   read/write there. That's correct for orders (both sides legitimately write); wrong here (the
   supplier should never write receipt data). `provider_bypass`/`worker_bypass` included per the
   project's mandatory triad (enforced by `RlsCrossTenantIntegrationTests.
   AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass`).
4. **Status transition**: `AllowedTransitions[Shipped]` (currently `= [Delivered]`, its only
   entry) gets the key removed entirely, not emptied. `MarketplaceOrderReceiptService` (new) is
   the sole writer of `Status = Delivered`, going through the order's own status check directly
   (mirrors `ReceiptService.ReceiveAsync`), not through `AllowedTransitions`. Confirmed
   unambiguous for frontend-developer: the supplier's existing "Deliver" POST already 400s the
   moment this ships, whether or not the button is removed in the same deploy.
5. **API contract sketch** (5 endpoints: list awaiting-receipt, create-or-get draft, get, update
   one item by scan, finalize) — order-centric routing, extends
   `MarketplaceCooperationController.cs` rather than a new controller. One deliberate deviation
   from the `ReceiptsController` template: per-item `PUT`, not bulk — matches the scan-one-
   commit-one mobile UX, not a form-submit-all UX.
6. **Barcode resolution**: `GET /api/items/by-barcode/{code}` confirmed sufficient as-is — 404s
   cleanly, already JWT-gated the same way mobile POS/stock/write-offs already use it, tenant-
   scoped via existing RLS. Zero new backend work needed there.
7. **Consequences flagged**: pre-existing `Shipped` orders with `DestinationStoreId IS NULL`
   become permanently un-receivable once the supplier self-service path is removed — exact SQL
   check written into the ADR for database-engineer/backend-developer to run against prod before
   merging. Task-log evidence (TASK-359, TASK-584/585) suggests this is a small/zero blast
   radius (the ship/delay-reason lifecycle only landed 2026-08-20), but this session had no DB
   access to confirm the row count directly — flagged as a required pre-deploy check, not
   resolved here. Non-blocking discrepancy handling endorsed as-is (matches shipped
   `StockReceiptItem` precedent). No supplier "order received" notification in v1 — read-only
   display only, matches plan scope; documented as a future extension point, not built.

## Build/tests

N/A — no code changed this session.

## For next agent

`.claude/logs/handoffs/586-to-database_project-architect.md` — verbatim entity field lists + RLS
policy SQL for database-engineer to start immediately without re-reading the full ADR.
