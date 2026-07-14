# TASK-349 — InviteUserModal: pick a TenantRole template at invite time (ADR-020 UX gap)

**Agent:** frontend-developer
**Date:** 2026-07-13
**Status:** done

## Scope

UX-only fix, no backend changes. A newly created custom TenantRole template (ADR-020,
TASK-345/346/348) never appeared in "Запросити користувача" — the modal's `INVITE_ROLES`
was a hardcoded 4-role list, and template assignment only existed post-creation via
`TenantRoleSelector` in `UserDetailPanel`. Orchestrated the two already-shipped endpoints
from the frontend instead of touching `InviteAsync`/`UsersController.Invite` (explicitly
out of scope — hardened today under TASK-346/347 privilege-escalation review).

## Changes

- `frontend/features/users/components/InviteUserModal.tsx`:
  - `INVITE_ROLES` gains `"staff"` (ADR-020 base tier, rank 0), appended last so the
    existing default (`store_manager`) is unaffected.
  - New second `<select>` "Шаблон ролі (необов'язково)" under the Role field — active
    templates from `useTenantRoles(false, canManageTenantRole)` + "— Без шаблону —".
    `canManageTenantRole = hasRole(me?.role, AT_LEAST_ENTERPRISE_ADMIN)` gates it
    defensively (mirrors `TenantRoleSelector`/`TenantRoleBadge`) even though the modal
    today only ever opens for that same role set (`isAdmin` in `users/page.tsx`).
  - Picking a template auto-sets Role to `"staff"` *only if* the admin hasn't manually
    touched the Role field yet (`roleTouched` flag) — the two fields stay independent
    per ADR-020, this only nudges a sensible default for the common "template-only hire".
  - `handleSubmit`: `invite.mutateAsync` first, then (if a template was picked)
    `assignTenantRole.mutateAsync({ userId: newUser.id, tenantRoleId })`
    (`useAssignTenantRole`, already existed from TASK-348). On assign failure: user
    creation is never hidden or rolled back — `partialError` state shows "Користувача
    створено, але не вдалося призначити шаблон ролі: {reason}. Призначте вручну в
    профілі користувача." (trailing period from the server message stripped to avoid a
    double stop), modal does **not** auto-close, primary button becomes "Закрити"
    (submit handler short-circuits to `onClose()` instead of re-inviting the same email),
    the "Скасувати" ghost button is hidden, all fields disabled.
- `frontend/features/profile/types.ts`: `ROLE_LABELS.staff = "Спеціаліст (без доступу)"`
  — kept short since this map also feeds the compact role-pill badges in
  `UsersList`/`UserDetailPanel`, not just the invite dropdown.

No backend files touched. Confirmed pre-existing and unchanged: `"staff"` already in
backend `UserService.ValidRoles` (ADR-020), `POST /api/users/invite` already returns the
full `UserDto` (id included), `POST /api/users/{id}/tenant-role` already
`AtLeastEnterpriseAdmin`-only with no capability bypass.

## Verification

- `npx tsc --noEmit` — clean (twice: initial implementation, and after a follow-up fix
  found live below).
- `npm run build` — clean, 51/51 routes.
- Live-tested on local dev stack (`backend-dev` + `frontend-dev`) as `ea@demo.local`:
  - Created a fresh template "Бухгалтер TASK-349" (2 capabilities) via "Шаблони ролей".
  - Invite modal: role select now lists "Спеціаліст (без доступу)"; new template select
    lists "— Без шаблону —" / "HR" / "Бухгалтер TASK-349" with the hint text. Selecting
    the template with Role untouched flipped Role to "staff" automatically — confirmed
    via DOM inspection twice.
  - **Happy path:** invited `buh-test-349@demo.local` with role `staff` + the new
    template → `POST /api/users/invite` 201 → `POST /api/users/{id}/tenant-role` 204 →
    modal closed, user list updated immediately (9→ eventually) with both the role badge
    and the "Бухгалтер TASK-349" `TenantRoleBadge`. Opened the user's detail panel:
    `TenantRoleSelector` on the "Доступ" tab shows the same template pre-selected —
    confirms `UserDto.tenantRoleId` round-trips consistently everywhere it's rendered.
  - **Partial-failure path** (the part most worth verifying): selected an active
    template in the form, then archived that exact template via a direct
    `DELETE /api/tenant-roles/{id}` call (simulating a concurrent admin action) *before*
    submitting. Result: `POST /invite` 201, `POST /tenant-role` 400 "Cannot assign an
    archived TenantRole." → user still created and visible in the list (no template
    badge) → modal stayed open, amber warning banner with the exact required message
    text, submit button became "Закрити", Cancel button hidden. Found and fixed a
    cosmetic double-period bug this way (server message already ends in "."), re-verified
    clean after the fix.

## Files touched

- `frontend/features/users/components/InviteUserModal.tsx`
- `frontend/features/profile/types.ts`

## Follow-ups noticed, not fixed (out of scope)

- `UserDetailPanel.tsx`'s `EDITABLE_ROLES` (line ~17) doesn't include `"staff"` — a user
  invited as staff can't be re-selected as staff from the *edit* role dropdown later
  (doesn't corrupt data, just an inconsistent capability vs. invite). Natural small
  follow-up, flagged separately.
- Broader and unverified: `frontend/lib/roles.ts`'s nav/page gating sets are role-only
  and never consult the ADR-020 `capabilities` JWT claim, so a non-enterprise_admin
  `"users.manage"` capability holder (backend-legal per `EnterpriseAdminOrUsersManage`)
  may have no frontend entry point at all to reach pages their capability unlocks. Did
  not investigate scope/impact beyond `/users` — not flagged as a task, just noted.
