# BUG-015, BUG-016, BUG-017 — 2026-07-04 — frontend-developer

## BUG-015 — StoreSelector shown to provider role
`frontend/components/layout/TopBar.tsx` already used `TENANT_ROLES.has(userRole) && <StoreSelector />`
(TENANT_ROLES excludes provider/provider_admin/provider_agent and supplier_admin). No change needed —
fix was already in place.

## BUG-016 — Duplicate "Створити постачальника" button
Removed the button and `CreateSupplierModal` usage from `frontend/app/(dashboard)/marketplace/page.tsx`
(also dropped now-unused `useMe`/`PROVIDER_TEAM`/`useState` createModalOpen/`Btn` imports).
Deleted `frontend/features/marketplace/components/CreateSupplierModal.tsx` — no other callers found.
Backend `MarketplaceAdminController` / `AdminCreateSupplierAsync` left as-is (candidate for later cleanup,
not deleted per instructions). "+ Додати товар" flow on the supplier detail page was not touched.

## BUG-017 — Supplier detail page half-width
Removed `maxWidth: 900` from both wrapper `<div>` occurrences in
`frontend/app/(dashboard)/marketplace/[id]/page.tsx` (loading state + main render).

## Verification
- `npx tsc --noEmit` — clean, no errors.
- `npm run build` — succeeded, all 40 routes generated.

## Files changed
- `frontend/components/layout/TopBar.tsx` (no change — verified only)
- `frontend/app/(dashboard)/marketplace/page.tsx`
- `frontend/app/(dashboard)/marketplace/[id]/page.tsx`
- `frontend/features/marketplace/components/CreateSupplierModal.tsx` (deleted)
