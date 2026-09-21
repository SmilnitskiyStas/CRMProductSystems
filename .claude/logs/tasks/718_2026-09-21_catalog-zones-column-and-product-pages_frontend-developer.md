# TASK-718 — Catalog Zones column + create/edit modal → pages

**Status:** review · main session (frontend-developer) · not pushed

## What changed

1. **Zones column** on `/inventory`: `Product.zoneNames: string[] | null` added to
   `frontend/features/inventory/types.ts` (consumes TASK-717's paged-list field).
   `ProductsTable.tsx` — new `zones` column between `category` and `itemType`, `t("headers.zones")`,
   renders `"—"` or the joined list with a `title` tooltip + ellipsis truncation (no richer
   truncation primitive existed in `Table.tsx` to reuse). i18n key added to both `uk.json`/`en.json`.

2. **Product create/edit: modal → dedicated pages.** `ProductForm.tsx` stripped of its
   backdrop/centered-box/header-with-✕ wrapper — now returns just the `<form>` and everything
   inside it, unchanged. `Props`: dropped `open`/`onClose`, added `onCancel: () => void`. Verified
   via `grep` that `InventoryPage` was the only caller before refactoring.
   New routes: `app/(dashboard)/inventory/new/page.tsx` and `app/(dashboard)/inventory/[id]/edit/page.tsx`,
   both copying the back-button header pattern from `/inventory/[id]/page.tsx`. `inventory/page.tsx`
   lost `formOpen`/`editingProduct` state and the create/update wiring — "Add product" and the row
   Edit action now `router.push` to the new routes; delete flow untouched.
   `useProducts.ts` gained `useUploadProductImage` (mirrors `useUploadBannerImage`) — the old modal
   flow never actually wired `onImageUpload` to a mutation (dead prop, always `undefined`); the new
   edit page wires it for real, per the brief's explicit ask.

## Verification

- `npx tsc --noEmit`, `npm run lint`, `npm run build` — all clean (0 errors/warnings).
- `uk.json`/`en.json` — valid JSON after hand-edits.
- Full live browser E2E against local backend + dev Postgres (both had live data already):
  Zones column confirmed with real multi-zone data ("Молоко 2,5% Галичина" → 2 zones joined) and
  `"—"` for untagged products. Create page renders with no modal chrome, Cancel/Back → `/inventory`.
  Edit page loads and pre-fills a real product (barcode, category, pricing, stock, and the
  untouched `ProductZonesSection` showing its 2 existing zone tags), Cancel → `/inventory/{id}`.
  Save (PUT) → `200 OK` → toast + redirect to `/inventory/{id}`, confirmed via network log.
  Create (POST) consistently returned `403` for the logged-in Provider test account specifically
  (`AtLeastStoreManager` policy — PUT/GET succeeded for the same account, POST didn't; a backend
  authorization asymmetry unrelated to this change, out of scope per brief — backend untouched).
  Frontend request shape/payload for create was confirmed correct from the network log; only the
  full success-redirect couldn't be exercised without tenant-staff credentials. No console errors
  beyond the expected 401 (pre-login) / 403 (create, explained above).

## Untouched (per brief)

Backend, `ProductZonesSection.tsx`, `ProductSearchPicker.tsx`, shelf-canvas pages, `/inventory/[id]`
detail page content, the View/Analytics `DetailDrawer` in `ProductsTable.tsx`.
