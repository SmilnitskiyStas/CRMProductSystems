# TASK-602: Write-off reimbursement + purchase-price loss — schema

**Status:** done
**Agent:** database-engineer
**Scope:** Domain entities + EF migration + AppDbContext only (Application/Api and mobile out of scope — next agent: backend-developer)

## Changes

- `backend/ShelfGuard.Domain/Entities/Item.cs`: added `DefaultReimbursementType` (string?), `DefaultReimbursementValue` (decimal?) after `PriceRetail`.
- `backend/ShelfGuard.Domain/Entities/WriteOffItem.cs`: added `UnitPricePurchase`, `LossAmountPurchase`, `IsReturnedToSupplier`, `ReimbursementType`, `ReimbursementValue`, `ReimbursementAmount` (all `init`, matching existing entity style) after `LossAmount`.
- `backend/ShelfGuard.Domain/Entities/WriteOff.cs`: added `TotalLossAmountPurchase`, `TotalReimbursementAmount` after `TotalLossAmount`. No `NetLossAmount` column (DTO-only, next agent).
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs`: added `.Property(...)` config for all 8 new fields in the `Item`, `WriteOffItem`, `WriteOff` fluent-config blocks — `decimal(12,2)` for money fields (matches `PricePurchase`/`PriceRetail`/`UnitPrice`/`LossAmount`/`TotalLossAmount`), `.HasMaxLength(10)` for the two type strings, `.HasDefaultValue(false)` for `IsReturnedToSupplier`.

## Migration

`20260823190911_AddWriteOffReimbursementAndPurchaseLoss` (`backend/ShelfGuard.Infrastructure/Migrations/`).

Generated types matched the target exactly on first pass — no manual correction needed:
- All decimal columns → `numeric(12,2)` (Postgres synonym for `decimal(12,2)`), not EF's default `numeric(18,2)`.
- `ReimbursementType`/`DefaultReimbursementType` → `character varying(10)`.
- `IsReturnedToSupplier` → `boolean not null default false`.

## RLS confirmation

Read `backend/ShelfGuard.Infrastructure/Migrations/20260628000000_ForceRlsOnAllTenantTables.cs`: FORCE ROW LEVEL SECURITY is applied via a `pg_tables` loop keyed on `rowsecurity = true`, i.e. per-table, not per-column. `items`, `write_off_items`, `write_offs` are already in that set. New columns on already-RLS-protected tables are covered automatically — **no RLS policy changes needed**.

## Verification

- `dotnet ef database update` applied cleanly against local dev Postgres (`crm` DB, port 5435, `shelfguard_app_dev` — note: `dotnet ef` design-time factory defaults to a stale `postgres/postgres@5432` fallback connection string in `AppDbContextFactory.cs`; had to export `ConnectionStrings__DefaultConnection` matching `appsettings.Development.json` to reach the real dev DB).
- Confirmed via `psql \d` that all 8 columns exist in `items`, `write_off_items`, `write_offs` with the exact expected types (`numeric(12,2)`, `character varying(10)`, `boolean not null default false`).
- `dotnet build` on the full solution: **0 errors**, 1 pre-existing unrelated warning (`MarketplaceServiceTests.cs:534`, possible null deref — not touched by this change).

## Deviations from brief

None. All 8 properties added exactly as specified; migration types correct without manual edits.

## Handoff

Not created per instructions — orchestrating session chains directly to `backend-developer` for Application-layer work (`WriteOffDtos.cs`, `WriteOffService.cs`, `ItemDto`/`ItemService`, `ProductStockDto`/`StockService`, `WriteOffServiceTests.cs`).
