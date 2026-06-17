# TASK-232 — Frontend: Auto Service Module

**Agent:** frontend-developer
**Date:** 2026-06-17
**Status:** done
**Depends on:** TASK-231 ✅

---

## Summary

Implemented the full Auto Service frontend module (TASK-232). All pages, components, hooks, API layer, and types are complete and verified with `tsc --noEmit` (green) and `next build` (green).

---

## Files Created

### Feature layer (`frontend/features/auto-service/`)

| File | Description |
|---|---|
| `types.ts` | All TypeScript interfaces matching TASK-231 API DTOs |
| `api/auto-service-api.ts` | API module using `@/lib/api` — 15 endpoints |
| `hooks/useAutoService.ts` | React Query hooks for all entities (work orders, customers, vehicles, service catalog) |
| `components/WorkOrderCard.tsx` | Kanban card with status badge + "Перемістити →" button |
| `components/WorkOrderKanban.tsx` | 5-column Kanban board (new / in_progress / waiting_parts / done / invoiced) |
| `components/CreateWorkOrderModal.tsx` | Modal to create work order: vehicle dropdown, mechanic dropdown, notes |
| `components/WorkOrderDetail.tsx` | Detail page: header, lines table, "Завершити" with 422 error display, add/remove lines |
| `components/WorkOrderLineForm.tsx` | Add line modal: type selector (service/part), catalog dropdowns, qty/price/discount |
| `components/CustomerForm.tsx` | Create/edit customer modal |
| `components/CustomerTable.tsx` | Customer list with expand → vehicle sub-list + "Авто" add button |
| `components/VehicleForm.tsx` | Create/edit vehicle modal |
| `components/ServiceCatalogTable.tsx` | Service catalog CRUD table with deactivate button |

### App Router pages (`frontend/app/(dashboard)/auto-service/`)

| Route | File |
|---|---|
| `/auto-service` | `page.tsx` — Kanban board |
| `/auto-service/work-orders/[id]` | `work-orders/[id]/page.tsx` — Work order detail |
| `/auto-service/customers` | `customers/page.tsx` — Customer list |
| `/auto-service/service-catalog` | `service-catalog/page.tsx` — Service catalog |

All pages include module gate: returns "Модуль Auto Service не активний" when `auto_service` not in `modulesData.modules`.

### Sidebar

Updated `frontend/components/layout/Sidebar.tsx`:
- Added `Wrench`, `Car`, `BookOpen` icons from lucide-react
- Added `auto_service` group with 3 links: Наряди / Клієнти / Каталог послуг
- Group gated by `moduleKey: "auto_service"` — hides automatically when module is off

---

## Acceptance Criteria

- [x] `tsc --noEmit` — green (no output)
- [x] `next build` — green, 4 new routes compiled
- [x] Kanban board loads work orders grouped by status column
- [x] Work order detail shows lines + "Завершити" button with 422 error handling
- [x] Customer list + expandable vehicle management
- [x] Service catalog CRUD + deactivate
- [x] Sidebar "Auto Service" group visible only when module active

---

## Key Patterns Used

- `api` client from `@/lib/api` (no local apiFetch)
- React Query for all server state, no Zustand duplication
- `"use client"` only on interactive components
- Module gate: `modulesData.modules.includes("auto_service")`
- Inline styles consistent with dark theme (#0D1117 / #111827 / #1F2937)
