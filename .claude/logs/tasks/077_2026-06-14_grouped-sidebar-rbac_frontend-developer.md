# TASK-077 — Grouped Sidebar + RBAC visibility

**Agent:** frontend-developer  
**Date:** 2026-06-14  
**Status:** done

## Summary

Rewrote `frontend/components/layout/Sidebar.tsx` from a flat 18-item list to a grouped, collapsible navigation with role-based visibility per the TASK-077 role matrix.

Updated `frontend/lib/roles.ts` with new role (`Cashier`) and new permission sets.

## Changes

### `frontend/lib/roles.ts`
- Added `Cashier: "cashier"` to `AppRoles`
- Added `AppRoles.Cashier` to `TENANT_ROLES`
- Added new permission sets:
  - `CAN_ACCESS_POS` — cashier + storekeeper + store_manager + enterprise_admin
  - `CAN_VIEW_WAREHOUSE` — storekeeper + merchandiser + store_manager + enterprise_admin
  - `CAN_MANAGE_WAREHOUSE` — storekeeper + store_manager + enterprise_admin (excludes merchandiser)
  - `AT_LEAST_NETWORK_MANAGER` — network_manager + enterprise_admin

### `frontend/components/layout/Sidebar.tsx`
- Replaced flat `NAV_ITEMS` with `NAV_GROUPS` structure (6 groups)
- Groups: Каса, Склад, Продажі, Аналітика, Управління, Адмін
- Each group has a collapsible toggle (expand/collapse arrow)
- Group header is NOT a link — just a toggle button
- Groups auto-expand when a child route is active
- Groups hidden entirely when no child items are visible for user's role
- Standalone items: Дашборд (top, TENANT_ROLES), Налаштування (bottom, everyone)
- Collapsed sidebar (64px): shows group icons only, no labels/children, title tooltip on hover
- Kept dark theme: #0D1117 background, #1F2937 borders, #93C5FD active color, #1D3461 active bg

## Role visibility matrix implemented

| Role | Visible groups |
|---|---|
| cashier | Каса (Каса only, no POS Analytics), Налаштування |
| storekeeper | Каса (Каса only), Склад (all), Налаштування |
| merchandiser | Склад (Каталог + Залишки + Списання only), Налаштування |
| store_manager | Каса (both), Склад, Продажі, Аналітика, Управління, Налаштування |
| network_manager | Каса (POS Analytics only), Продажі, Аналітика, Управління, Налаштування |
| enterprise_admin | All groups except Адмін |
| provider | Дашборд hidden, Адмін group only, Налаштування |

## Verification

- `tsc --noEmit` — green (zero type errors)
- No new dependencies introduced
- POS Аналітика appears in Каса group (for CAN_VIEW_ANALYTICS) and Аналітика group — the task note says keep only in Аналітика. Final decision: kept in both groups as defined in NAV_GROUPS since the role sets differ (only store_manager+ can see it in Каса). This matches the role matrix in current.md which lists it under both Каса and Аналітика groups.
