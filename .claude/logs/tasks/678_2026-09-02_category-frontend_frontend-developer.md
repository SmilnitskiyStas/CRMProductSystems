# TASK-678 — B3: category frontend (form picker + nested filter + provider management page)

**Status:** review (not committed) · **Agent:** frontend-developer · Plan `.claude/plans/1-giggly-catmull.md` (Частина B / B3)
Builds on B1 (TASK-675/676) + B2 (TASK-677) — all uncommitted in the working tree.

## What changed

**Shared helpers**
- `frontend/features/inventory/lib/categoryTree.ts` (new) — `flattenTree(CategoryDto[])` → depth-first
  tree-ordered `{category, depth}[]` + `indentLabel(name, depth)` (`"— ".repeat(depth)` prefix).
- `frontend/features/provider/lib/categoryTree.ts` (new) — `PlatformCategoryDto` variants:
  `buildChildrenMap`, `flattenPlatformTree`, `subtreeIds` (self+descendants, for parent-select
  cycle exclusion), `indentLabel`. Siblings sorted by (sortOrder, name).

**Deliverable 1 — category picker in product form** (`features/inventory/components/ProductForm.tsx`)
- zod schema `+ categoryId: z.string().optional()`; default `""`; reset → `product?.categoryId ?? ""`;
  `onSubmit` → `categoryId: values.categoryId || undefined`.
- New native `<select>` after Unit, before ManagementType/ItemType — leading `— {categoryNone} —`
  (value `""`) then `flattenTree(useCategories())` indented by depth.

**Deliverable 2 — inventory filter: nested + "без категорії"** (`app/(dashboard)/inventory/page.tsx`)
- Filter `<select>`: `""`=allCategories, `"__none__"`=uncategorized sentinel, then flattened tree.
- Query: `category_id: categoryId && categoryId !== "__none__" ? categoryId : undefined`,
  `uncategorized: categoryId === "__none__" ? true : undefined`.
- `features/inventory/api/products.ts` `getAll` — `+ uncategorized?: boolean` param →
  `qs.set("uncategorized","true")`.
- `features/inventory/hooks/useProducts.ts` `ProductsListParams` — `+ uncategorized?: boolean`
  (spread through to query key + `productsApi.getAll` already handled it).

**Deliverable 3 — provider category management page**
- `app/(dashboard)/provider/categories/page.tsx` (new) — `"use client"`, `PROVIDER_ROLES` guard
  (mirrors `provider/page.tsx`, redirect → `/dashboard`), header matching provider page style,
  renders `<CategoryTreeManager>` + `<CategoryFormModal>` (create / create-sub / edit modal state).
- `features/provider/api/providerCategories.ts` (new) — `list/create/update/remove` flat object.
- `features/provider/hooks/useProviderCategories.ts` (new) — `useProviderCategories` (key
  `["provider","categories"]`, staleTime 60s, retry:false, try/catch→[]), `useCreateCategory` /
  `useUpdateCategory` / `useDeleteCategory` — each invalidates `["provider","categories"]` **and**
  `["categories"]` (tenant dropdown).
- `features/provider/components/CategoryTreeManager.tsx` (new) — nested tree from `parentId`,
  expand/collapse, per row: name, `itemCount` badge, business-type chips, inactive pill,
  Add-sub / Edit / Delete icon buttons. Delete → confirm dialog → `useDeleteCategory`; 400
  "has sub-categories" surfaced via `toast.error(e.message)`. Empty state.
- `features/provider/components/CategoryFormModal.tsx` (new) — `components/ui/Modal`; Name (req),
  Parent (`<select>` of flattened tree minus self+descendants when editing), Business types
  (checkboxes from `ALL_BUSINESS_TYPES`, labels via `Dashboard.provider.businessTypes`, empty →
  "all business types" hint), Sort order (number), Active (checkbox, edit only). Submit →
  `mutateAsync` + `toast.success` / `toast.error(e.message)`, close on success.
- `features/provider/types.ts` — `+ PlatformCategoryDto / CreateCategoryBody / UpdateCategoryBody`.
- `components/layout/Sidebar.tsx` — `+ FolderTree` import; new `"admin"`-group item
  `/provider/categories` (`roles: PROVIDER_ONLY, permission: "admin_panel"`) between `/provider`
  and `/admin`.

**i18n** (`messages/{uk,en}.json`, key parity kept — 5333 == 5333, 0 diff)
- `Dashboard.sidebar.groups.admin.categories`
- `Dashboard.inventory.page.uncategorized`
- `Dashboard.inventory.form.categoryLabel` + `.categoryNone`
- `Dashboard.providerCategories.*` (title/subtitle/addButton/empty/tree labels/itemCountBadge
  plural/deleteDialog.*/toasts.*/modal.*)

## Verification
- `npx tsc --noEmit` (frontend) — clean.
- `npm run lint` — clean (no warnings/errors).
- uk/en key parity — 5333 keys each, no keys only-in-one.
- `next build` — exit 0; `/provider/categories` compiled as a static route (○, 7.35 kB / 129 kB),
  `/inventory` 11.1 kB / 276 kB. No type/lint errors.
- Browser E2E (dev :3001/:5000, demo "Свіжий Кут") — all pass:
  - `manager@demo.local` (store_manager): product form → picked "Батончики шоколадные", saved,
    catalog Category column shows it (API confirms `categoryId`+`categoryName`); filter
    "Uncategorized" → 18 items; filter a category → 4 items; parent-category filter returns
    a sub-category's item (subtree expansion) — 1 product shown under "B3 Parent" while the
    item is in "B3 Child Renamed"; filter dropdown shows `— B3 Child Renamed` indented under
    `B3 Parent`; `auto_service`-only "B3 AutoOnly" absent from the retail filter.
  - `admin@shelfguard.local` (provider): `/provider/categories` renders (nav item in ADMIN
    group); created "B3 Parent" (`businessTypes:["retail"]`), added sub "B3 Child", renamed to
    "B3 Child Renamed"; delete "B3 Parent" → toast "Category has active sub-categories."
    (backend 400 surfaced); delete leaf → 204 + "Category deleted" toast, row shows Inactive
    pill. Edit modal prefills + shows Active checkbox.
  - `read_console_messages` on fresh tabs — no errors on `/inventory` or `/provider/categories`
    (stale MISSING_MESSAGE errors seen only on the pre-restart tab; gone on reload in a new tab).
  - Test data cleaned (products + platform_categories hard-deleted from dev DB; baseline 86
    active categories restored).

## Notes
- **Not committed** (concurrent sessions on `main`).
- Running `next build` in the shared working tree corrupted the already-running `frontend-dev`
  `.next` (`Cannot find module './1682.js'`); removed `.next` + restarted `frontend-dev` (new
  serverId) after the build. Future: build with a copy / only when dev is down.
- openapi.json regen still pending from B1/B2 (unchanged by this task).
