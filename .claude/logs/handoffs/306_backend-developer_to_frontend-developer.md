# Handoff: Supplier roles + task board endpoints, marketplace admin cleanup → Frontend

**From:** backend-developer (TASK-306)
**To:** frontend-developer
**Date:** 2026-07-05
**Plan:** `calm-singing-marble.md` Parts 1 (frontend half), 3 (frontend half), 4 (frontend half)

## What was removed (delete the corresponding dead/broken frontend code)

- `POST /api/admin/marketplace/suppliers` no longer exists (410-equivalent — the route is gone, 404 from
  routing). Delete the frontend code that calls it:
  - `useCreateSupplier` (`frontend/features/marketplace/hooks/useMarketplace.ts:142-150`)
  - `adminCreateSupplier` (`frontend/features/marketplace/api/marketplace-api.ts:73-75`)
  - `CreateSupplierRequest` type (`frontend/features/marketplace/types.ts:166+`)
  This was already dead code (no UI called it), per the plan.

## New endpoints — Supplier roles (`/api/supplier-cabinet/roles`)

All under `[Authorize(Policy = AppPolicies.SupplierCabinet)]` (same auth as the rest of the cabinet —
supplier_admin role, `marketplace_supplier` module required). No supplier id in the URL — always scoped
to the caller's own tenant via JWT `tenant_id` claim.

```
GET    /api/supplier-cabinet/roles           → SupplierRoleDto[]
POST   /api/supplier-cabinet/roles           → 201 SupplierRoleDto | 400 { error }
PUT    /api/supplier-cabinet/roles/{id}      → 200 SupplierRoleDto | 400 { error }
DELETE /api/supplier-cabinet/roles/{id}      → 204 | 400 { error }
```

```ts
type SupplierRoleDto = {
  id: string;
  displayName: string;
  baseRole: string;        // always "supplier_admin" for now — no other supplier base role exists
  permissions: string[];   // subset of SUPPLIER_PERMISSIONS keys, see below
  isSystem: boolean;       // no system roles are seeded yet — always false in practice
};

type CreateSupplierRoleRequest = {
  displayName: string;
  baseRole: string;        // must be "supplier_admin"
  permissions: string[];
};

type UpdateSupplierRoleRequest = CreateSupplierRoleRequest; // same shape
```

Valid permission strings (mirror `backend/ShelfGuard.Domain/Constants/SupplierPermissions.cs`, build
`frontend/lib/supplierPermissions.ts` analogous to `providerPermissions.ts`):
```
catalog_management   — items tab
client_reviews       — reviews tab
task_board           — tasks tab (see below)
staff_management     — staff tab + role management
profile_management   — company profile tab
```
Backend does **not** enforce these per-endpoint (same convention as provider permissions) — it's a
UI-level nav/tab filter against `user.permissions` from `/api/auth/me`, exactly like `Sidebar.tsx:377-421`
does today for the provider nav group. `SUPPLIER_NAV_GROUP` (currently unfiltered, ~line 421) needs the
same treatment.

Error strings you'll see in `{ error }` (400): "Display name is required.", "Base role '...' is invalid...",
"Unknown permission(s): ...", "Role '...' already exists.", "Role not found.", "System roles cannot be
edited/deleted.", "Cannot delete a role currently assigned to staff. Reassign them first."

## Staff invite — role selection now supported

`POST /api/supplier-cabinet/staff` request body gained an optional field:
```ts
type CabinetInviteStaffDto = {
  email: string;
  fullName: string;
  password: string;
  supplierRoleId?: string;   // NEW, optional — omit for full access (old behavior, unchanged)
};
```
If `supplierRoleId` is provided and doesn't resolve to a role of the caller's tenant, you get
`400 { error: "Role not found." }`. Wire a role dropdown (sourced from `GET /roles`) into the existing
invite-staff form/modal in the supplier cabinet.

## New endpoints — Supplier task board (`/api/supplier-cabinet/tasks`)

```
GET  /api/supplier-cabinet/tasks?assignedToMe=bool&clientTenantId=guid&status=string
     → SupplierTaskDto[] | 400 { error }   (all query params optional)
POST /api/supplier-cabinet/tasks           → 201 SupplierTaskDto | 400 | 404 (cabinet not available)
PUT  /api/supplier-cabinet/tasks/{id}      → 200 SupplierTaskDto | 400 | 404 "Task not found."
PUT  /api/supplier-cabinet/tasks/{id}/status → 200 SupplierTaskDto | 400 | 404 "Task not found."
```

```ts
type SupplierTaskDto = {
  id: string;
  clientTenantId: string | null;
  clientTenantName: string | null;   // denormalized display name, read-only
  assignedToUserId: string | null;
  assignedToUserName: string | null; // denormalized display name, read-only
  title: string;
  description: string | null;
  status: "pending" | "in_progress" | "completed" | "cancelled";
  dueDate: string | null;    // ISO date
  createdByUserId: string | null;
  createdAt: string;         // ISO datetime
  completedAt: string | null; // set automatically when status becomes "completed", cleared otherwise
};

type CreateSupplierTaskRequest = {
  title: string;
  description?: string | null;
  clientTenantId?: string | null;
  assignedToUserId?: string | null;   // must belong to the caller's own tenant (staff list)
  dueDate?: string | null;
};

type UpdateSupplierTaskRequest = CreateSupplierTaskRequest; // same shape, full replace on PUT

type UpdateSupplierTaskStatusRequest = { status: string }; // one of the 4 values above
```

Notes for the `TasksBoard.tsx` component:
- "assignedToMe" filter = `assignedToMe=true` (compares against the JWT-resolved caller's own user id
  server-side — you don't need to pass a user id yourself).
- Client dropdown: no dedicated "clients of this supplier" endpoint was built — plan suggested sourcing
  it from existing reviews/orders or a generic client-tenant list; that data-sourcing decision is yours.
  `clientTenantId` in the request/filter just needs to be *a* tenant id; backend does not validate it
  belongs to a real B2B relationship (only that it round-trips for filtering/display).
- Assignee dropdown: use the existing `GET /api/supplier-cabinet/staff` list.

## Gate for the Tasks tab

Same UI-level convention as roles — permission key `task_board`. `staff_management` gates the
roles/staff management UI.

## Verify before wiring up

Backend `dotnet build`/`dotnet test` are green (575 tests). Migrations for `supplier_roles`/
`supplier_tasks`/`users.supplier_role_id` exist but were **not applied to any database** in this
session (no reachable dev DB) — confirm with whoever runs the dev stack that `dotnet ef database update`
has been run before you start manual testing against these endpoints, otherwise you'll get 500s.
