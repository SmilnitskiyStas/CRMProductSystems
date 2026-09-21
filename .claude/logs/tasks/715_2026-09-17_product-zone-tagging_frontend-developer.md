# TASK-715 — Product ↔ zone tagging (frontend)

**Status:** review · **Agent:** frontend-developer · not pushed

## What changed

Frontend half of the floor-plan canvas feature, consuming TASK-714's backend
(`GET/POST /api/items/{id}/zones`, `DELETE /api/items/{id}/zones/{zoneId}`). Lets a
merchandiser tag a catalog product with one or more store zones/counters from the
product edit form. TASK-716 (a shelf-canvas box referencing one of these tags) is a
separate follow-up, not started here.

- `frontend/features/inventory/types.ts` — new `ItemZone` interface mirroring `ItemZoneDto`.
- `frontend/features/inventory/api/products.ts` — `productsApi.getZones/assignZone/unassignZone`.
- `frontend/features/inventory/hooks/useProducts.ts` — `useItemZones`, `useAssignItemZone`,
  `useUnassignItemZone` (mutations invalidate `[...PRODUCTS_KEY, productId, "zones"]`).
- `frontend/features/inventory/components/ProductZonesSection.tsx` (new) — `useLocations()` +
  `useItemZones()`; removable chips ("Location — Zone"); add control is a location→zone
  cascading `<select>` pair (mirrors `AddBatchForm`'s cascade) filtered to active
  locations/zones and excluding already-tagged zones so the backend's 400 "already exists"
  is normally unreachable; still caught and shown inline if hit (e.g. concurrent-tab race).
  Zone options show the zone type via the existing `Dashboard.locations.zoneTypes` i18n map.
- `frontend/features/inventory/components/ProductForm.tsx` — new `CollapsibleSection`
  (`t("sectionZones")`) wrapping `ProductZonesSection`, gated on `product` (edit mode only —
  a new product has no id to tag yet).
- `frontend/app/(dashboard)/inventory/[id]/page.tsx` — read-only "Розміщення"/"Placement"
  `Section` in the Info tab listing the product's zone tags, each row linking to
  `/locations/{locationId}/zones/{zoneId}/shelves`. Incidentally starts using the `ExternalLink`
  icon that was imported but unused before this change.
- `frontend/messages/{uk,en}.json` — `Dashboard.inventory.form.sectionZones` +new sibling
  namespace `Dashboard.inventory.itemZones` (picker labels, empty/loading, add/remove errors,
  placement section title). Named `itemZones`, not `zones`, to avoid colliding in a grep with
  the pre-existing (unrelated) `Dashboard.inventory.analytics.zones` chart-band labels.

## Build/verification status

- `npx tsc --noEmit` (frontend): clean, 0 errors. (`node_modules` wasn't installed in this
  worktree — ran `npm install` first.)
- `npm run lint`: clean, 0 warnings/errors.
- Manual browser check was only partial: no Postgres/backend reachable in this environment
  (Docker Desktop not running, ports 5435/5000 refused connections), so I could not log in or
  see real data. What I did verify: started `frontend-dev` (port 3001), confirmed the landing
  page renders, and navigated directly to `/inventory` and `/inventory/[id]` — both routes
  compiled successfully through Next's dev bundler (2228 / 2243 modules, 0 build errors) and
  pulled in `ProductForm`/`ProductZonesSection`/`PlacementSection` without runtime errors
  (only expected `ERR_CONNECTION_REFUSED` console noise from the missing backend). I did **not**
  visually confirm the add/remove-chip interaction against real data — that needs someone with
  a running backend + seeded tenant to click through per the task's verification steps.

## Notes / follow-ups

- Did not touch any `backend/*.cs` file, `features/locations/`, or the shelf-canvas pages, per
  brief.
- TASK-716 (shelf-canvas box → zone-tagged product reference) is unimplemented, as instructed.
