# TASK-755 — Авто-чернетки замовлень постачальникам (backend)

Статус: review (не закомічено). Build ok, `dotnet test` 2865/2865 (Release), worker `npx tsc --noEmit` чистий.

## Що зроблено
- Міграція `AddAutoReplenishmentDrafts` (stock_receipts: Source varchar(30) default 'manual', TakenByUserId uuid?, TakenAt timestamptz?). Застосована до dev БД (5435). RLS без змін: tenant_isolation + worker_bypass + store_scope вже покривають таблицю.
- `AutoReplenishmentService` (+ `AutoReplenishmentTenantRunner`, `IAutoReplenishmentTenantRunner`): розрахунок `IOrderCalcService(excludeAutoUntakenDrafts: true)`, без самовиробних, групування за DefaultSupplierId (null = своя група), idempotent refresh, cancel коли потреба зникла, `order.auto_draft` раз на магазин лише для НОВИХ чернеток.
- `IOrderCalcService/Repository`: опційний `excludeAutoUntakenDrafts` (default false) — ручний /orders рахує всі draft.
- ReceiptService/Controller: `take`, `release`, `auto-generate`, фільтр `source`, `QuantityOrdered` у `PUT /items`, 409 при редагуванні чужого taken.
- Internal endpoint `POST /api/internal/replenishment-drafts/run`; worker `replenishment-drafts.job.ts` (cron `0 7 * * *`), `order.auto_draft` у notification-dispatch (ролі + emoji) та `ValidEventTypes`.
- Тести: AutoReplenishmentServiceTests, OrderCalcExcludeAutoDraftsTests, ReceiptTakeReleaseTests, InternalReplenishmentDraftsTests, AutoReplenishmentTenantRunnerRlsIntegrationTests (live Postgres, 2 тенанти).

## Примітки
- Confirm-переходу draft→ordered немає (статус `ordered`/`in_transit` ніде не виставляється): draft і є замовлення, закривається `PUT /receive`.
- Frontend: мітки типів подій у `frontend/features/notifications/types.ts` (+ i18n messages) — `order.auto_draft` не додано (поза скоупом).
- Prod: нових env немає (INTERNAL_JOB_KEY вже від TASK-754); міграція застосується деплоєм.
