# TASK-397: Fix TenantRole tabs OR-bug + unify store assignment to multi-select for every restricted role

**Agent:** frontend-developer
**Date:** 2026-07-20
**Status:** done

## Bug 1 — Store assignment: multi-select for every restricted role, not just network_manager

1. **`frontend/features/users/types.ts`** — renamed `SINGLE_LOCATION_ROLES`/`isSingleLocationRole`
   → `LOCATION_SCOPED_ROLES`/`isLocationScopedRole` (added `network_manager` to the set), 1:1
   mirror of backend `UserService.LocationScopedRoles` (already existed there for
   `needsLocationAssignment`, TASK-395). The single-vs-multi UI distinction is gone; every
   restricted role now uses the same multi-select.
2. **`frontend/features/users/components/LocationsMultiSelectDropdown.tsx`** (new, shared) —
   closed-by-default dropdown: a button summarizing the selection ("Оберіть магазини" /
   "N локацій обрано") opens a floating checkbox panel (portal + viewport-anchored, same
   pattern as `ActionMenu`/`Sidebar`'s `CollapsedGroupTrigger`), closes on outside click or a
   "Готово" button. Purely controlled/presentational — no fetch or save logic of its own.
3. **`UserLocationsEditor.tsx`** — swapped its always-expanded checkbox list for
   `LocationsMultiSelectDropdown`; kept its own fetch/save/dirty-check logic unchanged.
4. **`UserDetailPanel.tsx`** — `canManageLocations` now checks `isLocationScopedRole(user.role)`
   instead of `user.role === "network_manager"`. Removed the single-`<select>` "Магазин" field
   and its `storeId`/`isSingleLocationEdit` state entirely. `handleSave` now always forwards
   `user.storeId` unchanged (no longer edited from this panel for any role) — **important**:
   this is not just cosmetic, see the backend section below for why it must never send a
   *different* value.
5. **`InviteUserModal.tsx`** — removed the silent `me.storeId` auto-bind and the fallback
   single-picker entirely. Every `isLocationScopedRole` pick now shows the same multi-select
   (`LocationsMultiSelectDropdown`), seeded once from the inviter's own store as a default (not
   silent — visible and freely editable), applied via the existing two-step
   invite-then-`PUT /locations` flow (same pattern network_manager already used).
6. **i18n** (`uk.json`/`en.json`) — removed now-dead `detailPanel.storeLabel/storeNoneOption`
   and `inviteModal.storeLabel/storeNoneOption`; added `locationsEditor.selectPlaceholder/
   doneButton` and `inviteModal.territoryPlaceholder/territorySelectedCount/
   territoryDoneButton`; reworded `territoryHint`/`locationsEditor.label`/`inviteModal.
   territoryLabel` to drop "network manager"-specific wording now that every restricted role
   uses this same UI.

### Backend fix required beyond "frontend-only" (found during implementation, not pre-existing)

Confirmed the stated premise — `PUT/GET /api/users/:id/locations` has no role restriction and
no location-count cap for any target — but found a **real data-loss interaction** this specific
change exposes: `UserService.UpdateAsync`/`InviteAsync` call `SyncSingleLocationAsync`
*unconditionally* on every save for `SingleLocationRoles` (store_manager/merchandiser/
storekeeper/cashier/staff), which force-replaces `user_locations` with a single row (or empty)
matching `request.StoreId` — **regardless of whether storeId changed**. Once these same roles
can hold 2+ locations via the new uniform multi-select, the next unrelated profile edit (name/
phone/role/legal entity) through the plain Update endpoint would silently collapse/wipe that
multi-location assignment. This is a direct, guaranteed consequence of shipping Bug 1 as
specified, not a hypothetical.

Fix (`backend/ShelfGuard.Application/Features/Users/UserService.cs`,
`SyncSingleLocationAsync`): read the target's current `user_locations` row count first; skip
the legacy single-row sync entirely once it's already 2+ (meaning `SetLocationsAsync`/the new
UI has taken over for that user). 0-or-1-row targets (the legacy shape, includes every brand
new invite) are unaffected — same behavior as before. Updated the stale doc comments in
`UserService.cs`/`IUserService.cs` that described the old "network_manager-only multi-location"
invariant. Added two unit tests to `UserServiceLocationsTests.cs` (guard triggers at 2+ rows;
still syncs normally at exactly 1 row — boundary check). Full backend suite: **901/901
passing**. Verified live in the browser too (see below) — assigned a store_manager 2 locations,
then saved an unrelated profile edit, confirmed the 2-location assignment survived.

Flagging this prominently per the brief's own "confirm nothing else on this screen still needs
to read/write plain storeId" instruction — this is the answer to that question, and it required
touching backend code, which the brief said was out of scope. Happy to have this reviewed
separately if preferred.

## Bug 2 — TenantRole.AllowedTabs made authoritative/exclusive instead of additive-OR

