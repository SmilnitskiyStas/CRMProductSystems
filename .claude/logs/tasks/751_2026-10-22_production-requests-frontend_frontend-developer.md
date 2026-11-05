# TASK-751 — Production requests, frontend (Stage 1)

Added: `features/production` types/api/hooks (`useProductionRequests.ts`), components `ProductionRequestsPage|Form|Detail|Summary|StatusBadge`, route `app/(dashboard)/production/requests/page.tsx`, Sidebar item, i18n (`Dashboard.production.requests.*`, uk+en), order table/detail show neededBy/plannedDate and linked requests.

Decisions:
- Sidebar item sits in the `production` group (moduleKey gate): stores of a tenant that has the production module reach it; a tenant without the module has no nav entry. Page itself is not module-gated; summary/review/ship/create-orders UI is enabled only if `useModules()` contains `production` (those endpoints are module-gated, 403 otherwise).
- Single page with tabs (Заявки / Зведення), detail + review as modals (existing Modal/ReasonModal/Btn/Table primitives). Item picker uses `/api/items?search` (debounced).
- Shipment link goes to `/transfers` (no per-transfer route used).

Status: `npx tsc --noEmit` and eslint (changed dirs) clean. Not verified in browser (no backend/dev server). Not committed.
Open: orders-from-requests 422 message shown raw (lists items without recipe); no neededFrom/neededTo filter UI.
