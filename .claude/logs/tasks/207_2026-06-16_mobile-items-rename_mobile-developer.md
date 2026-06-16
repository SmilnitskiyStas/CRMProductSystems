# TASK-207 — Mobile: products → items

**Agent:** mobile-developer
**Status:** done
**Date:** 2026-06-16
**Depends:** TASK-205 (backend rename, done)

## Summary
Updated the mobile app to call the new `/api/items` routes directly instead of relying on the legacy `/api/products` 301-redirect controller.

## Changes
- `mobile/features/stock/api/stockApi.ts` — `getProductByBarcode()` now calls `/items/by-barcode/${barcode}` instead of `/products/by-barcode/${barcode}`.
- `mobile/app/(app)/pos/scanner.tsx` — updated the `ProductInfo` interface's source comment to reference `/items/by-barcode/:barcode`, and added an optional `itemType?: string` field for forward-compatibility with the new `Item` DTO shape (not consumed in the UI yet).

## Not changed (by design)
- FK field names (`productId`, `productName`, `productStockId`) in `mobile/features/write-offs/types.ts`, `mobile/features/transfers/types.ts`, etc. — backend kept these column/field names unchanged during the entity rename.
- `mobile/app/(app)/transfers/create.tsx` — consumes `getProductByBarcode()` via the updated `stockApi.ts`; no local typed interface for the barcode response here, so nothing to update.

## Verification
- Grepped entire `mobile/` tree for literal `'/products` / `"/products` / `/catalog` API paths — only the one occurrence in `stockApi.ts` existed; now fixed. No remaining literal legacy routes.
- `cd mobile && npx tsc --noEmit` → 0 errors.

## Out of scope / follow-up
- None identified. Backend `/api/products/*` legacy redirect controller can be removed in a future cleanup task once all clients (web, mobile) are confirmed off it — not actioned here since frontend work is a separate task.
