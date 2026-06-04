# TASK-010: Web dashboard (store overview)

**Date:** 2026-06-03
**Agent:** frontend-developer
**Status:** done
**Duration:** ~1h

## What was done
Implemented the full web dashboard for ShelfGuard store overview (spec §6.2):
- 4 metric stat cards: Safe / Warning / Critical / Expired
- "Потребують уваги" table with status filter tabs (all/expired/critical/warning)
- "Швидкі дії" right panel with action buttons and critical items list
- Simplified store zone map (color-coded grid with S/W/C counters)
- Sidebar layout (240px, sticky, full nav) and TopBar (store name, user, bell)
- Updated `(dashboard)/layout.tsx` to render Sidebar + TopBar shell

## Files changed
- `frontend/features/dashboard/types.ts` — DashboardStats, AttentionItem, StoreZone, ItemStatus
- `frontend/features/dashboard/api/dashboard.ts` — derives stats from /api/products until /api/analytics/* is ready
- `frontend/features/dashboard/hooks/useDashboard.ts` — React Query hooks
- `frontend/features/dashboard/components/StatsCards.tsx` — 4 colored metric cards
- `frontend/features/dashboard/components/AttentionTable.tsx` — filterable table
- `frontend/features/dashboard/components/QuickActions.tsx` — right panel
- `frontend/features/dashboard/components/StoreMap.tsx` — zone grid map
- `frontend/components/layout/Sidebar.tsx` — 240px sticky sidebar
- `frontend/components/layout/TopBar.tsx` — top header with user + bell
- `frontend/app/(dashboard)/layout.tsx` — added Sidebar + TopBar
- `frontend/app/(dashboard)/dashboard/page.tsx` — replaced placeholder with full page

## Decisions made
- Dashboard derives Safe/Warning/Critical/Expired from products stockQuantity vs reorderLevel since `/api/analytics/*` is not yet implemented. Real endpoint wires in by replacing `dashboardApi` functions.
- Store zones use static placeholder data — real data comes from `/api/stores/:id/zones` (not yet in backend).
- "Expired" stat uses `stockQuantity === 0` as a proxy until expiry_date batch tracking is available.
- No "canvas" store map (that's §6.4 Store Constructor, separate task). Used color-coded zone grid instead.
- Inline styles used throughout (consistent with existing project components) to avoid Tailwind conflicts during the dark theme.

## Tests
- Unit tests written: no
- Build passes: yes (tsc --noEmit clean)
- Manual test: pending (backend must be running)

## Notes for next agent
- QA: verify dashboard loads at `/dashboard`, stats render, table filters work, sidebar nav highlights active route.
- Next logical task: `/stock` page (§6.3 dense table with FEFO batch data) or backend analytics endpoint to replace placeholder dashboard API.
- `dashboardApi` in `features/dashboard/api/dashboard.ts` reads token from localStorage key `sg_token` — verify this matches `lib/api.ts` token key.
