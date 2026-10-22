# TASK-750 — Заявки магазинів на виробництво, backend (Етап 1)

Статус: review. Не закомічено. План: `~/.claude/plans/concurrent-cooking-pond.md`.

## Що зроблено
- Сутності `ProductionRequest` / `ProductionRequestLine` / `ProductionOrderRequestLine` (+ `ProductionOrder.NeededBy/PlannedDate`). Міграція `AddProductionRequests`: RLS (tenant_isolation, provider_bypass, worker_bypass, NULLIF) на 3 таблицях; RESTRICTIVE `store_scope` на `production_requests` (магазин АБО виробнича локація).
- `ProductionRequestService` (+ `IProductionRequestRepository`): create/update(draft)/submit/cancel (магазин); review/reject/summary/orders-from-requests/ship (виробництво). Ship = FEFO (прострочені пропускаються) -> `TransferService.CreateAsync` (`production_to_store`), статус `fulfilled` комітиться разом з трансфером, `ShipmentTransferId` лінкується після.
- Нотифікація `production_request.submitted` (outbox, StoreId = виробнича локація) + запис у worker `DISPATCH_EVENT_ROLES`.
- `ProductionService`: `UpdateOrderAsync` більше не змінює статус (409 при зміні; тільки start/complete/cancel); `CreateOrderAsync` вимагає `Location.Type == "production"` (422); optional NeededBy/PlannedDate.
- Tab-ключ `/production/requests` у `TenantRoleTabs`; `production_to_store` дозволено в `TransferService`.
- Тести: `ProductionRequestServiceTests` (26), доповнено `ProductionServiceTests` (4), `ProductionRequestsRlsIntegrationTests` (RLS + real-Postgres flow create->review->ship), `StoreScopeRlsIntegrationTests` guard += `production_requests`.

## Build / tests
`dotnet build` ok; повний `dotnet test` 2792/2792 на тимчасовій БД з застосованою міграцією (дроп після). Увага: на dev-БД `crm` store_scope-guard падатиме, доки міграцію не застосовано (`dotnet ef database update`).

## Відомі обмеження
- Одне відвантаження на заявку (без часткових відвантажень); без optimistic concurrency на ship.
- Авто-генерація заявок (source auto_*) — наступний етап; API створює лише `manual`.
