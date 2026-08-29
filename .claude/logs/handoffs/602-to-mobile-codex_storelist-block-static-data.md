# Handoff: TASK-602 → mobile (Codex agent) — `storeList` App Builder block renders static data instead of real stores

**Bug report:** tenant sees only 1 of 3 real stores on the consumer app's home screen "Store List"
block, despite the block's `limit` prop being raised well above 3. Diagnosed by the main Claude
session (backend + web); this fix belongs on mobile per this project's established division of
labor.

## Root cause

`StoreListBlock` (`mobile/features/server-driven-ui/blocks/CoreBlocks.tsx`, ~line 243) already
calls `useAvailableNetworks(hasPersonalAccess)` (line 248) — the same live, working
`GET /consumer/loyalty/networks` endpoint the home screen's OTHER store picker
("Оберіть зручний магазин", `mobile/app/(personal)/index.tsx`) already uses correctly via
`mergeStoreNetworks(networksQuery.data, memberships)` (`mobile/features/loyalty/selection.ts`).

But `StoreListBlock`'s expanded list (line 319: `block.props.items.map(...)`) renders
**`block.props.items`** — the block's static, App-Builder-authored config value (same shape as a
curated `productGrid`'s hand-picked product list) — not `networksQuery.data`. The `networksQuery`
fetch happens (and is used to `refetch()` on open, and to read `membership.preferredStoreId`/
`preferredStoreName` for highlighting), but its actual store list is never rendered. Whatever
static `items` value got saved into that tenant's block config (likely a single placeholder/seed
store from whenever the block was first added to the canvas) is all that ever shows — independent
of how many real `Location` rows the tenant has.

Confirmed on the backend side (not the bug, just context): `LoyaltyService.LoadNetworkDetailsAsync`
(`backend/ShelfGuard.Application/Features/Loyalty/LoyaltyService.cs:343-356`) correctly returns
all of a tenant's active, shoppable-type `Location`s — this is proven working because the sibling
home-screen picker already displays all 3 stores correctly using the exact same endpoint. **Do
not go looking for a backend bug — there isn't one here.**

## What "not yet wired" looks like today, concretely

- `block.props.limit` (int, default 10, max 30 — `backend/ShelfGuard.Application/Features/MobileConfig/BlockRegistry/BlockRegistry.cs:226`)
  is a real, editable field in the App Builder UI (`BlockPropertyEditor.tsx` renders it as a
  number input), but **nothing in `StoreListBlock` ever reads `block.props.limit`** — it has zero
  effect on what's rendered today.
- The block registry's own `SupportedDataSource` comment
  (`BlockRegistry.cs:229-232`) says "No dedicated consumer-facing store-list endpoint exists yet
  today... flagged as a gap for a future consumer endpoint" — **this is stale/inaccurate**. The
  endpoint (`GET /consumer/loyalty/networks`) already exists and is already being called from
  inside this exact component; it's a rendering-source bug, not a missing-endpoint gap. Worth
  correcting that comment once this is fixed (small backend string change, or flag it back to the
  main session — your call, low stakes either way).

## Fix

In `StoreListBlock`, replace the `block.props.items.map(...)` render source with the consumer's
own joined-network stores, derived the same way the sibling picker does:

1. Find the network matching the consumer's currently selected/preferred membership
   (`membership.tenantId`, already available in this component via `useSelectedConsumerContext`
   equivalent — check how `membership` is obtained here vs. in `index.tsx`, they may differ
   slightly; reconcile so both read from the same source of truth).
2. Get that network's `stores: LoyaltyNetworkStore[]` from `networksQuery.data` (or reuse
   `mergeStoreNetworks(networksQuery.data, membershipsQuery.data)` from
   `mobile/features/loyalty/selection.ts` for consistency with the other picker — check whether
   `membershipsQuery` is already available in this component's scope or needs to be added).
3. Apply `block.props.limit` via `.slice(0, block.props.limit)` before rendering — this makes the
   previously-inert `limit` field actually do something.
4. `LoyaltyNetworkStore` (backend `LoyaltyNetworkStoreDto`) doesn't carry `distanceKm`/`openNow` —
   those fields exist on the local `StoreItem` type
   (`mobile/features/server-driven-ui/blocks/types.ts:98-104`) but nothing computes them anywhere
   in the codebase today (confirmed: grepped for `distanceKm`/`openNow` computation outside the
   home screen's own `findNearestStore()`, which is a one-shot "nearest store" action, not a
   per-item live distance feed). Decide: drop `showDistance`/`openNow` rendering from this block
   until a real distance/hours data source exists, or leave them `undefined` (the existing render
   code already guards both with `!== undefined` checks, so `undefined` is safe — just won't show
   anything for those fields, which is honest given no data exists to show).
5. Keep `block.props.items` in the type/schema for backward compat if any other code path still
   reads it, but the render path for the expanded list should use live data, not this field, going
   forward. Confirm with a grep whether `items` is read anywhere else before removing it outright.

## Verification

- Manually check a tenant with 3+ real, active, shoppable-type `Location`s and a consumer account
  joined to that tenant's loyalty network — the `storeList` block should now show all of them
  (up to `limit`), matching what the "Оберіть зручний магазин" picker on the same screen already
  shows correctly.
- Confirm changing `limit` in the App Builder now actually changes how many stores render.
- No backend or web changes needed for this fix — confirm you don't need to touch
  `LoyaltyService.cs`, `ConsumerLoyaltyController.cs`, or anything under `frontend/features/consumer-app/`.

## Related files (read-only reference, not to be duplicated)

- `mobile/app/(personal)/index.tsx` — the working sibling picker, exact pattern to mirror.
- `mobile/features/loyalty/selection.ts` — `mergeStoreNetworks`, already-proven-correct merge logic.
- `mobile/features/loyalty/hooks/useLoyalty.ts` — `useAvailableNetworks`, `useMemberships`.
- `backend/ShelfGuard.Application/Features/Loyalty/LoyaltyService.cs:343-356` — server-side store
  filtering (`IsActive && IsShoppableStoreType`), already correct, not the bug.
