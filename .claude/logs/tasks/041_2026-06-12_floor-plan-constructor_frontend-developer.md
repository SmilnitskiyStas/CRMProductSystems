---
task_id: TASK-041
date: 2026-06-12
agent: frontend-developer
status: done
---

# TASK-041 — Web floor-plan constructor (/stores/:id/floor-plan)

v1-spec §6.4 — the last unimplemented web page from the spec. Backend was
already complete (PUT /api/stores/:id/floor-plan, jsonb `FloorPlan` on store).

## Files
| File | What |
|---|---|
| `features/stores/types.ts` | + FloorPlanLayout/FloorPlanZonePlacement/ZoneStatus(Counts) |
| `features/stores/api/stores.ts` | + updateFloorPlan, getStock (slim shape for zone counts) |
| `features/stores/hooks/useFloorPlan.ts` | parseFloorPlan, useUpdateFloorPlan, useZoneStatusCounts |
| `features/stores/components/FloorPlanCanvas.tsx` | dnd-kit canvas: dark bg + grid, draggable ZoneBox, hover tooltip, resize handle |
| `features/stores/components/FloorPlanSidePanel.tsx` | tools, unplaced zones, legend, placed-zone list |
| `app/(dashboard)/stores/[id]/floor-plan/page.tsx` | page: local layout state, dirty tracking, save, store switcher |
| `app/(dashboard)/floor-plan/page.tsx` | sidebar entry → redirects to first store |
| `components/layout/Sidebar.tsx` | + «План магазину» (AT_LEAST_STORE_MANAGER) |

## Spec compliance (§6.4)
- ✅ Canvas with dark background + grid (CSS gradients, snap step from layout)
- ✅ Drag & drop via @dnd-kit/core (+ @dnd-kit/modifiers: grid snap, restrict to parent)
- ✅ Zone shows name, type icon, color = worst product status
  (expired > critical > warning > safe > empty/gray)
- ✅ Hover tooltip with safe/warning/critical (+expired if >0) counts
- ✅ Right panel: tools + legend (+ unplaced zone placement, remove from plan)
- Extra: corner resize handle (native pointer events), snap to grid

## Decisions
- Layout persisted in `stores.floor_plan` jsonb: `{version:1, grid:20, zones:[{zoneId,x,y,w,h}]}`;
  zones missing from layout = "unplaced", zones deleted after save are skipped on render
- Zone status counts derived client-side from GET /api/stock (same approach as dashboard);
  React Query key `["stores", id, "zone-status"]`
- New deps: @dnd-kit/core, @dnd-kit/modifiers

## Verification
- `npx tsc --noEmit` clean; `npm run build` clean — route `/stores/[id]/floor-plan` 19 kB
- Live e2e (drag/save against API) not run in this session — see handoff

## Handoff → qa-tester
Manual check when stack is up: open «План магазину» from sidebar → redirects to
first store; add zones, drag/resize, save → reload page → layout persists;
hover tooltip shows real stock counts per zone.
