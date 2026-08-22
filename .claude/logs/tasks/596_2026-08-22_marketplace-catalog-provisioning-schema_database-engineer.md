# TASK-596 — Marketplace catalog auto-provisioning + discrepancy tickets: schema (Wave 1)

**Agent:** database-engineer
**Status:** done
**Scope:** entity + EF config + migration + one repository method only — service/controller/DTO
logic (ItemService, MarketplaceOrderService, MarketplaceOrderReceiptService,
SupplierSupportService, any controller) is a follow-up wave for backend-developer agents.

## What changed

### 1. `Item.SourceSupplierItemId`
`backend/ShelfGuard.Domain/Entities/Item.cs` — new `public Guid? SourceSupplierItemId { get; set; }`
(next to `DefaultSupplierId`, same nullable-Guid style) + nav `public SupplierItem?
SourceSupplierItem { get; init; }` (next to `DefaultSupplier`, matching that nav's style).
Lineage pointer: which marketplace `SupplierItem` listing a client's `Item` was auto-provisioned
from at order time.

`AppDbContext.cs` (Item config block): `e.HasOne(p => p.SourceSupplierItem).WithMany()
.HasForeignKey(p => p.SourceSupplierItemId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);`
— **no standalone `e.HasIndex(...)` call** (see gotcha below); relies on the implicit
FK-by-convention index, same as `CategoryId`/`SegmentId`/`DefaultSupplierId` above it in the
same block.

**Gotcha (spend a minute on this before copying the pattern elsewhere):** `Item` and
`SupplierItem` already have a reference nav pointing at each other in the *other* direction
(`SupplierItem.Item` → `Item`, via `ItemId`, pre-existing). Adding an explicit standalone
`e.HasIndex(p => p.SourceSupplierItemId);` (copying the `SupplierItem.ItemId` block's own
style verbatim) made EF's relationship-discovery convention treat the two independently-
configured FKs as a single 1:1 pair, silently marking `IX_items_SourceSupplierItemId` **unique**
in the generated migration — confirmed by generating the migration once, inspecting it,
reverting (`dotnet ef migrations remove`), dropping the explicit `HasIndex`, and regenerating;
the second migration's index came out correctly non-unique. Root cause not fully isolated
beyond "two single reference navs pointing at each other between the same two entity types,
one of them backed by an extra explicit same-property `HasIndex`" — not worth the design-time
detour to pin down further given the working fix. If a future FK is added going *back* the
other way between two types that already have a nav pointing at each other, verify the
generated migration's index `unique:` flag before applying, don't assume the FK-index
convention alone is always non-unique.

### 2. `SupplierSupportTicket.MarketplaceOrderId`
`backend/ShelfGuard.Domain/Entities/SupplierSupportTicket.cs` — new `public Guid?
MarketplaceOrderId { get; set; }` (mutable, matches `CreatedByUserId`'s style; no nav property,
same as `CreatedByUserId` has none). Order this ticket was auto-opened for on a flagged
receiving discrepancy; null for manually-opened tickets.

`AppDbContext.cs` (`SupplierSupportTicket` config block): `e.HasIndex(x =>
x.MarketplaceOrderId);` + `e.HasOne<MarketplaceOrder>().WithMany().HasForeignKey(x =>
x.MarketplaceOrderId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);` — anonymous-nav
style matching `CreatedByUserId`'s own FK block, `Restrict` (not `SetNull` like
`CreatedByUserId`) per the brief: matches `MarketplaceOrderReceipt.MarketplaceOrderId`'s own
choice — orders are never hard-deleted, only status-transitioned.
`SupplierSupportTicketMessage` untouched, as instructed.

