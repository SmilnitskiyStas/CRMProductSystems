# TASK-600 — Marketplace order receiving: API reference update (docs)

**Status:** done · **Agent:** documentation-writer

Updated `mobile/features/marketplace-orders/API.md` (written after TASK-586/595) to reflect
TASK-596..599 (commits `96a4fefe`, `40632ba8`, both 2026-08-22), which landed on top of it.

Changes:
- Status banner: added an addendum block noting the two new backend changes, without rewriting
  the original TASK-586 banner.
- DTO shapes: added `price: number` and `referenceImageUrl: string | null` to the
  `MarketplaceOrderReceiptItemDto` TS block, field names/order/nullability verified directly
  against the current `MarketplaceOrderReceiptItemDto` record in
  `backend/ShelfGuard.Application/Features/Marketplace/Dtos/CooperationDtos.cs` (Price then
  ReferenceImageUrl, immediately after IsResolved — matches).
- Endpoint d field-semantics: added a note that non-empty `discrepancyNotes` at finalize
  auto-opens a supplier support ticket server-side (TASK-599) — no new mobile call needed.
  Mirrored with a shorter pointer on the DTO's `discrepancyNotes` comment.
- Known v1 limitations: rewrote the barcode-crosswalk bullet — still a real limitation, but now
  hit less often since orders auto-provision the client's own `Item` catalog at order time
  (TASK-596..598); manual-search fallback still needed for pre-existing orders, resolved barcode
  collisions, and edge cases.
- New section "Not yet built: price + reference-photo display in the receiving UI" — explicitly
  framed as a build note, not shipped behavior. Describes intended UX (item list row + detail/
  scan view, visible pre-scan since `referenceImageUrl` falls back to the supplier photo before
  `productId` resolves) and points to the existing no-image placeholder pattern already used in
  `mobile/app/(personal)/catalog.tsx` (~line 199) and `mobile/app/(personal)/product/[id].tsx`
  (~line 105) as the pattern to reuse, rather than inventing a new one.

Verification: read the live `MarketplaceOrderReceiptItemDto` record and
`mobile/app/(app)/marketplace-orders/[orderId].tsx` directly before writing; confirmed the mobile
screen currently renders neither new field and `types.ts`'s `MarketplaceOrderReceiptItem` doesn't
declare them yet (noted in the new section). Docs-only change, no code touched.
