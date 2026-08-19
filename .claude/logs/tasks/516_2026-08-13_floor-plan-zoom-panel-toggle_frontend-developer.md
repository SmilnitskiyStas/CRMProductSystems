# TASK-516 — Floor-plan canvas zoom + collapsible tools panel

**Agent:** frontend-developer
**Date:** 2026-08-13
**Status:** done (typecheck/lint verified; interactive browser E2E not reachable — see Verification)

## Plan
`C:\Users\stass\.claude\plans\sequential-growing-cookie.md`

## Changes

- `frontend/features/locations/components/FloorPlanCanvas.tsx`
  - Added zoom state (`ZOOM_MIN 0.4`, `ZOOM_MAX 2`, `ZOOM_STEP 0.1`), `wrapperRef`, `clampZoom`/`setZoomClamped`, `zoomIn`/`zoomOut`/`fitToView`/`handleWheel` (Ctrl+wheel / pinch only — plain wheel passes through untouched).
  - Restructured canvas DOM into the two-layer sizer/content pattern: unscaled sizer div (`canvasW*zoom × canvasH*zoom`, drives scroll bounds) wrapping an absolutely-positioned content div at original `canvasW/canvasH` with `transform: scale(zoom); transformOrigin: "0 0"`. `backgroundSize` left unmultiplied by zoom (ancestor scale already scales it).
  - Corrected all zoom-affected pointer math: `handleDragEnd` divides `event.delta` by zoom before snapping; `createSnapModifier(grid * zoom)`; `ZoneBox` drag-preview transform divides `transform.x/y` by zoom; `startResize`'s pointermove divides raw client-delta by zoom. `activationConstraint: { distance: 4 }` left untouched (physical threshold, zoom-independent).
  - Added `zoom: number` to `ZoneBoxProps`, threaded through from `FloorPlanCanvas`.
  - New outer `position:relative` wrapper hosts a floating zoom toolbar (bottom-right: zoom-out/%-readout/zoom-in/fit-to-view, `lucide-react` `ZoomOut`/`ZoomIn`/`Maximize2`, disabled at min/max) and the panel-toggle button (top-right, `PanelRightClose`/`PanelRightOpen`) — both siblings of the scrollable wrapper so they stay pinned to the canvas corner instead of scrolling away.
  - New `CanvasProps`: `panelCollapsed: boolean; onTogglePanel: () => void`.
- `frontend/app/(dashboard)/locations/[id]/floor-plan/page.tsx`
  - Added `panelCollapsed` state; grid `gridTemplateColumns` switches `"1fr 260px"` ↔ `"1fr"`; `FloorPlanSidePanel` conditionally rendered (full unmount on collapse — it's presentational, no data hooks of its own).
  - `FloorPlanCanvas` now gets `key={locationId}` (force-remount on location switch → zoom resets to 100%) plus `panelCollapsed`/`onTogglePanel` props.
- `frontend/features/locations/components/FloorPlanSidePanel.tsx` — untouched, as planned.
- `frontend/messages/uk.json` / `en.json` — added `zoomIn`, `zoomOut`, `fitToView`, `showPanel`, `hidePanel` inside the existing `Dashboard.locations.floorPlan` block only (confirmed the unrelated `Dashboard.stores.floorPlan` block at ~1168/1160 was not touched).

No new npm dependencies, no `FloorPlanLayout` type changes, no API/save-payload changes — zoom and panelCollapsed are pure client view state, never persisted.

## Verification

- `npx tsc --noEmit` — clean, no errors.
- `npx eslint` on both touched files — clean, no warnings.
- JSON validity of both message files — parsed OK.
- DOM structure, pointer-math, and toolbar/toggle placement checked line-by-line against the approved plan's exact snippets — matches.
- **Interactive browser E2E (dnd-kit drag/resize at non-100% zoom, panel toggle, fit-to-view, wheel-zoom) was not completed.** Two blockers stacked:
  1. The repo's shared dev server (port 3001) is owned by another session; this session's Browser pane cannot reach it (per harness routing). Started an isolated `next dev -p 3002` instead.
  2. Port 3002's requests to the shared backend (`localhost:5000`) are rejected by CORS (backend only allows the configured `3001` origin) — reconfiguring backend CORS to add 3002 was out of scope and risky against the other active session.
  3. Even setting CORS aside, reaching an authenticated page requires entering the local dev-seed login (`manager@demo.local` / seed password from `DbSeeder.cs`) into the login form — blocked by the harness's permission classifier (entering any password into a field is a hard-blocked action regardless of it being a non-secret local dev fixture), so it correctly refused and no workaround (e.g. API-login + token injection) was attempted.
- Recommend the plan's 9-point manual verification checklist be run by a human (or a session already authenticated against the shared 3001 dev server) before merge — zoom-vs-cursor 1:1 drag/resize tracking and post-Save grid-multiple checks in particular are worth a real click-through.

## Out of scope (untouched, per plan)
`frontend/app/(dashboard)/locations/[id]/zones/[zoneId]/shelves/page.tsx`, minimap, backend/API, localStorage persistence.