### 3. `IItemRepository.GetByAnyBarcodeAsync`
Final signature (`backend/ShelfGuard.Domain/Interfaces/IItemRepository.cs`):
```csharp
Task<IReadOnlyList<Item>> GetByAnyBarcodeAsync(IReadOnlyList<string> barcodes, CancellationToken ct = default);
```
Implementation (`backend/ShelfGuard.Infrastructure/Data/Repositories/ItemRepository.cs`, right
after `GetByBarcodeAsync`): single-query jsonb-overlap approach, **not** a barcode-count loop.
Confirmed via reflecting the installed `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.11 package
(`NpgsqlJsonDbFunctionsExtensions`) that `EF.Functions.JsonExistAny(object json, string[] keys)`
exists and translates to Postgres's `?|` array-overlap operator (`"Barcodes" ?| ARRAY[...]`) —
same jsonb column `GetByBarcodeAsync` already targets with `JsonContains`/`@>`. One round trip
regardless of how many barcodes are passed; short-circuits to `Array.Empty<Item>()` without
querying when the input list is empty. Same `.Include(Category/Segment/DefaultSupplier)` shape
as the existing single-barcode method.

Two existing `IItemRepository` fakes needed the new member added to keep compiling:
`ShelfGuard.Tests/Pos/PosServiceTests.cs` (`FakeCatalogRepo` — real filter over its in-memory
`Products` list) and `ShelfGuard.Tests/Pos/FiscalizationRetryTests.cs`
(`RetryFakeCatalogRepo` — trivial empty-list stub, matches its other unused-method stubs).

## Verification

- `dotnet build backend/ShelfGuard.sln` — clean, 0 errors (1 pre-existing unrelated warning,
  `MarketplaceServiceTests.cs:534`, same one prior task logs note).
- Migration `20260822134439_AddItemSourceSupplierItemAndTicketOrderRef` applied to local dev DB
  (`crmproductsystems-postgres-1`, port 5435; `ConnectionStrings__DefaultConnection` exported
  manually — `AppDbContextFactory`'s design-time factory ignores `appsettings.Development.json`,
  same gotcha TASK-592/584 already logged).
- `\d items` / `\d supplier_support_tickets` in psql confirm both columns, both indexes
  (`IX_items_SourceSupplierItemId` — **non-unique**, `IX_supplier_support_tickets_
  MarketplaceOrderId`), and both FKs (`FK_items_supplier_items_SourceSupplierItemId` → `supplier_
  items(Id)` ON DELETE SET NULL; `FK_supplier_support_tickets_marketplace_orders_
  MarketplaceOrde~` → `marketplace_orders(Id)` ON DELETE RESTRICT) present exactly as designed.
  `supplier_support_tickets`' existing RLS policies (`tenant_isolation`/`provider_bypass`/
  `worker_bypass`) untouched — no new tenant column was added, no RLS change needed.
- `dotnet test backend/ShelfGuard.sln` — **1810 passed, 0 failed** (1807 baseline + 3 new).
- Added 3 direct integration tests against real Postgres for `GetByAnyBarcodeAsync`
  (`ShelfGuard.Tests/Infrastructure/ItemRepositoryGetByAnyBarcodeIntegrationTests.cs`, same
  connection/skip/cleanup pattern as `PriceSegmentsRepositoryIntegrationTests`): matches-any
  + dedup (an item matching 2 of the given barcodes appears once, not twice), no-match →
  empty, empty-input → empty without querying. All pass — the `JsonExistAny`/`?|` translation
  is confirmed correct against a live database, not just compiling.

## For the next wave (backend-developer × 2)

Contract to build against:
- `Item.SourceSupplierItemId` (`Guid?`) / nav `Item.SourceSupplierItem` (`SupplierItem?`).
  Table `items`, column `SourceSupplierItemId`, FK `FK_items_supplier_items_
  SourceSupplierItemId` → `supplier_items(Id)` ON DELETE SET NULL, index
  `IX_items_SourceSupplierItemId` (non-unique — many client Items can, in principle, trace back
  to listings; don't assume 1:1).
- `SupplierSupportTicket.MarketplaceOrderId` (`Guid?`, no nav property — resolve via a separate
  `MarketplaceOrder` lookup if needed, same as this entity already does for `CreatedByUserId`/
  `User`). Table `supplier_support_tickets`, column `MarketplaceOrderId`, FK
  `FK_supplier_support_tickets_marketplace_orders_MarketplaceOrde~` → `marketplace_orders(Id)`
  ON DELETE RESTRICT, index `IX_supplier_support_tickets_MarketplaceOrderId`.
- `IItemRepository.GetByAnyBarcodeAsync(IReadOnlyList<string> barcodes, CancellationToken ct = default)`
  → `Task<IReadOnlyList<Item>>`. Empty input → empty result, no query. One item per matching
  `Item.Id`, regardless of how many of the given barcodes it matches.
- Not touched (deliberately, per brief): `ItemService.cs`, `MarketplaceOrderService.cs`,
  `MarketplaceOrderReceiptService.cs`, `SupplierSupportService.cs`, any controller/DTO. Setting
  `SourceSupplierItemId` at order-provisioning time, wiring the discrepancy → auto-open-ticket
  flow (including populating `MarketplaceOrderId`), and exposing either field through any DTO
  is entirely next-wave work.
