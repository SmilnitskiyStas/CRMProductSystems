# TASK-595 — Marketplace order receiving: post-implementation API reference (docs)

**Status:** done · **Agent:** documentation-writer

Wrote `mobile/features/marketplace-orders/API.md` — a reference doc (not a build spec) for the
already-shipped marketplace order receiving feature (backend TASK-586/ADR-033, mobile committed
`aeb830fc`, both live in prod). Replaces the old pre-implementation handoff
(`.claude/logs/handoffs/586-to-mobile-codex.md`) as the contract-of-record for future work in this
directory, while keeping that handoff linked for rationale/history.

Covers: what the feature does, file map (mobile + backend), full API contract (5 endpoints, DTOs,
error strings, auth policy split reads vs. mutations), known v1 limitations, and
confirmed-implemented details not knowable at spec time (manual search fallback wiring, datetimepicker
9.1.0, barcode types scanned, and that `saveItem()` always sends a full field snapshot so the
PUT's merge-vs-overwrite distinction is currently moot in practice).

**Verification:** read every cited file directly — `MarketplaceCooperationController.cs` (routes +
`[Authorize(Policy = AppPolicies.CanReceiveStock)]` placement), `MarketplaceOrderReceiptService.cs`
(all 9 error-message constants), `CooperationDtos.cs` (`MarketplaceOrderDto`,
`MarketplaceOrderReceiptDto`, `MarketplaceOrderReceiptItemDto`,
`UpdateMarketplaceOrderReceiptItemRequest` — field names/types/nullability), and the mobile
`types.ts` / `marketplaceOrdersApi.ts` / `useMarketplaceOrders.ts` / `[orderId].tsx` /
`index.tsx`. Every route, policy, error string, and DTO field in the old handoff matched the
current source exactly — no drift found. One deviation worth flagging: the mobile `types.ts`
`UpdateReceiptItemRequest` interface declares `productId`/`quantityReceived`/`expiryDate` as
required (not optional like the backend's nullable request record) — documented explicitly in the
new file as "shape of one call site, not a mirror of the backend contract," not a bug.
