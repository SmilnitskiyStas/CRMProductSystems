# TASK-307 — Supplier roles + task board (frontend), marketplace dead code cleanup

**Agent:** frontend-developer
**Date:** 2026-07-05
**Status:** done (frontend layer only — see handoff notes below)

## What was done

**Part 1 — dead code removal:**
- Removed `useCreateSupplier` (`marketplace/hooks/useMarketplace.ts`), `adminCreateSupplier`
  (`marketplace/api/marketplace-api.ts`), `CreateSupplierRequest` (`marketplace/types.ts`).
  Confirmed via grep no other frontend code referenced these symbols before deleting.

**Part 3 — supplier roles:**
- `frontend/lib/supplierPermissions.ts` — `SUPPLIER_PERMISSIONS` dict (catalog_management,
  client_reviews, task_board, staff_management, profile_management) + `resolveSupplierPermissions`
  helper, mirrors `providerPermissions.ts`.
- `supplier-cabinet/types.ts` — `SupplierRoleDto`, `CreateSupplierRoleRequest`,
  `UpdateSupplierRoleRequest`, plus task types (below).
- `supplier-cabinet-api.ts` — `getRoles/createRole/updateRole/deleteRole` against
  `/api/supplier-cabinet/roles`.
- `useSupplierCabinet.ts` — `useSupplierRoles`, `useCreateSupplierRole`, `useUpdateSupplierRole`,
  `useDeleteSupplierRole` (React Query, invalidate on mutation).
- New `RolesTab.tsx` — list + create/edit modal with permission checkboxes, delete with confirm;
  pattern copied from `provider/components/RolesSection.tsx`.
- `InviteStaffModal.tsx` — added optional role `<select>` sourced from `GET /roles`; sends
  `supplierRoleId` (undefined when "Повний доступ" is selected).
- `Sidebar.tsx` — `SUPPLIER_NAV_GROUP` items now carry a `permission` key each (profile_management,
  catalog_management, client_reviews, task_board, staff_management). Added
  `supplierEffectivePermissions` (built from `me.permissions` only when non-null — null means
  full/owner access, matches backend convention) and wired it into the existing item-filter
  predicate alongside the provider-team permission check.

**Part 4 — task board:**
- Task types + API methods (`getTasks` with query filters, `createTask`, `updateTask`,
  `updateTaskStatus`) added to the same files as above.
- Hooks: `useSupplierTasks(filters)`, `useCreateSupplierTask`, `useUpdateSupplierTask`,
  `useUpdateSupplierTaskStatus`.
- New `TasksBoard.tsx` — list view (not kanban, per plan's "not required if time-constrained"),
  Мої/Усі toggle, client-tenant-id text filter (no dedicated "clients of supplier" endpoint exists
  yet, per handoff — used a plain text input for `clientTenantId`), status filter dropdown,
  inline status change per row, create/edit modal (title, description, client tenant id, assignee
  from `GET /staff`, due date).

**Navigation restructuring:**
- Cabinet pages are separate routes (not tabs), so added two new pages:
  `/supplier/team` (staff panel + RolesTab, gated `staff_management`) and `/supplier/tasks`
  (TasksBoard, gated `task_board`). Moved `CabinetStaffPanel` out of `/supplier/profile` into
  `/supplier/team` since staff management is now its own permission-gated area distinct from
  profile editing.

## Verification

- `npx tsc --noEmit` — 0 errors.
- `npm run lint` — could not run: repo has no ESLint config file, and `next lint` in this
  Next.js version prompts interactively to create one (pre-existing condition, unrelated to this
  change).
- Dev server smoke test: `/supplier/team`, `/supplier/tasks`, `/supplier/profile` (edited) all
  compiled cleanly, no console errors, no server errors. Could not test authenticated behavior
  (no local backend running / no supplier_admin session available in this environment).

## For QA / next steps

- Backend migrations for `supplier_roles`, `supplier_tasks`, `users.supplier_role_id` were **not
  applied** to any database as of TASK-306 handoff — real data won't load until
  `dotnet ef database update` is run. Expect 404/500 from these new endpoints until then.
- Client-tenant selection in `TasksBoard` is a raw text input for tenant id (no supplier→client
  relationship endpoint exists on the backend yet) — acceptable per plan ("не over-engineer").
