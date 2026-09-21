# TASK-716 — Shelf-canvas box → linked catalog product (frontend)

**Status:** review · **Agent:** frontend-developer · not pushed

## What changed

Second half of the floor-plan feature: a placed shelf/counter box on the per-zone
shelf builder can now link to a real catalog product, with a live stock-status dot.
Builds on TASK-714 (backend `/api/items/{id}/zones`) and TASK-715 (product↔zone
tagging on the product form, already merged in this worktree).

- `frontend/features/locations/types.ts` — `ShelfItemPlacement.itemId?: string | null`
  (optional, so old saved plans without it keep working).
- `frontend/features/locations/hooks/useFloorPlan.ts` — `useZoneItemStatusCounts(locationId,
  zoneId)`, sibling of `useZoneStatusCounts`, grouped by `productId` instead of `zoneId`.
- `frontend/features/inventory/components/ProductSearchPicker.tsx` (new) — generalized from
  `features/events/components/EventProductPicker.tsx`; same debounced-search behavior, new
  `Dashboard.inventory.productPicker` i18n namespace instead of the events one.
- `frontend/app/(dashboard)/locations/[id]/zones/[zoneId]/shelves/page.tsx` — per-row
  link/change/unlink controls in the Sections side panel (inline-expanding
  `ProductSearchPicker`, no modal — matches this page's existing plain-style layout and the
  events feature's own inline-picker precedent); resolved product name + `STATUS_CONFIG`
  status dot on both the side-panel row and the canvas `ShelfItemBox` (presentation-only —
  drag/resize untouched); `handleSave` fires `productsApi.assignZone` for every linked item
  after the layout PUT succeeds, invalidates that item's zones query, and swallows a 400
  ("already tagged") silently while still surfacing any other failure via toast. Unlinking a
  box (or deleting it) never auto-removes the zone tag — intentional asymmetry per brief,
  untagging stays a manual action in `ProductZonesSection`.
- `frontend/messages/{uk,en}.json` — `Dashboard.locations.shelvesPage.{linkProduct,
  changeProduct,unlinkProduct,cancelLinking,zoneSyncError}` + new namespace
  `Dashboard.inventory.productPicker.{placeholder,hint,loading,empty}`.

## Build/verification status

- `npx tsc --noEmit` (frontend): clean.
- `npm run lint` (eslint, changed files): clean.
- `npm run build`: fails during "Collecting page data" with `PageNotFoundError: Cannot find
  module for page: /_document` — an internal Next.js/environment quirk (no `pages/` dir exists
  in this project; App Router only), reproduced after a full `.next` cache clear, unrelated to
  any file this task touched. "Compiled successfully" + full type-check both passed *before*
  hitting it.
- Manual verification: full live pass, both frontend-dev and backend-dev were reachable.
  Logged in (`ea@demo.local`/store_manager session already active), placed a zone, opened its
  Shelf Builder, added two sections, linked two different products via the new picker — name +
  status dot rendered on canvas and in the side panel, "Change product"/"Unlink product"
  controls appeared correctly. Saved, reloaded the page cold, confirmed `itemId` round-trips
  through `zone.position` JSON. Verified both `assignZone` outcomes over the network: a
  never-before-tagged product got `201 Created`; a product already tagged to that zone (from
  earlier in this same test) got `400 Bad Request` and was silently swallowed — no error toast,
  no console exception. Unlink verified client-side (clears display, no unassign call fired).
  Did not reach `ProductZonesSection` itself (the logged-in demo role has no product-edit
  access) — the `201 Created` network response is direct evidence the same endpoint that
  section reads from was written correctly.

## Notes / follow-ups

- Pre-existing, unrelated issues noticed while testing (not fixed, out of scope): missing
  i18n key `Dashboard.locations.types.shop` (raw key shown on the Locations list for `shop`-type
  locations); a few stray 401s from an unrelated background poll.
- Did not touch backend `.cs` files, `features/inventory/api/products.ts`,
  `useProducts.ts`, `ProductZonesSection.tsx`, `ProductForm.tsx`, or `FloorPlanCanvas.tsx`
  (only imported `STATUS_CONFIG`/`worstStatus` from the last one), per brief.
