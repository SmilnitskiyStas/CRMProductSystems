# Follow-up: `storeList` block still not showing all stores — duplicate resolution logic found

**From:** main Claude session (backend + web + diagnosis), follow-up to
`.claude/logs/handoffs/602-to-mobile-codex_storelist-block-static-data.md`.
**Context:** that handoff's diagnosis was based on reading `CoreBlocks.tsx` alone and was
**incomplete** — it never checked `resolveBlocks.ts`, and wrongly concluded `storeList.items` was
purely static/curated config. This document corrects that and explains what's actually happening
in the current (uncommitted) working tree, and what to do about it.

## What's already in the working tree right now (uncommitted)

Since the original handoff, changes have landed in:
- `mobile/features/consumer-content/hooks.ts` — `useSelectedConsumerContext` now falls back to
  the first active membership when `selectedTenantId` isn't set yet.
- `mobile/features/loyalty/selection.ts` — new `mergeStoreNetworks` and `selectNetworkStores`
  helpers.
- `mobile/features/server-driven-ui/PageRenderer.tsx` — now computes `network` via
  `mergeStoreNetworks(networks.data, membershipsQuery.data)` instead of a plain `networks.data.find(...)`.
- `mobile/features/server-driven-ui/resolveBlocks.ts` — `storeList` case now also passes `limit`
  into the resolved props (the `items` derivation from `data.network?.stores` was **already there
  before any of this**, unchanged).
- `mobile/features/server-driven-ui/blocks/CoreBlocks.tsx` — `StoreListBlock` was rewritten to
  stop using `block.props.items` entirely and instead independently call its own
  `useAvailableNetworks`/`useSelectedConsumerContext` and `selectNetworkStores(...)`.
- `mobile/features/server-driven-ui/blocks/types.ts` — `StoreListProps` gained `limit?: number`.

Reported result: still not displaying correctly on the phone.

## The actual root cause (corrected diagnosis)

`resolveBlocks.ts`'s `resolvePage()` **already had a working mechanism** for `storeList` before
any of today's changes — it derives `items` from `data.network?.stores` (the `network` object
`PageRenderer.tsx` computes and passes into `resolvePage`'s data sources). `StoreListBlock`
consuming `block.props.items` was therefore *supposed to* already be receiving live, resolved
store data — not curated/static config, contrary to the original handoff's claim.

The real, original bug was almost certainly upstream, in the **old** `PageRenderer.tsx`:
```ts
// old
const network = networks.data?.find((item) => item.tenantId === membership?.tenantId) ?? null;
```
If `membership` resolved to `null`/`undefined` (e.g. `useLoyaltyUiStore`'s `selectedTenantId` is
unset on first load, and the old `selectMembershipForTenant` had no fallback), `network` was
always `null`, so `resolveBlocks.ts`'s `data.network?.stores ?? []` produced an empty/incomplete
list regardless of how many real `Location`s the tenant actually has.

**The `useSelectedConsumerContext` fallback + `PageRenderer.tsx`'s `mergeStoreNetworks` fix
already landed address exactly this** and, on their own, should very plausibly have been
sufficient to fix the bug through the pre-existing `resolveBlocks.ts` pipeline — no changes to
`StoreListBlock` itself should have been necessary.

## The problem: duplicate, competing resolution paths now coexist

`StoreListBlock`'s rewrite makes it **ignore `block.props.items` entirely** (the value
`resolveBlocks.ts` already correctly computes, post-fix) and instead redo the same computation a
second time, independently, via its own local hook calls (`useAvailableNetworks`,
`useSelectedConsumerContext`, `selectNetworkStores`). This is now genuinely duplicated logic with
two separate code paths that must both be correct and stay in sync:

1. `PageRenderer.tsx` → `resolveBlocks.ts` → `block.props.items` (already fixed, likely correct
   now).
2. `StoreListBlock`'s own internal fetch + `selectNetworkStores(...)` (brand new, unverified,
   and — since the bug is still reproducing — the more likely remaining fault line, simply
   because it's the newest, least-tested code).

This duplication is a maintenance risk on its own even if it were working today: two
implementations of "resolve this consumer's network stores" that can silently drift apart.

## Recommended fix

**Revert `StoreListBlock` back to consuming `block.props.items` directly** (its pre-rewrite
form), and rely solely on the `PageRenderer.tsx` + `useSelectedConsumerContext` fix to make sure
`block.props.items` arrives already correct. Concretely:

1. In `mobile/features/server-driven-ui/blocks/CoreBlocks.tsx`, `StoreListBlock`:
   - Remove the `useAvailableNetworks` call, the `selectNetworkStores` call, and the `stores`
     local variable.
   - Go back to reading `block.props.items` for the expanded list's `.map(...)`, the summary count
     (`block.props.items.length`), and the empty-state check — i.e., undo this component-level
     part of the diff specifically, while **keeping** the `toggleStoreList`/`membershipsQuery`
     refetch-on-open behavior if that part is independently useful (it doesn't depend on the
     duplicated resolution).
   - Since `resolveBlocks.ts` already applies `limit` when building `items` (`.slice(0, limit)`
     before this ever reaches the component), the component doesn't need to slice by
     `block.props.limit` itself either.
   - `distanceKm`/`openNow`: these were correctly identified as unrenderable (no data source
     exists) — that part of the rewrite is fine to keep; `resolveBlocks.ts`'s own `items` mapping
     already omits them (only maps `id`/`name`/`address`), so nothing to change there either.

2. Keep the genuinely-fixed upstream pieces as-is:
   - `useSelectedConsumerContext`'s active-membership fallback (`hooks.ts`).
   - `PageRenderer.tsx`'s `mergeStoreNetworks(networks.data, membershipsQuery.data)` computation
     for `network`.
   - `resolveBlocks.ts`'s `limit` passthrough (harmless, keeps the prop consistent even though the
     reverted component won't read it directly from `block.props.limit` — leave it, no reason to
     remove).

3. After reverting the component, test again on a real device/build with a tenant that has 3+
   active, shoppable-type `Location`s and a consumer account with a membership in that tenant.
   If it *still* doesn't show all 3, the bug is confirmed to be upstream in
   `PageRenderer`/`resolveBlocks`/the network-fetch itself, not in the component — at that point,
   add logging at `PageRenderer.tsx`'s `network` computation (log `networks.data`,
   `membershipsQuery.data`, `membership?.tenantId`, and the resulting `network`) to see exactly
   where the chain breaks. The main Claude session cannot attach to a running device/simulator to
   verify this directly — this step needs to happen on your end.

## Why not just fix `StoreListBlock`'s new code instead of reverting it

Possible, but reverting to the pre-existing, simpler, already-proven pattern (`resolveBlocks.ts`
already does this exact job, and does it once per page render rather than once per block
instance) is lower-risk and removes an entire duplicate code path instead of debugging two
parallel implementations. If there's a reason the component-level fetch is actually needed (e.g.
the block needs to refetch on open without re-rendering the whole page) that this note is missing,
that's a judgment call for whoever picks this up — but the default recommendation is: one source
of truth, and the one that already existed and required only an upstream fix.
