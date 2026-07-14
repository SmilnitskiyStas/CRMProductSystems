# TASK-348 — TenantRole templates UI (ADR-020 frontend contract)

**Agent:** frontend-developer
**Date:** 2026-07-13
**Status:** done

## Scope

Frontend UI for the already-deployed ADR-020 backend (TASK-345/346/347): custom
capability-template roles (`TenantRole`), management CRUD, and per-user assignment.

## Changes

- New feature `frontend/features/tenant-roles/`:
  - `types.ts` — `TenantRoleDto`, `Create/UpdateTenantRoleRequest`,
    `TenantRoleCapabilityDto`/`Group`, `AssignTenantRoleRequest`.
  - `api/tenant-roles.ts` — CRUD for the 6 `TenantRolesController` endpoints
    (list/getById/capabilities/create/update/archive).
  - `hooks/useTenantRoles.ts` — `useTenantRoles(includeInactive, enabled)`,
    `useTenantRoleCapabilities`, `useCreateTenantRole`, `useUpdateTenantRole`,
    `useArchiveTenantRole`, `useAssignTenantRole` (calls `usersApi.assignTenantRole`
    since that endpoint lives on `UsersController`; patches the shared `users` query
    cache since the assign endpoint returns 204).
  - `components/TenantRolesTab.tsx` — management UI: list with capability/assigned-user
    counts (derived client-side from `useUsers()`, no new backend count endpoint),
    archive toggle to show/hide archived templates, create/edit modal with checkboxes
    grouped by specialty — group names and labels sourced live from
    `GET /api/tenant-roles/capabilities`, never hardcoded (verified: "HR",
    "Бухгалтер / Фінансист", "Закупка" render exactly as backend defines).
  - `components/TenantRoleSelector.tsx` — assignment dropdown (active templates +
    "Без шаблону"), calls `POST /api/users/{id}/tenant-role`.
  - `components/TenantRoleBadge.tsx` — pill showing the assigned template name;
    query is `enabled` only for enterprise_admin+ viewers (see Fix below).
- `frontend/app/(dashboard)/users/page.tsx` — added a "Користувачі" / "Шаблони ролей"
  tab switcher, second tab gated by `hasRole(me?.role, AT_LEAST_ENTERPRISE_ADMIN)`.
- `frontend/features/users/types.ts` — added `UserDto.tenantRoleId`.
- `frontend/features/users/api/users.ts` — added `assignTenantRole`.
- `frontend/features/users/components/UsersList.tsx` — `TenantRoleBadge` next to the
  role badge per row; refactored `selected` from a frozen snapshot to
  `selectedId` + live derivation from `useUsers()` (see Fix below).
- `frontend/features/users/components/UserDetailPanel.tsx` — `TenantRoleBadge` in the
  header; `<TenantRoleSelector>` in the "Доступ" tab, gated by
  `hasRole(me?.role, AT_LEAST_ENTERPRISE_ADMIN) && !isSelf` — independent of the
  existing `editorRank > targetRank` check (backend's `AssignTenantRoleAsync` has no
  rank comparison, so EA can assign a template to a peer EA too); tab itself now shows
  when either that or the existing permissions-editor rank check is true.

## Fixes found during live verification (not pre-existing, introduced/caught in this task)

1. `UsersList`'s `selected` detail-panel state was a frozen object snapshot captured at
   click time — an already-open panel's header badge (and any other live field) didn't
   reflect a mutation until closed/reopened. Changed to `selectedId` + derive `selected`
   from the live `useUsers()` array each render. Verified: assign/unassign while the
   panel stays open now updates the header badge immediately.
2. `TenantRoleBadge` unconditionally called `useTenantRoles(true)` — `GET
   /api/tenant-roles` is `AtLeastEnterpriseAdmin`-only, so every row for every
   non-admin viewer (e.g. store_manager browsing `/users`) fired a guaranteed 403 with
   React Query's default retries. Added `enabled` param to `useTenantRoles` (+
   `retry: false`) and gated the badge's own query on the viewer's role via `useMe()`.

## Verification

- `npx tsc --noEmit` — clean (checked twice, after the initial implementation and
  again after the two fixes above).
- `npm run build` — succeeded, all 51 routes generated (checked twice).
- Live-tested via local dev servers (`backend-dev` + `frontend-dev`, local Postgres —
  backend applied migration `20260713152826_AddTenantRoles` cleanly on startup):
  - As `ea@demo.local` (enterprise_admin): created template "HR" with capabilities
    `users.manage` + `schedules.manage` → `POST /api/tenant-roles` 201, group headers
    and labels matched backend `TenantRoleCapabilities.Groups` exactly. Assigned it to
    `manager@demo.local` (Олена Ткаченко) → `POST /api/users/{id}/tenant-role` 204 →
    "HR" badge appeared in both the list row and the open detail panel header
    immediately. Unassigned and reassigned to confirm both directions update live.
    Confirmed via `GET /api/auth/me` that Olena's session now carries
    `"capabilities":["users.manage","schedules.manage"]` — full JWT round-trip works.
  - As `manager@demo.local` (store_manager): "Шаблони ролей" tab and tab bar are
    completely absent from `/users` (not just visually hidden — never rendered, no
    `/api/tenant-roles` request fires at all). Own row shows no TenantRoleBadge
    (correct — backend won't let this role resolve template names). Opened a
    lower-ranked peer (Аліна Шевченко, merchandiser): "Доступ" tab visible
    (permissions editor, existing rank-based rule) but no TenantRoleSelector —
    confirms the EA-only gate is independent of and stricter than the rank check.
  - Note: `computer`/`zoom` (pixel screenshot) timed out repeatedly in this sandbox on
    both this task and TASK-344 — no visual screenshot captured. Full verification done
    via accessibility tree (`read_page`/`get_page_text`), network log inspection, and
    direct DOM/JS assertions instead.

## Files touched

- `frontend/features/tenant-roles/types.ts` (new)
- `frontend/features/tenant-roles/api/tenant-roles.ts` (new)
- `frontend/features/tenant-roles/hooks/useTenantRoles.ts` (new)
- `frontend/features/tenant-roles/components/TenantRolesTab.tsx` (new)
- `frontend/features/tenant-roles/components/TenantRoleSelector.tsx` (new)
- `frontend/features/tenant-roles/components/TenantRoleBadge.tsx` (new)
- `frontend/app/(dashboard)/users/page.tsx`
- `frontend/features/users/types.ts`
- `frontend/features/users/api/users.ts`
- `frontend/features/users/components/UsersList.tsx`
- `frontend/features/users/components/UserDetailPanel.tsx`
