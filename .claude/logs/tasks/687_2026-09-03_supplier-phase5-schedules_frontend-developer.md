# TASK-687 — Supplier-portal expansion Phase 5: employee work schedules (D6, request #6)

**Agent:** frontend-developer · **Plan:** `1-partitioned-book.md` Phase 5 · **Status:** review (NOT committed)

## What changed

### Backend (new controller only — no migration, no service change)
- `backend/ShelfGuard.Api/Controllers/SupplierCabinetSchedulesController.cs` — thin pass-through to
  the **shared** `IScheduleService`, tenant id from JWT (`tenant_id` claim), user from
  `NameIdentifier`. Class attrs `[ApiController] [Authorize(Policy = AppPolicies.SupplierCabinet)]
  [RequireModule("supplier_workforce")]`. 9 schedule/shift actions mirroring `SchedulesController`
  + `GET /my-shifts` + `GET /staff`. Every **mutation** gated by
  `SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.WorkforceManagement)` →
  `Forbid()`. GET list/{id}/my-shifts have no permission gate (any supplier_admin of a
  module-enabled tenant).
- `GET /api/supplier-cabinet/schedules/staff` — assignee picker. Gated by `workforce_management`
  (NOT `staff_management`) and delegates to `ISupplierCabinetService.GetStaffAsync`. Decision:
  a schedule manager without the team-management permission still needs the list of people to
  place on shifts; a dedicated endpoint on the new controller avoids coupling to the existing
  `/api/supplier-cabinet/staff` (which is `staff_management`-gated) and touches nothing shared.
- `backend/ShelfGuard.Tests/Schedules/SupplierCabinetSchedulesControllerTests.cs` — 10 facts
  (NSubstitute `IScheduleService` + `ISupplierCabinetService`, claims via `ControllerContext`):
  tenant threaded from JWT into the service, forbid-when-no-tenant, create round-trip + error→400,
  `workforce_management` gate returns 403 on Create/AddShift/GetStaff (service not called),
  AddShift with the permission delegates, my-shifts resolves caller without a gate.

RLS: `work_schedules`/`schedule_shifts` are `tenant_isolation` + `provider_bypass` + `worker_bypass`,
NO `store_scope` — a supplier tenant sees only its own. `ScheduleService` already validates
`LocationExistsAsync(locationId, tenantId)`, so schedules/shifts can only attach to the supplier's
own warehouses (Location type `"warehouse"`).

### Frontend
- `features/supplier-cabinet/api/supplier-cabinet-api.ts` — nested `schedules.*` (list, getById,
  create, update, remove, addShift, updateShift, deleteShift, myShifts, staff). Reuses retail
  `@/features/schedules/types`.
- `features/supplier-cabinet/hooks/useSupplierSchedules.ts` (new) — mirrors `useSchedules.ts`,
  keys `["supplier","schedules",…]` / `["supplier","my-shifts",…]`.
- `features/supplier-cabinet/components/schedules/` (new): `SupplierScheduleList`,
  `SupplierScheduleForm` (warehouse picker via `useSupplierWarehouses`), `SupplierWeekGrid`
  (supplier shift hooks + `useSupplierScheduleStaff`), `SupplierMyShifts`.
- `app/(dashboard)/supplier/schedules/page.tsx` (new) — `SUPPLIER_ONLY` role check +
  `<ModuleGate moduleKey="supplier_workforce">`; tabs «Розклади» (visible with
  `workforce_management` / owner) / «Мій розклад» (all staff). No `AT_LEAST_STORE_MANAGER` gate.
- `components/layout/Sidebar.tsx` — `buildSupplierNavGroup` += `/supplier/schedules`
  (`CalendarDays`, `roles: SUPPLIER_ONLY`, `permission: "workforce_management"`,
  `moduleKey: "supplier_workforce"`), placed after `/supplier/inventory`.
- `messages/{uk,en}.json` — nav `supplierCabinet.schedules` = «Графіки»/"Schedules";
  `Dashboard.supplierCabinet.schedules.*` (page title/subtitle, tabs, hints, warehouse
  label/placeholder/error). All other strings reuse `Dashboard.schedules.*`.
  `supplier_workforce` module-catalog label already existed (Phase 1). Parity **4893 == 4893**,
  no key diffs.

## Component-reuse-vs-fork decision
Forked the 4 data-coupled components (`WeekGrid` imports retail `useSchedules` + `useUsers`;
`ScheduleForm` imports retail `useLocations`; `ScheduleList`/`MyShifts` import retail schedule
hooks). Reused **as-is** the genuinely presentational retail pieces — `ShiftCard` and `ShiftForm`
(both props-only) — and the retail `features/schedules/types` (identical shapes, no duplication).
The retail `/schedules` page and its component internals are untouched.

## Supplier-staff endpoint used
New `GET /api/supplier-cabinet/schedules/staff` → `UserDto[]`, gated by `workforce_management`,
delegating to `ISupplierCabinetService.GetStaffAsync(tenantId)` (same source the "Команда" page
uses). Chosen over the existing `staff_management`-gated `/api/supplier-cabinet/staff`.

## Verification
- Backend `dotnet build -c Release` — succeeded (1 pre-existing warning).
- `dotnet test -c Release --filter "FullyQualifiedName~Schedule"` — 32/32 pass.
- `dotnet test -c Release --filter "FullyQualifiedName~RlsCrossTenant"` — 6/6 pass.
- Frontend `npx tsc --noEmit` — clean. `npx next lint` (touched dirs) — clean.
- `npx next build` — EXIT 0; `/supplier/schedules` route built.

## Not done / notes
- Not committed (per brief).
- `backend/openapi.json` not regenerated — shared debt carried since TASK-670..674; api-contracts.md
  updated instead.
- `mobile/` untouched.
