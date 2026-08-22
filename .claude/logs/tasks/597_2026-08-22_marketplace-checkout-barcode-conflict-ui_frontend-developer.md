# TASK-597 — Marketplace checkout: barcode-conflict resolution UI

**Agent:** frontend-developer
**Status:** done (frontend only; backend endpoint still WIP by parallel agents, see below)
**Scope:** `/frontend` only, per brief. Did not touch `/backend` or `/mobile`.

## What changed

1. `frontend/features/marketplace/types.ts` — added `CatalogAction`, `CreateMarketplaceOrderItem`
   (extends the old inline items-array type with optional `catalogAction`/`linkedItemId`),
   `BarcodeConflictExistingItem`, `BarcodeConflict`. `CreateMarketplaceOrderRequest.items` now
   typed as `CreateMarketplaceOrderItem[]`.
2. `frontend/features/marketplace/api/marketplace-api.ts` — added
   `checkOrderConflicts(supplierId, items)` → `POST /api/marketplace/suppliers/{id}/orders/conflicts`.
3. `frontend/features/marketplace/hooks/useCooperation.ts` — added `useCheckOrderConflicts(supplierId)`,
   a `useMutation` (on-demand, not `useQuery`).
4. `frontend/features/marketplace/components/SupplierOrderCart.tsx` — checkout modal is now a
   two-step flow via local `step: "cart" | "conflicts"` state:
   - Confirm on the cart step calls `checkOrderConflicts` first. Empty result → submits exactly
     as before (no UX change, one click). Non-empty → switches to a "conflicts" step, same modal.
   - Conflicts step renders one card per conflict: ordered line name (from cart), existing item's
     photo (falls back to `ImageOff` icon placeholder, same pattern as
     `SupplierItemDetailDialog.tsx`'s image gallery)/name/barcodes, and two toggle buttons
     ("Прив'язати до цього товару" → `catalogAction: "link"` + `linkedItemId`, "Все одно
     створити новий товар" → `catalogAction: "create_new"`). Confirm is disabled until every
     conflict has a choice.
   - "Back" returns to the cart step (clears conflicts/resolutions, keeps qty/store/comment).
     Closing the modal (X, overlay click, Close button) resets both steps.
5. i18n — added 9 keys under `Dashboard.marketplace.orderCart` in both
   `frontend/messages/uk.json` and `frontend/messages/en.json` (checkingConflicts, back,
   conflictStepTitle, conflictIntro, conflictOrderedLabel, conflictExistingLabel,
   conflictNoBarcodes, conflictLinkAction, conflictCreateNewAction).

## Types declared (final shapes)

```ts
export type CatalogAction = "auto" | "link" | "create_new";

export interface CreateMarketplaceOrderItem {
  supplierItemId: string;
  qty: number;
  catalogAction?: CatalogAction;
  linkedItemId?: string;
}

export interface CreateMarketplaceOrderRequest {
  items: CreateMarketplaceOrderItem[];
  comment?: string;
  destinationStoreId: string;
}

export interface BarcodeConflictExistingItem {
  id: string;
  name: string;
  imageUrl: string | null;
  barcodes: string[];
}

export interface BarcodeConflict {
  supplierItemId: string;
  existingItem: BarcodeConflictExistingItem;
}
```

## Contract verification against real (in-progress) backend

Backend work is uncommitted but present in the working tree
(`backend/ShelfGuard.Application/Features/Marketplace/Dtos/CooperationDtos.cs`,
`MarketplaceOrderService.CheckCatalogConflictsAsync`). Confirmed field names match exactly:
`MarketplaceOrderConflictDto(Guid SupplierItemId, MarketplaceOrderConflictingItemDto ExistingItem)`,
`MarketplaceOrderConflictingItemDto(Guid Id, string Name, string? ImageUrl, List<string> Barcodes)`
→ camelCase JSON (`supplierItemId`/`existingItem`/`id`/`name`/`imageUrl`/`barcodes`) matches the
TS types above 1:1. `CreateMarketplaceOrderItemDto` also confirms `catalogAction`/`linkedItemId`
naming. **Not yet present**: the controller route itself — `MarketplaceCooperationController.cs`
has no `orders/conflicts` endpoint wired up yet (service method exists, HTTP route doesn't), so
this is still genuinely in progress on the backend side, not a naming mismatch.

## Verification

- `npx tsc --noEmit` (frontend) — clean.
- `npx eslint` on all 4 touched files — clean.
- `uk.json`/`en.json` — valid JSON (checked via `JSON.parse`).
- **Not verified live**: the conflict-resolution step itself, since the backend
  `orders/conflicts` HTTP endpoint doesn't exist yet (confirmed above — service layer exists,
  controller route doesn't). Did not attempt to start the dev server / log in, per instructions.
  The no-conflict path is unchanged code-for-code from before except for the added pre-flight
  mutation call, which is a straightforward, typed, tested-by-compiler addition.
