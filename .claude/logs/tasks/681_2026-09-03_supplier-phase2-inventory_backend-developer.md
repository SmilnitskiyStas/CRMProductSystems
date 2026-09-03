# TASK-681 — Supplier-portal expansion Phase 2 (backend: supplier inventory + batch receiving)

**Status:** review (not pushed) · **Agent:** backend-developer · Plan: `.claude/plans/1-partitioned-book.md` Phase 2 (D2, D3, D8)

## What changed

### Migration `AddSupplierInventory` (`20260903063008_...`)
4 new supplier-owned tables — parallel to the retail Stock/Receipts model, **not a reuse** (D2:
`ProductStock.ProductId→items` is mandatory + `product_stock` carries a RESTRICTIVE `store_scope`
policy a supplier_admin with no `user_locations` reads as 0 rows).

- `supplier_stock` — FEFO batches keyed on (SupplierItemId, WarehouseId). `xmin` rowversion
  concurrency token (mirror of ProductStock TASK-356). Partial FEFO index
  `ix_supplier_stock_fefo (TenantId,WarehouseId,SupplierItemId,ExpiryDate) WHERE "Quantity" > 0`.
  FKs → `supplier_items` (RESTRICT), `locations` (RESTRICT).
- `supplier_stock_movements` — append-only ledger (`receipt`/`ship`/`adjust`/`write_off`).
  FKs → `supplier_stock`, `supplier_items`, `locations`×2 (From/To), all RESTRICT.
  Indexes `(TenantId,SupplierStockId)`, `(TenantId,CreatedAt)`.
- `supplier_stock_receipts` — manual "what actually arrived" intake (`draft`/`received`/`cancelled`).
  FK → `locations` RESTRICT. Index `(TenantId,WarehouseId,Status)`.
- `supplier_stock_receipt_items` — N rows per (SupplierItemId, ExpiryDate, BatchNumber); `TenantId`
  denormalized (RLS = plain tenant_isolation, no join). `ExpiryDate` nullable (required at finalize).
  FK → `supplier_stock_receipts` CASCADE, `supplier_items` RESTRICT. Indexes `(ReceiptId)`, `(TenantId)`.

