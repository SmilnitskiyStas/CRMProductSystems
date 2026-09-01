# TASK-667 — supplier profile forms (read-only single category + collapsible sections + region label) + supplier-category selector at tenant creation

**Status:** done (committed to main) · **Agent:** frontend-developer · Frontend only.
Consumes TASK-665 (backend, `de8f1632`) + TASK-666 (`f5036244`).

## What changed

### New `frontend/components/ui/CollapsibleSection.tsx`
Generic dark-theme collapsible panel. Props `{ title: string; defaultOpen?: boolean; children }`,
`defaultOpen` defaults to `true`. Header row = lucide `ChevronDown`/`ChevronRight` + title
(`#9CA3AF`, 12px/600), `aria-expanded`; body hidden when collapsed, `#1F2937` border, no bg
coupling. No prior equivalent existed in `components/ui/` (checked).

### `CabinetProfileForm.tsx` + `SupplierProfileForm.tsx`
- **Category → read-only single.** Removed the categories checklist (Cabinet) / `TagInput`
  (Marketplace — component + its `KeyboardEvent` import deleted). Now a muted read-only line:
  `itemCategories.find(c => c.key === (profile.categories ?? [])[0])?.labelUa`, else `categoryNone`.
  `categories` no longer in either update-mutation payload (`SupplierProfileForm.handleSave`
  now builds an explicit body).
- **Collapsible sections** (all `defaultOpen`): `sectionGeneralLabel` (origin region + website),
  `categoryReadonlyLabel` (read-only category), `deliveryCoverageLabel` (`DeliveryCoverageEditor`),
  `sectionScheduleLabel` (working hours + payment terms). Save button, publish toggle / is-public
  toggle, plan selector stay outside all collapsibles.
- **Region field** relabelled `regionLabel` "Регіон / область" → **"Регіон відправлення"** /
  "Origin region", + new `regionHint` "звідки здійснюється доставка" / "where deliveries are
  dispatched from" (i18n key kept, value changed). Local `Field` helper in `CabinetProfileForm`
  gained an optional `hint`.

### `CreateTenantWizard.tsx` (provider)
Step 2: when `businessType === "supplier"` a **single-select radio list** "Категорія товарів"
from `useItemCategories()` (4 options) renders under the business-type grid. New
`supplierCategory` state; cleared when a non-supplier type is picked. `canGoStep3` now also
requires `supplierCategory` for suppliers → Next is blocked without it; final submit button
gated the same way. `createTenant.mutateAsync({ …, ...(isSupplier && supplierCategory ?
{ supplierCategory } : {}) })`.

### `CreateTenantModal.tsx` (admin)
When `form.businessType === "supplier"` a `<select>` "Категорія товарів" (options from
`useItemCategories()`) renders below the supplier hint. `supplierCategory` lives in the
`CreateTenantRequest` form state (`""` default); `handleSubmit` requires it for suppliers and
sends `supplierCategory: isSupplier ? form.supplierCategory : undefined`.

### Types
- `features/provider/types.ts` — `CreateTenantRequest += supplierCategory?: string`
- `features/admin/types.ts` — `CreateTenantRequest += supplierCategory?: string`
- `features/marketplace/types.ts` — `SupplierProfileUpdateRequest.categories` now optional
  (GET still returns it; update ignores it)
- api/hook layers pass the request object straight through — no change needed.

### i18n (both `uk.json` + `en.json`)
`Dashboard.marketplace.profileForm` & `Dashboard.supplierCabinet.profileForm`:
`sectionGeneralLabel`, `sectionScheduleLabel`, `regionHint`, `categoryReadonlyLabel`,
`categoryNone` (+ `regionLabel` value changed).
`Dashboard.provider.createTenantWizard`: `supplierCategoryLabel`, `supplierCategoryHint`.
`Dashboard.admin.createTenantModal`: `supplierCategoryLabel`, `supplierCategoryPlaceholder`,
`supplierCategoryHint`.
Category option text itself comes from the API (`labelUa`) — not keyed.

## Checks
`npx tsc --noEmit` clean · `npm run lint` clean · `npx vitest run` 59/59 · uk⇄en deep-key
parity 0 drift.

## Verification
Interactive — local `next dev` (:3001) against a from-source `dotnet run` backend (:5050,
`crmproductsystems-postgres-1` dev DB), seed users.
- **Provider `/provider` → New client wizard:** step 2, picking "Supplier" reveals the
  radio "Product category" list (4 opts from `useItemCategories`); Next stays disabled until
  a category is picked, then enables. Completed creation → tenant `businessType="supplier"`;
  `supplier_profiles.Categories = ["food"]` in the DB (matched the picked "Продукти харчування").
- **Supplier `/supplier/profile` (`CabinetProfileForm`):** 4 collapsible sections all open by
  default (General / Product category / Delivery regions / Schedule & payment); "Delivery
  regions" header collapses/expands on click; category shows as read-only text ("Автозапчастини");
  region labelled "Origin region" + hint "where deliveries are dispatched from"; Save button
  outside all sections; "Save profile" → PUT 200, no `categories` in payload, stored category
  preserved. (Category set for the test supplier via `PUT /api/provider/tenants/{id}/
  supplier-category` since the seed value `["dairy"]` predates the 4-category registry.)
- **Admin `CreateTenantModal`:** verified statically only — the component is exported but has
  **no render site** anywhere in `app/` (dead-but-in-scope; change mirrors the wizard).

Static: `npx tsc --noEmit` clean · `npm run lint` clean · `npx vitest run` 59/59 · uk⇄en
deep-key parity 0 drift.

Dev-DB side effects (verification only): created tenant "TASK-667 Verify Supplier"
(`7f126a7e-…`); changed `alpha@supplier.local` supplier category `dairy` → `auto_parts`.