1. **`frontend/components/layout/Sidebar.tsx`**:
   - Group-items filter: `if (tabsSet?.has(group.key)) return true;` (additive, could only ADD
     visibility) → `if (tabsSet) return tabsSet.has(group.key);` (when a non-empty tabsSet
     exists, it's the complete answer for that group — replaces the `item.roles`/permission
     checks below entirely, in both directions). `tabsSet` stays `null` (falls through
     unchanged to the old logic) whenever the user has no TenantRole tabs configured — the
     default, and the case for every existing capability-only TenantRole user.
   - `showDashboard`: previously never consulted `tabsSet` at all (a separate, genuinely
     non-functional bug — checking/unchecking "Дашборд" had zero effect either direction).
     Now applies the same override: `tabsSet ? tabsSet.has("dashboard") : (existing role
     check)`.
   - Legal Entities' `canManageLegalEntities` special-case is untouched by design — it sits
     before the tabsSet check and stays independent of it in both directions (unchanged from
     391c).
2. **`frontend/lib/useRequireTab.ts`** — same override semantic for the route-guard: non-empty
   tabs claim is authoritative (can grant OR block), empty/absent claim falls back to
   `alreadyAllowed` unchanged.
3. **`/users`, `/schedules`, `/analytics` page comments** — updated to describe the new
   override semantic (previously described a plain OR). Flagged explicitly in `/schedules`'s
   comment: this page had NO restriction for any tenant role before, so a TenantRole with
   non-empty tabs excluding "workforce" now blocks direct navigation to `/schedules` too (not
   just hides the sidebar link) — a deliberate, verified consequence of making Sidebar and the
   route guard agree, not a new bug.

**Settings is deliberately never hideable** (no `roles` on `settingsItem`, rendered outside the
tabsSet-filtered group logic entirely) — confirmed this is correct as shipped (ADR-021: it's
where a user manages their own profile/password/2FA, must never be lockable), not something
left unfixed.

## Live browser verification (in addition to build/lint/tsc/docker)

Ran `docker compose up -d postgres` (already running) + `backend-dev`/`frontend-dev`
(`.claude/launch.json`), logged in as seed users:
- **Bug 1**: as `ea@demo.local` (enterprise_admin) — opened `Олена Ткаченко` (store_manager)
  and `Дмитро Коваль` (merchandiser); both now show the closed "STORES" dropdown on the Access
  tab (previously store_manager had no Access-tab location UI at all — only a single `<select>`
  on the Info tab; merchandiser had neither). Selected 2 locations for the store_manager via
  the dropdown, saved — `PUT /api/users/:id/locations` returned 200 with both ids. Confirmed
  the "Without a store" coverage-gap stat/badge (TASK-395/396) updated correctly (8 → 7, her
  row's badge cleared). Then edited her name via the plain Info-tab Save (no location field
  there anymore) — confirmed via the Access tab afterward that the 2-location assignment was
  **not** wiped (the exact backend regression the guard above prevents).
- **Bug 2**: created a throwaway TenantRole with `allowedTabs = ["operations"]` only (no
  capabilities), assigned it to the merchandiser, logged in as him — sidebar showed **only**
  "OPERATIONS" (no standalone Dashboard, no Staff/workforce group, even though `/schedules`
  inside that group has zero role restriction) — confirmed all 7 Operations items visible
  (including ones merchandiser's role alone wouldn't normally grant, e.g. Receiving/Transfers/
  Locations/IoT), matching the "group-level, exclusive" spec exactly. Direct navigation to
  `/schedules` redirected to `/dashboard` (route guard agrees with the sidebar). Logged in as
  the store_manager (TenantRole "HR", `allowedTabs: []`, never configured) — sidebar showed the
  full normal set (Dashboard + Operations/Sales/Procurement/Marketplace/Analytics/Staff/
  Support), confirming **empty AllowedTabs is a complete no-op**, exact backward compatibility.
- Cleaned up afterward: reverted the merchandiser's TenantRole to "No template" (confirmed via
  `GET /api/users` — `tenantRoleId` back to `null`), archived the throwaway TenantRole
  (confirmed via `GET /api/tenant-roles?includeInactive=false` — only `HR`/`Бухгалтер TASK-349`
  remain, both `allowedTabs: []`). The 2-location assignment on `Олена Ткаченко` (store_manager)
  was left in place — a real, sensible exercise of the feature, not test debris.
- Both dev servers stopped after verification; left the pre-existing `postgres` container
  running (was already up before this task, standard dev state).

## Верифікація

- `npx tsc --noEmit` — 0 помилок.
- `npm run lint` — 0 попереджень/помилок.
- `npm run build` — exit 0, 52/52 сторінок (repeated `ENVIRONMENT_FALLBACK` next-intl SSG
  noise, same pre-existing/unrelated pattern TASK-391c/392c/396 already documented).
- `docker build -f frontend/Dockerfile frontend` (з кореня репо) — synchronous, **exit 0**.
  Verification image removed after confirming.
- Backend: `dotnet build` — 0 errors (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`). `dotnet test ShelfGuard.Tests` — **901/901 passing** (full
  suite, not just the Users ones).
- Live e2e browser verification — see above, both bugs confirmed fixed and backward-compat
  confirmed, on real dev data.

## Не в скоупі (свідомо)

- Stage 3 RLS enforcement — untouched, doesn't exist yet.
- Tier 2 backend enforcement of `AllowedTabs` for route access beyond the existing
  `useRequireTab`-guarded pages — untouched, deliberately deferred (per 391c).
- `EDITABLE_ROLES` in `UserDetailPanel.tsx` still excludes `staff`/`network_manager` from the
  role-change dropdown — pre-existing gap noted by 392c, not touched here (unrelated to this
  brief).

## Git

Committed locally. **No push** — product owner reviews and pushes themselves.