**RLS (D8)** — hand-added in `Up()`/`Down()` (EF doesn't emit it), one loop over all 4 tables:
`tenant_isolation` (NULLIF-guarded, fail-closed, `WITH CHECK` mirroring `USING`) + `provider_bypass`
IN ('provider','provider_admin') + `worker_bypass`, all under `FORCE ROW LEVEL SECURITY`.
**No `store_scope` policy — deliberate, documented in the migration class summary** (supplier
tenants have no `user_locations` model).

`AppDbContext` — 4 `DbSet<>` + 4 config blocks (after SupplierItemImage). Snapshot regenerated
(clean 4-entity diff).

**Applied to dev/test DB** `localhost:5435/crm` (docker `crmproductsystems-postgres-1`): generated
idempotent SQL (`dotnet ef migrations script 20260902203915_... 20260903063008_... --idempotent`),
piped through `psql -v ON_ERROR_STOP=1`. Verified `\d supplier_stock` (xmin present, FEFO index,
FKs), `pg_policies` (12 policies / 4 tables), `relforcerowsecurity=t` on all 4,
`__EFMigrationsHistory` row present. **Not applied to prod.**

`dotnet ef` note: the design-time factory (`AppDbContextFactory`) defaults to
`localhost:5432/shelfguard_dev` (wrong DB, auth fails) — pass
`ConnectionStrings__DefaultConnection=Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password`
as an env var for any `dotnet ef` invocation. `--startup-project ShelfGuard.Infrastructure` (not Api).

### Domain — `Domain/Entities/` (4 new)
`SupplierStock` (mutable Quantity/Status/LastCheckedAt, `init` rest), `SupplierStockMovement`
(all `init`), `SupplierStockReceipt` (`ICollection<SupplierStockReceiptItem> Items`, WarehouseId
settable for draft header edit), `SupplierStockReceiptItem`.

### Application — `Application/Features/SupplierInventory/`
- `ISupplierStockRepository` / `SupplierStockRepository` (Infrastructure) — `GetPagedAsync`
  (FEFO-ordered), `GetFefoOrderedAsync(tenantId, supplierItemId, warehouseId)` — mirror of
  `StockRepository.GetFefoOrderedAsync` (`Quantity>0`, `Status not in ('sold_out','archived')`,
  `OrderBy(ExpiryDate)`), `GetByIdAsync`, `WarehouseExistsAsync` (active + `Type="warehouse"`),
  `SupplierItemExistsAsync`, `AddAsync`/`AddMovementAsync`/`Update`/`SaveChangesAsync`.
  `SaveChangesAsync` translates `DbUpdateConcurrencyException → ConcurrencyConflictException`
  (same as `LoyaltyRepository`/`PosRepository`).
- `SupplierStockService`:
  - `GetStockAsync` → `PagedResult<SupplierStockDto>`.
  - `AddBatchAsync` — validates qty>0, expiry in future, warehouse + supplierItem exist → 1
    `SupplierStock` (`SourceType="manual"`) + 1 `SupplierStockMovement("receipt")`.
  - `AdjustAsync` — xmin-guarded update + `SupplierStockMovement("adjust")`; catches
    `ConcurrencyConflictException` → clean retry error string.
  - `FefoConsumeAsync` — **duplicate of `StockService.FefoConsumeAsync`**: walks batches
    nearest-expiry-first, decrements, 1 `SupplierStockMovement("ship")` per touched batch.
    Returns `SupplierFefoConsumeResult { QuantityConsumed, Shortfall, BatchesConsumed[] }` —
    **a non-zero `Shortfall` is returned, not thrown** (Phase 3 shipping allows shortfall + warning,
    user decision 2026-09-02).
- `ISupplierStockReceiptRepository` / `SupplierStockReceiptRepository` + `SupplierStockReceiptService`:
  `CreateDraftAsync`, `GetAsync`, `ListAsync(tenantId, warehouseId?, status?)`, `UpdateAsync`
  (reference/notes/warehouse while draft), `AddLineAsync`, `RemoveLineAsync`, `ReceiveAsync` —
  gate: status==draft AND every line has `ExpiryDate != null` AND `Quantity > 0`; then per line
  1 `SupplierStock` (`SourceType="supplier_receipt"`, `SourceId=receiptId`) + 1
  `SupplierStockMovement("receipt", ReferenceType="supplier_stock_receipt")`; set
  `Status="received"`, `ReceivedBy`, `ReceivedAt`. Mirrors `ReceiptService.ReceiveAsync`.
  (Injects `ISupplierStockRepository` only for the warehouse/supplierItem existence checks —
  same scoped `AppDbContext`.)
- Reuses `Features/Stock/StockStatus.cs` (pure static helper) for batch `Status`.
- DTOs in `Dtos/SupplierStockDtos.cs`.
- DI: services in `ShelfGuard.Application/DependencyInjection.cs` (next to Phase 1's
  `ISupplierWarehouseService`), repos in `ShelfGuard.Infrastructure/DependencyInjection.cs`.

### API — `SupplierCabinetInventoryController` (new, separate from the warehouses controller)
`[Route("api/supplier-cabinet")]`, `[Authorize(Policy = AppPolicies.SupplierCabinet)]`,
`[RequireModule("supplier_inventory")]`; every action gated
`SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.WarehouseManagement)`.
`ResolveTenantId()` from the JWT `tenant_id` claim (pattern from `SupplierCabinetController`).

| Method | Route | Body |
|---|---|---|
| GET | `warehouses/{warehouseId}/stock?supplierItemId=&page=&pageSize=` | — |
| POST | `warehouses/{warehouseId}/stock` | `{ supplierItemId, expiryDate, quantity, batchNumber? }` |
| POST | `stock/{batchId}/adjust` | `{ quantity, reason? }` |
| GET | `warehouses/{warehouseId}/receipts?status=` | — |
| POST | `warehouses/{warehouseId}/receipts` | `{ reference?, notes? }` |
| GET | `receipts/{id}` | — |
| PUT | `receipts/{id}` | `{ warehouseId, reference?, notes? }` |
| POST | `receipts/{id}/lines` | `{ supplierItemId, expiryDate?, quantity, batchNumber?, unitCost?, notes? }` |
| DELETE | `receipts/{id}/lines/{lineId}` | — |
| POST | `receipts/{id}/finalize` | — |

## Build / tests

- `dotnet build -c Release` (whole solution) — **clean, 0 err** (1 pre-existing warning in
  `MarketplaceServiceTests.cs:1006`, not mine).
- New tests:
  - `SupplierStockServiceTests` ×13 — AddBatch (zero qty / past expiry / unknown warehouse /
    valid+movement), FEFO-consume (nearest-first ordering, partial→shortfall no-throw, no-stock),
    Adjust (valid+movement, `ConcurrencyConflictException`→clean error, negative, not-found).
  - `SupplierStockReceiptServiceTests` ×8 — draft→2 lines same product different expiry→finalize
    →2 FEFO batches + 2 receipt movements; finalize gate rejects a line missing expiry (stays
    draft, no writes); empty / already-received rejects; addLine unknown item / non-draft rejects;
    createDraft unknown warehouse rejects.
  - `SupplierStockRlsIntegrationTests` ×3 (real Postgres, `[Collection("TENANT_ISOLATION_TESTS")]`,
    EF-seed + `rls_audit_test_role` session) — supplier tenant A cannot SELECT tenant B's
    `supplier_stock` / `supplier_stock_receipts` (forged filter → 0), fully-RESET session → 0 rows
    on all 4 tables.
- `RlsCrossTenantIntegrationTests.AllForceRlsTables_...` — **green**, now covers the 4 new tables.
- Required filtered run `~SupplierStock|~SupplierInventory|~Rls` (Release) — **90 passed, 0 failed**.
- Regression `~Stock|~Receipt|~Location|~Marketplace` (Release) — **540 passed, 0 failed**.

## Deviations / notes

- Repo interfaces placed in `Application/Features/SupplierInventory/` (per brief + plan) — precedent
  exists (`Application.Features.ConsumerContent.IConsumerContentRepository`).
- Added `WarehouseExistsAsync` / `SupplierItemExistsAsync` to `ISupplierStockRepository` (not in the
  brief's method list) for clean boundary errors instead of raw FK `DbUpdateException`.
- Controller: chose a **separate `SupplierCabinetInventoryController`** over a region on the
  warehouses controller — keeps each controller single-purpose; both share the `api/supplier-cabinet`
  prefix with distinct templates.
- `AddBatchAsync` rejects past-or-today expiry (mirrors `StockService.CreateAsync`).
- Not committed. Not applied to prod.

## For the Phase 2 FRONTEND agent

**Module/permission gate:** `supplier_inventory` module + `warehouse_management` permission
(both already exist from Phase 1). Nav item `/supplier/inventory` (`moduleKey: "supplier_inventory"`,
`permission: "warehouse_management"`).

**Endpoints** (all under `/api/supplier-cabinet`): see the table above.

**DTO field names:**
- `SupplierStockDto { id, supplierItemId, supplierItemName, warehouseId, warehouseName, expiryDate,
  daysLeft, quantity, quantityInitial, batchNumber?, status, sourceType?, addedAt, lastCheckedAt }`.
  `status` values from `StockStatus`: `safe` / `warning` / `critical` / `expired` / `sold_out` /
  `needs_verification` — reuse the retail Stock status chips.
- `GET .../stock` returns `PagedResult<SupplierStockDto> { items, totalCount, page, pageSize, totalPages }`.
- `SupplierStockReceiptDto { id, warehouseId, warehouseName, status, reference?, notes?, receivedAt?,
  createdAt, items: SupplierStockReceiptItemDto[] }` (items sorted by expiry asc).
- `SupplierStockReceiptItemDto { id, supplierItemId, supplierItemName, expiryDate?, quantity,
  batchNumber?, unitCost?, notes? }`.
- `GET .../receipts` returns a bare `SupplierStockReceiptDto[]` (not paged).

**Requests:**
- `AddSupplierBatchRequest { supplierItemId, expiryDate (date), quantity, batchNumber? }`
- `AdjustSupplierStockRequest { quantity, reason? }`
- `CreateSupplierReceiptRequest { reference?, notes? }`
- `UpdateSupplierReceiptRequest { warehouseId, reference?, notes? }`
- `AddSupplierReceiptLineRequest { supplierItemId, expiryDate? (date), quantity, batchNumber?, unitCost?, notes? }`

**Receipt form (D3):** key rows by a synthetic client-side `rowId`, NOT `supplierItemId` — the
"+ add batch" action on a product adds another line with the same `supplierItemId` (one line per
expiry/batch). **Do NOT copy the `isRowAdded` guard from `CreateReceiptForm.tsx`.** `expiryDate` is
optional on a draft line but required to finalize — surface the per-line gate error from
`POST .../finalize` (400 `{ error }`).

**i18n keys needed** (uk + en): supplier-inventory nav label, warehouse stock table
(column headers: товар / склад / термін / залишок / початково / партія / статус / джерело),
status chip labels (reuse existing `stock.status.*` if present), receipt list + form
(статус draft/received/cancelled, «Додати партію», «Завершити прийом», reference / notes /
одиночна вартість fields), finalize-gate error toast, adjust-batch modal (нова кількість /
причина), and the "Партію щойно змінила інша операція…" concurrency retry message.

**Error strings the backend returns** (Ukrainian, surface as-is): `"Склад не знайдено."`,
`"Товар не знайдено в каталозі постачальника."`, `"Кількість має бути більшою за 0."`,
`"Термін придатності має бути в майбутньому."`, `"Партію не знайдено."`, `"Прийом не знайдено."`,
`"Змінювати можна лише чернетку прийому."`, finalize gate `"N позиц. без терміну придатності…"`.

**Pending debt:** `backend/openapi.json` regen (batched with TASK-670..674).
