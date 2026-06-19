# TASK: mobile-schedules — Workforce Schedules Mobile Module
**Date:** 2026-06-19
**Agent:** mobile-developer
**Status:** done

## What was done

Implemented the full Workforce Schedules mobile module for ShelfGuard.

### Files created

#### Feature layer (`mobile/features/schedules/`)
- `types.ts` — TypeScript interfaces: `WorkSchedule`, `WorkScheduleDetail`, `ScheduleShift`
- `api.ts` — API functions: `getMyShifts(from, to)`, `getSchedules(locationId?, weekStart?)`, `getSchedule(id)`
- `hooks/useSchedules.ts` — React Query hooks: `useMyShifts`, `useSchedules`, `useSchedule`
- `components/ShiftCard.tsx` — Shift card with date badge, time range, location, break, status badge, notes
- `components/ScheduleCard.tsx` — Schedule card for managers with location, week range, status badge, shift count, chevron

#### App routes (`mobile/app/(app)/schedules/`)
- `index.tsx` — Main screen with two modes:
  - "Мій розклад" — weekly view with ← → week navigation, FlatList of ShiftCards, pull-to-refresh, empty state
  - "Всі розклади" (managers only) — FlatList of ScheduleCards → navigates to detail screen
  - In-screen tabs (same pattern as service-desk) visible only to managers
- `[id].tsx` — Schedule detail screen (managers only): name + location header, status badge + week range, shifts grouped by date with section headers, ShiftRow with userName/time/break/status

#### Layout update
- `mobile/app/(app)/_layout.tsx` — Added hidden routes for `schedules/index` and `schedules/[id]`

### Key implementation details
- Week navigation: `getMonday()` helper + `weekOffset` state increments/decrements by 7 days
- Shifts sorted by `shiftDate` then `startTime` before rendering
- `groupShiftsByDate()` preserves insertion order (Map + sorted input)
- Time format: `t.slice(0, 5)` → "HH:mm" from "HH:mm:ss"
- Ukrainian day/month labels hardcoded for consistent formatting
- Manager roles: `['StoreManager', 'NetworkManager', 'EnterpriseAdmin', 'Provider', 'ProviderAdmin']`
- Status colors:
  - Shift: scheduled=blue, confirmed=green, completed=gray, cancelled=red
  - Schedule: draft=gray, published=green, archived=slate

## TypeScript check
`npx tsc --noEmit` — 0 errors
