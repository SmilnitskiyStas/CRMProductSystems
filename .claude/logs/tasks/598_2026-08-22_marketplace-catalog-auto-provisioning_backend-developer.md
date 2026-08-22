# TASK-598 — Marketplace catalog auto-provisioning at order time (Wave 2, backend)

**Agent:** backend-developer · Wave 2 of 2 on TASK-596's schema (database-engineer).
Parallel: TASK-597 (frontend-developer, checkout conflict-resolution UI, already confirmed the
DTO/route contract against this work's in-progress state) and a separate backend-developer agent
in an isolated worktree on receipt enrichment + discrepancy tickets (no file overlap observed).

## What changed

`backend/ShelfGuard.Application/Features/Marketplace/Dtos/CooperationDtos.cs` —
`CreateMarketplaceOrderItemDto` gained `CatalogAction` (`null`/`"auto"`|`"link"`|`"create_new"`)
and `LinkedItemId`. New `CheckMarketplaceOrderConflictsDto`, `MarketplaceOrderConflictingItemDto`,
`MarketplaceOrderConflictDto`.

`backend/ShelfGuard.Application/Features/Catalog/Dtos/ItemDto.cs` — `CreateProductRequest` gained
trailing optional `Guid? SourceSupplierItemId = null` (positional-arg backward compatible;
verified against the two existing positional call sites: `ImportRunner.cs`, `ItemServiceTests.cs`).
`ItemService.CreateAsync` now sets `Item.SourceSupplierItemId` from it.

`MarketplaceOrderService.cs`: new ctor deps `IItemRepository`, `IItemService`.
- `CreateOrderAsync` now runs a two-pass per line: pass 1 validates (extracted the pre-existing
  inline checks into `ValidateLine`, shared with the new endpoint) and plans the catalog outcome
  read-only (`PlanCatalogOutcomeAsync` — resolves `CatalogAction`, re-checks barcode collision
  authoritatively via `IItemRepository.GetByAnyBarcodeAsync`, never trusts a stale earlier
  `/conflicts` call); pass 2 (`ExecuteCatalogPlanAsync`) executes every plan only after all lines
  in the order have cleared pass 1 — deliberate, so a failure on line N can never leave lines
  1..N-1's auto-provisioned Items already committed while the overall order creation still fails
  (which would duplicate them on client retry).
- New public `CheckCatalogConflictsAsync(clientTenantId, supplierId, items, ct)` — read-only,
  reuses `ValidateLine` + the same gate check as `CreateOrderAsync`, adds the barcode-collision
  read. Creates nothing.
- `link`: validates `LinkedItemId` resolves via ambient-RLS-scoped `GetByIdAsync` (foreign-tenant
  id → null → rejected) and that its `Barcodes` actually intersects the `SupplierItem`'s —
  defence against a forged/stale request. Sets `SourceSupplierItemId` on the existing Item, no new
  Item created.
- `create`/`auto`/`create_new`: builds `CreateProductRequest` from the `SupplierItem` snapshot —
  `CategoryId`/`SegmentId` null (no supplier→client category mapping exists),
  `MinStock`/`MaxStock`/`SafetyBuffer` 0 (no supplier-side stock-policy equivalent),
  `ManagementType "NA"` (no default exists, brief mandated this literal), `VatRate 0` (**no
  tenant-level default VAT rate exists anywhere in this codebase — confirmed by search; known
  simplification**), `PricePurchase` from `SupplierItem.Price`, `PriceRetail` null, `ImageUrl` via
  `PickImageUrl` (lowest-`SortOrder` `Kind="main"` image, falling back to the lowest-`SortOrder`
  image of any kind — matches `MarketplaceService`/`SupplierCabinetService`'s own
  `Images.OrderBy(SortOrder)` convention, no existing "main first" helper was already in the
  codebase to reuse). Calls `ItemService.CreateAsync` directly (service-to-service — the ordering
  user may not hold `AtLeastStoreManager`, so routing through `POST /api/items` was out per brief).

`MarketplaceCooperationController.cs` — new `POST
api/marketplace/suppliers/{id:guid}/orders/conflicts`, class-level `[RequireModule("marketplace")]`
gate only (no extra policy — read-only). Body `{ items: CreateMarketplaceOrderItemDto[] }` (qty
included for shape parity with the real order, unused by the check itself). Maps `IsGateViolation`
→ 403, `SupplierNotFoundError` → 404, else 400. 200 → `MarketplaceOrderConflictDto[]`.

## Contract (frontend already built against this — see TASK-597 log)

```
POST /api/marketplace/suppliers/{id}/orders/conflicts
  body:  { items: [{ supplierItemId, qty, catalogAction?, linkedItemId? }] }
  200:   [{ supplierItemId, existingItem: { id, name, imageUrl, barcodes } }]
  403:   { error }  (no active agreement)
  404:   { error }  (supplier not found)
  400:   { error }  (validation)

CreateMarketplaceOrderItemDto (existing POST .../orders body, extended):
  { supplierItemId, qty, catalogAction?: null|"auto"|"link"|"create_new", linkedItemId?: guid }
```

New errors (Ukrainian, matching codebase convention): `BarcodeCollisionError`,
`LinkedItemRequiredError`, `LinkedItemNotFoundError`, `LinkedItemBarcodeMismatchError`,
`SupplierItemNameMissingError`, `CatalogProvisioningFailedError`.

## Tests

9 new cases in `MarketplaceOrderServiceTests.cs` (constructor now stubs `_itemService.CreateAsync`
to succeed by default + `_items.GetByAnyBarcodeAsync` to empty by default, so every pre-existing
test keeps passing unchanged): conflicts endpoint (no collision → empty, collision → matched item
details), auto-create with barcode/price/`SourceSupplierItemId` copied correctly, collision+auto
→ rejected + zero Item created, collision+`link` → existing Item linked + zero new Item, `link`
with a foreign/non-owned id → rejected, `link` with a non-overlapping barcode → rejected,
collision+`create_new` → duplicate created anyway, no-barcodes regression → collision check
skipped entirely (`GetByAnyBarcodeAsync` never called) and auto-create still succeeds.

## Verification

- `dotnet build backend/ShelfGuard.sln` — clean, 0 errors (same 1 pre-existing unrelated warning
  as TASK-596/prior logs, `MarketplaceServiceTests.cs:534`).
- `dotnet test --filter "FullyQualifiedName~Marketplace"` — 223/223 passing.
- `dotnet test` (full suite) — **1819 passed, 0 failed** (1810 baseline + 9 new).
