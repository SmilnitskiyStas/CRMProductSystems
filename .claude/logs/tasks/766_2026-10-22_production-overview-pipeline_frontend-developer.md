# TASK-766 Production overview pipeline (frontend)
- New `ProductionPipeline.tsx`: 4 windows (Нові заявки -> У роботі -> Готово -> Відправлено), period selector (today/week/month/custom, validation from<=to, max 366d), stats strip, collapsible period stats (byItem, byStore, recharts per-day bars). Embedded in `ProductionOverview.tsx`; old KPI/lists reduced to «Потребує уваги» (shortages + expiring).
- `getPipeline` api, `useProductionPipeline` hook (refetch 60s), types in insights-types.ts; pipeline key invalidated on review/ship/sync/create-orders.
- i18n uk+en Dashboard.production.overview.*.
- Status: tsc + eslint clean. Backend contract (TASK-765) not yet verified live.
