# TASK-400: Hide Locations Create/Edit buttons for roles below AtLeastEnterpriseAdmin

**Agent:** frontend-developer
**Date:** 2026-07-23
**Status:** done

## Контекст

Product owner reported: on `/locations`, clicking "Створити" for location type "Склад"
returns 403 from `POST /api/locations` with no friendly UI message. Root cause already
confirmed in the main session (not re-investigated here): `LocationsController.Create`/
`Update` are deliberately `AtLeastEnterpriseAdmin`-only (ADR-020/ADR-022 — Location
management is HQ-only "infrastructure", the same anti-escalation class as role/permission
assignment, and intentionally does **not** get the capability-OR escape hatch other modules
have). The bug is purely frontend: `locations/page.tsx` rendered the "Create" and "Edit"
buttons unconditionally for every `CanViewStock` role, so network_manager/store_manager/
merchandiser/storekeeper could see and click a button that was always going to 403.
Backend is correct as-is and was not touched.

## Зроблено

`frontend/app/(dashboard)/locations/page.tsx`:
- Imported `useMe` (`@/features/auth/hooks/useAuth`) and `hasRole`/`AT_LEAST_ENTERPRISE_ADMIN`
  (`@/lib/roles`) — same pattern already used identically in `users/page.tsx` for
  `canManageRoleTemplates`, nothing new invented.
- Added `const { data: me } = useMe();` and
  `const canManageLocations = hasRole(me?.role, AT_LEAST_ENTERPRISE_ADMIN);` (line ~27-33).
- Wrapped the header "Create" button (`t("newLocation")`, was line ~76-78) in
  `{canManageLocations && (...)}`.
- Wrapped the per-row "Edit" button (`t("edit")`, was line ~188-190) in the same condition.
- Left the "Plan" (floor-plan) link untouched — it hits `AtLeastStoreManager`, already
  correctly accessible to store_manager+.
- No whole-page gate added: `GET /api/locations` stays open to all `CanViewStock` roles, so
  the list itself remains visible to everyone who could see it before; only the two mutating
  buttons are now conditional.

## Верифікація

- `npx tsc --noEmit` in `/frontend` — 0 errors.
- Live browser check on local dev stack (dev `dotnet run`/`npm run dev` started for this
  verification, stopped again after; dev Postgres `crmproductsystems-postgres-1`, seeded demo
  tenant, unchanged):
  - Logged in as `manager@demo.local` (store_manager) — the exact reported scenario. `/locations`
    renders the list and "Plan" links correctly; **no "Create" button in the header, no "Edit"
    button per row.** No 403 possible anymore because the control simply isn't there.
  - Logged in as `ea@demo.local` (enterprise_admin) — header shows "New location", each row shows
    "Edit", confirming the gate doesn't regress the authorized path.
  - No console errors introduced by this change. One **pre-existing, unrelated** console error
    noted and left alone: `MISSING_MESSAGE: Dashboard.locations.types.shop` for the `en` locale
    (i18n key gap in `messages/en.json`, nothing to do with this diff — not touched per task
    brief's "no new translations needed").
  - No data mutated during verification (login/logout/GET only); dev DB left as found.

## Не в скоупі

- Backend (`LocationsController`) — confirmed correct as designed, not touched.
- New i18n keys — none needed, existing `newLocation`/`edit` keys reused as-is.
- New shared component — plain inline `hasRole` check, matches the minimal-diff pattern used
  on `users/page.tsx`.
- The pre-existing `Dashboard.locations.types.shop` missing-`en`-message console error (see
  above) — unrelated, flagged only, not fixed.

## Git

Not committed — task brief didn't ask for a commit; working tree left with the single
modified file (`git status`: `M frontend/app/(dashboard)/locations/page.tsx`) for the main
session/user to review and commit.
