# TASK-204 — DB: catalog_products → items + item_type

**Agent:** database-engineer · **Date:** 2026-06-16 · **Status:** done

## What was done
- EF Core migration `V4ItemsRename` (`20260616042437_V4ItemsRename`):
  - Renamed table `catalog_products` → `items`
  - Added column `ItemType` (varchar 50, default `'product'`)
  - Renamed indexes `IX_catalog_products_*` → `IX_items_*`
  - Dropped and recreated all FK constraints pointing at `catalog_products` to point at `items`
- `CatalogProduct.cs`: added `ItemType` property (default `"product"`)
- `AppDbContext.cs`: `CatalogProduct` entity now `ToTable("items")`, `ItemType` configured with max length 50 + default value
- POC `products` table: confirmed already absent locally (dropped in TASK-201) — no action needed
- RLS policies (`tenant_isolation`, `provider_bypass`) on the table: confirmed they survive `RenameTable` automatically since their `qual` doesn't reference the table by name — no manual SQL needed

## Migration generation mismatches found and fixed (same class of issue as TASK-200)
EF's generated migration was derived from the C# model, which is missing `HasOne(Product)` navigation config for two entities that DO have a real FK in the DB:
1. **`discounts.ProductId`** → `FK_discounts_catalog_products_ProductId` (ON DELETE RESTRICT) — EF didn't know about it, so the generated migration didn't drop/recreate it. Added manually to both `Up()` and `Down()`.
2. **`stock_movements.ProductId`** → `FK_stock_movements_catalog_products_ProductId` (ON DELETE RESTRICT) — same issue. Added manually.

Also removed:
- 3 bogus `DropForeignKey`/`AddForeignKey` pairs for `stock_receipt_items`, `stock_transfer_items`, `write_off_items` — these tables have `ProductId` columns but **no FK constraint exists in the actual DB** (confirmed via `pg_constraint` query). EF generated them based on stale model assumptions.
- 2 bogus `RenameColumn` calls (`stock_movements.ToStoreId→ToLocationId`, `FromStoreId→FromLocationId`) — these columns were already renamed to `ToLocationId`/`FromLocationId` by the earlier `V4LocationsRename` migration; the new migration's snapshot diff incorrectly thought they still needed renaming, causing `column "ToStoreId" does not exist`.

## Verification
- Queried actual `pg_constraint` for all FKs referencing `catalog_products` before writing the migration — found 14 (not 10) inbound, the extra ones being `discounts` and `stock_movements`.
- `dotnet build` → 0 errors
- `dotnet ef database update` → applied cleanly on second attempt (first attempt failed on `DROP CONSTRAINT PK_catalog_products` due to the missing `stock_movements` FK drop; transaction rolled back cleanly, DB left in correct prior state)
- Confirmed post-migration: `items` table exists, `ItemType` column present, RLS policies (`tenant_isolation`, `provider_bypass`) intact

## Lesson reinforced
Same lesson as TASK-200: **never trust EF's generated migration as the full picture for table renames.** Always query `pg_constraint` / `information_schema` for the ACTUAL FK set before applying, especially for FKs on entities that lack a `HasOne()` navigation in `AppDbContext` (EF won't manage those at all, so it won't include them in the diff — but they still break the migration when the underlying table is renamed and `DropPrimaryKey` is attempted while a stray FK still points at it).

## Next
TASK-205 — Backend: CatalogProduct → Item entity + API rename (depends on this).
