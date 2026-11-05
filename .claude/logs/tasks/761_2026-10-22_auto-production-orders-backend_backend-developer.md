# TASK-761 — Автоплан виробничих замовлень із прийнятих заявок, backend

Статус: review. Не закомічено.

## Що зроблено
- `ProductionOrderSyncService` (+ інтерфейс): ідемпотентний `SyncAsync(request)` — бажана кількість (AcceptedQty, 0 для rejected/cancelled) vs сума лінків; збільшення -> find-or-create Planned-ордера за ключем (товар, вир. локація, NeededBy), зменшення -> лише Planned (порожній -> Cancelled з примітки); InProgress/Done не чіпаються. Усе в одній транзакції (`ExecuteInTransactionAsync`, join до ambient) під `pg_advisory_xact_lock(hashtextextended('prod-order:{loc}:{item}',0))` (ключі відсортовано). Рецепт: найновіший активний (`CreatedAt DESC`).
- `ReviewAsync`: дозволено повторний review accepted/partial; review/reject/cancel + sync в одній транзакції. Нема рецепта -> заявка прийнята, ордера нема.
- `UnlinkOrderAsync` у `CancelOrderAsync` (лінки видаляються, авто-перестворення НЕ робиться — лише `sync-orders`). `NotifyOrderCompletedAsync` після Complete -> outbox `production_request.ready` (StoreId = магазин).
- DTO: `productionStage` (list+detail), `lines[].hasRecipe`, `lines[].linkedOrders[{orderId,status,qty}]`; order list `requestCount/storeCount`; order detail `requests[].requestStatus/shipmentTransferId`.
- Ship guard: 409 «Виробництво ще не завершене.» якщо є живі ордери й не всі Done (без ордерів — як раніше).
- Ендпоїнти: `POST /api/production-requests/{id}/sync-orders`, `POST /api/production/orders/{id}/ship-requests` (batch, per-request результат).
- Worker: `production_request.ready` у DISPATCH_EVENT_ROLES + іконка; `ValidEventTypes` += `production_request.ready`.
- Без міграцій (hasRecipe/stage похідні).

## Тести
`dotnet test` (Release) 2941/2941. Нові: `ProductionOrderSyncTests` (merge/new/InProgress/qty up-down/reject/no-recipe/cancel-unlink/stages/ship guard/batch з помилкою/ready notify), real-Postgres: конкурентні review (1 ордер, сума) + повний flow review->re-review->cancel->resync. Тестові тенанти чистяться.

## Обмеження / рішення
- Cancel ордера не перестворює ордер автоматично (рішення правила 4).
- Ship-requests: заявки, що чекають інші ордери, повертаються з ok=false; вже відвантажені/відхилені пропускаються.
