# TASK-391c: TenantRole tabs UI — Sidebar visibility, role-form checkboxes, page guards

**Agent:** frontend-developer
**Date:** 2026-07-19
**Status:** done

## ⚠️ Worktree was stale — fast-forwarded before starting

This worktree branched from local `main` at `516a4178` (TASK-391, schema only). By the time
this task started, local `main` had already advanced with `a8d6cd62` (TASK-391b — the actual
`GET /api/tenant-roles/tabs` endpoint + JWT `tabs` claim) and `b342da61` (TASK-392b, unrelated
store-scoped user-location API). The brief's claim that 391b was "present in this worktree
since it branches from local main" did not hold for the worktree's actual state at task start
— `git log --oneline` on this branch topped out at 516a4178, and neither the controller
endpoint nor `AuthUserDto.Tabs` existed on disk yet.

Verified no uncommitted work would be lost (`git status` showed only an unrelated, content-
identical `.claude/settings.local.json` diff), then ran `git merge main --ff-only` — a clean
fast-forward (no rebase, no rewritten history) that brought in exactly those two commits plus
their task logs/handoff doc. Proceeded against the real backend contract afterward. Flagging
this so the orchestrator knows other worktrees branched before 391b landed may need the same
fast-forward.

## Зроблено

1. **`frontend/features/tenant-roles/types.ts`** — `allowedTabs: string[]` added to
   `TenantRoleDto`/`CreateTenantRoleRequest`/`UpdateTenantRoleRequest` (required, mirrors the
   sibling `capabilities` field's ergonomics — not optional, since the only caller,
   `TenantRoleFormModal`, always supplies a real array). New `TenantTabDto { key, labelUa }`
   (exact name/shape per brief — backend's equivalent DTO is named `TenantRoleTabDto`, noted
   in a comment).
2. **`frontend/features/tenant-roles/api/tenant-roles.ts`** — `getTabs()` → `GET
   /api/tenant-roles/tabs`.
3. **`frontend/features/tenant-roles/hooks/useTenantRoles.ts`** — `useTenantRoleTabs()`,
   exact mirror of `useTenantRoleCapabilities()` (same `staleTime`, same query-key shape).
4. **`frontend/features/tenant-roles/components/TenantRolesTab.tsx`** — `TenantRoleFormModal`
   gets a second checkbox block ("Видимі розділи"/"Visible sections" — new i18n keys added to
   `uk.json`/`en.json` under `Dashboard.tenantRoles.formModal.tabsLabel`) rendered the same way
   as the capabilities block (flat list, no specialty grouping — tabs aren't grouped). New
   `allowedTabs` Set state + `toggleTab`, included in both create/update request payloads.
   Deliberately did NOT touch `TenantRoleCard`'s badge/list display — brief scoped this item to
   the form modal only ("чекбокс-блок... поруч з існуючим capability-чекбоксами").
5. **`frontend/components/layout/Sidebar.tsx`** — new `tabsSet` (from `me.tabs`, null when
   empty/absent) computed alongside `effectivePermissions`/`supplierEffectivePermissions`
   (~line 669). In the group-items filter (~line 729), added `if (tabsSet?.has(group.key))
   return true;` positioned **after** the Legal Entities special-case and **before** the plain
   `item.roles` check — bypasses only the generic role check (per brief: "НЕЗАЛЕЖНО від
   item.roles-перевірки"), leaving the narrower, security-sensitive Legal Entities gate
   (`canManageLegalEntities`, enterprise_admin-or-explicit-override) untouched even for items
   inside a tabs-visible "workforce" group. Confirmed this bypass is inert for PROVIDER_TEAM/
   supplier_admin users (the only roles with non-null `effectivePermissions`/
   `supplierEffectivePermissions`) since neither role can carry a `TenantRoleId`/`tabs` claim
   in the first place (no `TenantId`) — no interaction with those two checks in practice.
6. **`frontend/lib/useRequireTab.ts`** (new) — `useRequireTab(tabKey, alreadyAllowed):
   boolean`. Caller supplies `alreadyAllowed` (mirroring whatever role/permission condition
   Sidebar.tsx uses for that page's NavItem); hook ORs it with `me.tabs.includes(tabKey)`,
   redirects to `/dashboard` via `router.replace` in a `useEffect` once `useMe()` resolves
   (never redirects while loading), and returns the combined boolean for callers that need it
   for their own render logic.
7. **Applied to 3 pages**, each with `alreadyAllowed` chosen to exactly match today's real
   page-reachability (never a new restriction beyond closing the "no page-level gate at all"
   gap that already existed for /users):
   - `/users`: `hasRole(me?.role, AT_LEAST_STORE_MANAGER)` — mirrors the `/users` NavItem's
     `roles`. Tightens direct-URL access for roles below store_manager (previously
     ungated at the page level) unless they now hold the "workforce" tab — this is the actual
     point of the feature, not a regression.
   - `/schedules`: `true` (hardcoded, commented) — the `/schedules` NavItem carries no
     `roles` restriction today (every tenant role, including provider-team, already reaches
     this page for "My shifts"); making the role side of the OR anything narrower would have
     broken that existing self-service view. Hook is wired but inert by design.
   - `/analytics`: `hasRole(me.role, CAN_VIEW_ANALYTICS)`. Also refactored the page's existing
     `access` variable (previously pure role-check) to fold in the hook's combined
     `effectiveAccess`, so a tabs-granted user doesn't hit a dead sidebar link (visible in
     Sidebar, then immediately shown the pre-existing `AccessDenied` component because the
     page's own gate hadn't heard about tabs).
8. **`frontend/features/auth/types.ts`** — `tabs?: string[] | null` added to `AuthUserDto`,
   matching the established nullable-optional convention already used by the sibling
   `preferredLocale?: string | null` field two lines above.

## Не в скоупі (свідомо)

- Tier 2 backend enforcement — not touched, doesn't exist.
- Feature 2 (store assignment UI, TASK-392b territory) — not touched, disjoint files.
- `TenantRoleCard` tabs display — form-modal-only per brief wording (see item 4 above).

## Верифікація

- `npm install` — worktree had no `node_modules` (fresh worktree), installed clean (629
  packages).
- `npx tsc --noEmit` — 0 errors.
- `npm run lint` — 0 warnings/errors.
- `npm run build` — exit 0, all 52 static routes generated including `/users`, `/schedules`,
  `/analytics`. (Build log has repeated non-fatal `ENVIRONMENT_FALLBACK` traces during static
  page generation — pre-existing `next-intl` SSG noise, unrelated to this change, build still
  exits 0.)
- `docker build -f frontend/Dockerfile frontend -t shelfguard-frontend-task391c-verify` —
  **exit code 0**. Image built successfully (271MB), all 3 stages (deps/build/runtime)
  completed, `npm run build` inside the container generated all 52 static routes including
  `/users`, `/schedules`, `/analytics`. Context transfer alone took ~278s because `frontend/`
  has no `.dockerignore` (sends the freshly-installed `node_modules`/`.next` as build context)
  — pre-existing gap, not introduced here; flagged via a spawned background-task suggestion
  for devops-engineer follow-up, not fixed in this task (out of scope). Verification image
  removed after confirming the build (`docker rmi shelfguard-frontend-task391c-verify`).

## Git

Committed locally in this worktree. **No push** — per explicit brief instruction (product
owner paused deploys today).
