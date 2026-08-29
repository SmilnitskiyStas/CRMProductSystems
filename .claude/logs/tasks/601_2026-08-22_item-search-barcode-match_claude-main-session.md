# TASK-601: `GET /api/items?search=` didn't match barcodes — blocked manual receiving fallback

**Status:** done · **Agent:** main session (direct fix, <10 lines, per CLAUDE.md's exception for a
quick isolated fix in a single well-known file)

## Bug report

User couldn't find a real product (barcode `5999076269549`, store "Свіжий Кут") through the
mobile marketplace-order receiving screen's manual entry path, despite confirming the item
existed in the store's own catalog with that exact barcode (added via TASK-596..598's
order-time catalog auto-provisioning).

## Root cause

`mobile/app/(app)/marketplace-orders/[orderId].tsx` has no dedicated "enter barcode" field. Its
only two product-resolution paths are: (1) camera scan → `getProductByBarcode()` → exact backend
barcode match, or (2) "Знайти вручну" → a text field labeled "Назва товару" (product name) →
`searchCatalogProducts()` → `GET /api/items?search=`. That endpoint
(`ItemRepository.GetPagedAsync`, `backend/ShelfGuard.Infrastructure/Data/Repositories/ItemRepository.cs`)
only ever matched `search` against `Item.Name` via `EF.Functions.ILike` — barcodes were never
considered. Typing a barcode number into that field searched for a product literally *named*
that number, which obviously never matches. This blocked the two orders (`MP-2026-001`,
`MP-2026-003`) already flagged as unreceivable through the normal flow (shipped before
`DestinationStoreId` existed) from being resolved via mobile receiving at all — camera scanning
isn't a viable substitute in a remote/manual-testing session, and the only fallback silently
failed for the exact reason above.

## Fix

`ItemRepository.GetPagedAsync`'s `search` filter now also matches an exact barcode via
`EF.Functions.JsonContains(p.Barcodes, ...)`, OR'd with the existing name `ILike`. No mobile
changes needed — `searchCatalogProducts()` already calls this same endpoint, so typing a full
barcode into the existing "Знайти вручну" field now resolves it, with zero UI changes on the
mobile side (mobile ownership stays with the separate Codex agent; this fix is entirely backend).

## Why this needed a real-Postgres check, not just `dotnet build`

This codebase has documented prior incidents (see `ItemRepository.GetByBarcodeAsync`'s own
comment, and `ItemRepositoryGetPagedTests.cs`'s class doc) of `EF.Functions.ILike`/`JsonContains`
LINQ shapes that compile and pass against the InMemory test provider but throw or silently
no-op against real Postgres. Added
`backend/ShelfGuard.Tests/Infrastructure/ItemRepositoryGetPagedBarcodeSearchIntegrationTests.cs`
(live-Postgres, same connection/skip/cleanup convention as
`ItemRepositoryGetByAnyBarcodeIntegrationTests`) — 3 cases: exact-barcode match works even when
name doesn't match, name-substring search still works (regression), no match returns empty. All
3 passed against the local dev DB before considering this done.

## Verification

- `dotnet build` — clean, 0 errors.
- New integration tests — 3/3 passed against real Postgres (`crmproductsystems-postgres-1`).
- Full `dotnet test` — **1828/1828 passed**.

## Not done / follow-up

- Nothing committed or pushed — this session only commits/pushes on explicit request.
- Once deployed, the two stranded orders (`MP-2026-001`, `MP-2026-003`) should now be receivable
  through the normal mobile flow (manual search by barcode now works) — no SQL workaround needed,
  superseding the earlier manual-fix discussion for those two orders specifically.
- This is a generically useful fix beyond the marketplace-receiving screen — any other caller of
  `GET /api/items?search=` (main web Inventory/Catalog search, etc.) now also matches barcodes.
