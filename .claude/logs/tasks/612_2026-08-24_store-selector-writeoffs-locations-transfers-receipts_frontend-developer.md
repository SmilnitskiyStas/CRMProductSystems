# TASK-612 — Wire global store selector into Write-offs, Locations, Transfers, Receipts

**Agent:** frontend-developer
**Date:** 2026-08-24
**Status:** done

## Bug

Selecting one store in the global header selector had no effect on Write-offs, Locations,
Transfers, and Receipts — all four kept showing every store's records.

## Changes

- **Write-offs** (`frontend/app/(dashboard)/write-offs/page.tsx`) — removed the local
  URL-param-driven `storeIdFilter` state and its client-side `.filter()` in the
  `filteredWriteOffs` `useMemo` (dead code predating the global selector, same pattern already
  retired in the sales/orders/ai-orders refactors). Wired `usePrimaryStoreId()` straight into
  `useWriteOffs({ store_id: primaryStoreId, status: statusFilter || undefined })` — server-side
  filter now, matching `useWriteOffs`/`writeOffsApi`/`WriteOffsController` which already
  supported `store_id`. Grepped the whole `frontend/` tree for `write-offs?store_id` and any
  `/write-offs?` link — no hits, so nothing deep-links to that param; safe to remove outright.
  Also dropped the now-unused store filter chip UI and `chipBtnStyle`.
- **Locations** (`frontend/app/(dashboard)/locations/page.tsx`) — this page's rows ARE the
  store list, so "filter by selected store(s)" is a pure client-side id filter with no backend
  involved. Added a `filteredLocations` `useMemo` reading the full `selectedStoreIds` array via
  `useStoreContext((s) => s.selectedStoreIds)` (multi-select supported here specifically because
  it's free — no extra request, no backend change): empty selection shows all (unchanged
  default), non-empty keeps only locations whose `id` is in the selection.
- **Transfers** (`frontend/app/(dashboard)/transfers/page.tsx`) — passed
  `usePrimaryStoreId()` into `useTransfers({ store_id: primaryStoreId, status: statusFilter ||
  undefined }, access === true)`. Backend semantics unchanged (`FromStoreId == storeId ||
  ToStoreId == storeId`).
- **Receipts** (`frontend/app/(dashboard)/receipts/page.tsx`) — passed `usePrimaryStoreId()`
  into `useReceipts({ store_id: primaryStoreId, status: statusFilter || undefined }, access ===
  true)`. Backend semantics unchanged (`DestinationStoreId == storeId`).

No backend, hook, or API-client changes — all three non-Locations backends already accepted
`store_id`; only the page-level wiring was missing.

## Verification

- `cd frontend && npx tsc --noEmit` — clean, no errors.
- `dotnet build ShelfGuard.Api` — clean.
- Ran both dev servers, logged in, exercised all 4 pages with `read_network_requests` /
  `get_page_text` while toggling the persisted store-selector state
  (`localStorage['shelfguard-selected-store']`):
  - Write-offs: single store → request `?store_id=<id>`, list narrowed from 7 rows (both stores)
    to 1 row (single-store test) / 6 rows (other single-store test) matching that store only.
  - Transfers: single store → request `?store_id=<id>`, rows shown are exactly the ones where
    that store is `from` or `to`.
  - Receipts: single store → request `?store_id=<id>`, rows narrow to that store's receipts.
  - Locations: single store selected → row list shrinks to just that location, **no** new
    network request fired (confirmed client-side only).
  - All 4 pages: cleared selection (empty array) → full list returns, matching pre-fix
    "all stores" behavior — no regression.
  - No new console errors from any of the 4 pages. (Console showed a stale 401 + a pre-existing
    `MISSING_MESSAGE: Dashboard.locations.types.shop` i18n warning, both reproduced before my
    changes too — unrelated to this fix, left untouched per scope.)

## Notes

- Write-offs URL-param deep-link (`?store_id=`) was safely removable — no in-repo links to it.
- Multi-select stayed scoped to Locations only, per the brief; the other 3 pages remain
  single-store (`usePrimaryStoreId()`), backend untouched.
