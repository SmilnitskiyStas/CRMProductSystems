# TASK-727 - Web "Каси" management page

Status: review · not committed

## Backend (read-only additions, no auth/RLS change)
- `IRegisterReportRepository` (Domain) + `RegisterReportRepository`; `RegisterReportService` (Application) reuses `IRegisterService.ListAsync` for store scoping (store_manager own store, network_manager assigned stores).
- `RegistersController`: GET `operators`, `stats?from&to&storeId` (max 366 days), `{id}/sessions`, `{id}/shifts` (paged, max 100), `employees?storeId` (POS-role staff not outranking caller, PIN status only, never PIN).
- `RegisterService.PosRoleRank` made internal. Tests: `RegisterReportServiceTests` (7).

## Frontend
- `features/registers/` (types, api, hooks, components) + `app/(dashboard)/registers/page.tsx`; messages uk/en `Dashboard.registers`; Sidebar item "Каси" (AT_LEAST_STORE_MANAGER).
- One-time secrets (activation code, PIN) shown in modal with copy; re-issue for activated register asks confirmation (signs device out). self_checkout shown disabled.

## Verify
dotnet build ok; `dotnet test --filter Registers` 59/59; `tsc --noEmit`, `npm run lint`, vitest 66/66 clean. Not checked in browser.
