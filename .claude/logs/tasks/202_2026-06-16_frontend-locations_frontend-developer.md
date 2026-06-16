# TASK-202 — Frontend: stores → locations
**Agent:** frontend-developer  
**Date:** 2026-06-16  
**Status:** done

## Summary
Completed the frontend migration from `stores` → `locations` terminology. All API calls now use `/api/locations`, Sidebar already linked to `/locations`, and a full CRUD locations page with `locationType` selector was created.

## What was already done (partial work before this task)
- `frontend/features/locations/` — complete: types, API, hooks (useLocations/useLocation/useFloorPlan), FloorPlanCanvas, FloorPlanSidePanel
- `frontend/app/(dashboard)/locations/[id]/floor-plan/page.tsx` — complete, uses locations API
- `frontend/app/(dashboard)/floor-plan/page.tsx` — redirects to `/locations/:id/floor-plan`
- `frontend/app/(dashboard)/stores/[id]/floor-plan/page.tsx` — redirects to `/locations/:id/floor-plan`
- `frontend/components/layout/Sidebar.tsx` — already links to `/locations`, no changes needed

## Changes made in this task

### 1. `frontend/features/stores/` — converted to re-export aliases
- `types.ts` → re-exports `LocationZoneDto as StoreZoneDto`, `LocationDto as StoreDto`, and other types from `@/features/locations/types`
- `api/stores.ts` → re-exports `locationsApi as storesApi`, `StockBatchSlim` from `@/features/locations/api/locations`
- `hooks/useStores.ts` → re-exports `useLocations as useStores`, `useLocation as useStore` from `@/features/locations/hooks/useLocations`
- `hooks/useFloorPlan.ts` → re-exports all from `@/features/locations/hooks/useFloorPlan`

This allows 11 existing consumer files to keep importing from `@/features/stores/*` without changes while all data flows through the new locations API.

### 2. `frontend/app/(dashboard)/analytics/pos/page.tsx`
- Fixed inline `useStores` hook: changed direct `/api/stores` call to `/api/locations` with queryKey `["locations"]`

### 3. `frontend/features/locations/api/locations.ts`
- Added `CreateLocationDto`, `UpdateLocationDto` types
- Added `create()` and `update()` methods to `locationsApi`

### 4. `frontend/features/locations/hooks/useLocations.ts`
- Added `useCreateLocation()` and `useUpdateLocation(id)` mutation hooks

### 5. `frontend/features/locations/components/LocationFormDialog.tsx` — NEW
- Create/edit dialog with `locationType` selector using `LOCATION_TYPE_LABELS`
- Labels: retail_store→«Роздрібний магазин», warehouse→«Склад», auto_service→«Автосервіс», office→«Офіс», production→«Виробництво», restaurant→«Ресторан»
- react-hook-form + zod validation
- Name, locationType, address fields; isActive toggle in edit mode

### 6. `frontend/app/(dashboard)/locations/page.tsx` — rewritten
- Full CRUD locations list page (was just a redirect before)
- Table with name, type badge, address, zone count, active status
- "Нова локація" button opens create dialog
- "Редагувати" button opens edit dialog
- Link to `/locations/:id/floor-plan` from each row

## Verification
- `npx tsc --noEmit` → 0 errors
- All API calls use `/api/locations`
- Sidebar links to `/locations` (already done, unchanged)
- `locationType` selector present in `LocationFormDialog` with all 6 Ukrainian labels

## Files changed
- `frontend/features/stores/types.ts`
- `frontend/features/stores/api/stores.ts`
- `frontend/features/stores/hooks/useStores.ts`
- `frontend/features/stores/hooks/useFloorPlan.ts`
- `frontend/features/locations/api/locations.ts`
- `frontend/features/locations/hooks/useLocations.ts`
- `frontend/features/locations/components/LocationFormDialog.tsx` (new)
- `frontend/app/(dashboard)/locations/page.tsx`
- `frontend/app/(dashboard)/analytics/pos/page.tsx`
- `.claude/tasks/backlog.md` (status → done)
