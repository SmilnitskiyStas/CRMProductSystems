# TASK-764 production Overview/Plan/Shipments/Warehouse pages (frontend)

- Sidebar production group reordered (Огляд, Заявки, План і потреба, Виробничі замовлення, Відвантаження, Склад виробництва, Рецепти); `/production` redirects to `/production/overview`.
- New: `features/production/insights-types.ts`, `productionInsightsApi` (production-api.ts), `hooks/useProductionInsights.ts`, components `InsightsShared`, `ProductionOverview`, `ProductionPlan`, `ProductionShipments`, `ProductionWarehouse`; routes under `app/(dashboard)/production/{overview,plan,shipments,warehouse}`.
- i18n uk+en: `Dashboard.production.{overview,plan,shipments,warehouse}` + sidebar labels.
- Build: `tsc --noEmit` and eslint clean. Not verified against real backend (TASK-763 in parallel).
