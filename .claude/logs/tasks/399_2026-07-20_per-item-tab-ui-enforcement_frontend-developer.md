# TASK-399: Per-item sidebar tab UI + enforcement (frontend follow-up to TASK-398)

**Agent:** frontend-developer
**Date:** 2026-07-20
**Status:** done

## Контекст

Backend (TASK-398, commit `d926127a`, local-only) shipped a hierarchical
`GET /api/tenant-roles/tabs` catalog (`{groupKey, groupLabelUa, items:[{key,labelUa}]}`) and
widened `TenantRoleTabs.All` to accept item-level keys (e.g. `/receipts`) alongside the original
10 group-level keys (e.g. `operations`) — but explicitly left all frontend wiring (catalog UI,
Sidebar enforcement, route guards) undone. This task closes that gap.

## Зроблено

1. **`features/tenant-roles/types.ts`** — `TenantTabDto` doc updated (leaf, not flat-catalog);
   added `TenantTabGroupDto { groupKey: string | null; groupLabelUa: string; items: TenantTabDto[] }`.

2. **`features/tenant-roles/api/tenant-roles.ts` / `hooks/useTenantRoles.ts`** —
   `tenantRolesApi.getTabs()` / `useTenantRoleTabs()` return type changed to
   `TenantTabGroupDto[]`, no other logic change (query key/caching untouched).

3. **`features/tenant-roles/components/TenantRolesTab.tsx`** — "Видимі розділи" replaced with
   a two-level tree (`TabsTree` + `TabGroupSection`, new): Dashboard renders as a standalone
   bare checkbox (no group header); every other section is a collapsible group (default
   expanded) with a "Select all" checkbox (indeterminate when partially selected) plus one
   checkbox per page. Checked-state and toggle logic (`toggleTabItem`/`toggleTabGroupAll` in
   `TenantRoleFormModal`) treat an item as checked when either its own key OR the parent
   `groupKey` is present in `allowedTabs`, and:
   - unchecking one item out of a fully-group-granted set **expands** the group key into every
     sibling item key minus the unchecked one;
   - checking the last remaining item in a group **collapses** back into the single group key
     (keeps stored shape identical to a pre-TASK-398 "whole group" template).
   New i18n key `Dashboard.tenantRoles.formModal.selectAllGroup` (uk: "Обрати всі", en: "Select
   all") in both `messages/uk.json` and `messages/en.json`.

4. **`components/layout/Sidebar.tsx`** — the TASK-397 line
   `if (tabsSet) return tabsSet.has(group.key);` (group-only) became
   `if (tabsSet) return tabsSet.has(item.href) || tabsSet.has(group.key);` (item-level OR
   group-level, backward compat). `/settings/legal-entities`'s separate `canManageLegalEntities`
   gate sits earlier in the same filter chain and returns before this line is ever reached for
   that item — untouched, per TASK-398's explicit flag. `showDashboard` unchanged — Dashboard's
   bare `"dashboard"` key already serves as both its own group-key and item-key (no NavGroup of
   its own), documented inline why TASK-399 doesn't touch it.

5. **`lib/useRequireTab.ts`** — signature changed `(tabKey, alreadyAllowed)` →
   `(itemKey, groupKey, alreadyAllowed)`; effective access now
   `tabs.includes(itemKey) || tabs.includes(groupKey)`. Updated the 3 call sites:
   `/users` → `("/users", "workforce", ...)`, `/schedules` → `("/schedules", "workforce", true)`,
   `/analytics` → `("/analytics", "analytics", ...)`.

## Верифікація

- `npx tsc --noEmit` — 0 errors.
- `npm run lint` — 0 warnings/errors.
- `npm run build` — success, 52/52 static pages (the `ENVIRONMENT_FALLBACK` lines in the log are
  a pre-existing benign next-intl dev/build noise unrelated to this change — reproduces on
  unrelated pages, e.g. the marketing landing page, with no tenant-roles code involved).
- `docker build -f frontend/Dockerfile frontend` — exit code 0 (run synchronously, confirmed via
  direct `$?` capture, not through a piped `tail`).
- **Live dev-server + browser verification** (local dev DB, `crmproductsystems-postgres-1`,
  backend on :5000 / frontend on :3000; 2 disposable QA users, cleaned up after):
  - New hierarchical tree renders correctly: Dashboard standalone checkbox + 9 collapsible
    groups with "Select all" + per-item checkboxes, labels backend-sourced.
  - Role with only the "Прийомка" item checked → `POST /api/tenant-roles` persisted
    `allowedTabs: ["/receipts"]`. Assigned to a fresh `store_manager` test user; after
    re-login, `/api/auth/me` returned `"tabs":["/receipts"]`, Sidebar's Operations group showed
    **only** "Receiving -> /receipts" (Catalog/Stock/Transfers/Write-offs/Locations/IoT and
    Dashboard all correctly hidden), and direct navigation to `/users` redirected to
    `/dashboard` (route guard correctly blocked — neither `/users` nor `workforce` granted).
  - Role with "Select all" on Operations → persisted as `allowedTabs: ["operations"]` (byte-
    identical shape to a pre-TASK-398 template). After reassigning + re-login, Sidebar showed
    **all 7** Operations items — confirms group-level backward compat end to end.
  - Editing that role and unchecking one item (IoT) → checkbox state correctly expanded to the
    other 6 items with "Select all" going indeterminate; `PUT` persisted
    `allowedTabs: ["/inventory","/stock","/receipts","/transfers","/write-offs","/locations"]`
    (no `"operations"`, no `"/iot"`) — confirms the expand-on-uncheck branch.
  - Test artifacts (1 user, 2 tenant_roles rows) deleted after verification; the one pre-existing
    QA user's password hash that was reset to a known dev value (to enable login) was **not**
    restored to its original (unknown/unsaved) hash — disposable local-dev QA data only, no
    production/staging impact.

## Не в скоупі

- No backend changes (already done in TASK-398).
- `features/auth/types.ts`'s `tabs` field doc comment (mentions "OR" combination semantics from
  TASK-397, not item/group granularity) — left as is, unrelated staleness, not touched by this
  task's diff.

## Git

Local commit only, no push (per task brief — user pushes).
