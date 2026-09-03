# TASK-686 — Supplier Phase 4 frontend: mutable delivery date + in-transit source tooltip + i18n

**Status:** review (не закомічено) · **Agent:** frontend-developer · Plan `1-partitioned-book.md` Phase 4 (D5 / п.2)
Пара до backend TASK-685 (`685_2026-09-03_supplier-phase4-intransit_backend-developer.md`).

## Зроблено

### 1. Постачальник переносить дату доставки на SHIPPED-замовленні
- `features/supplier-cabinet/types.ts` — `SetOrderExpectedDeliveryDateRequest { expectedDeliveryDate: string }`.
- `features/supplier-cabinet/api/supplier-cabinet-api.ts` — `setOrderExpectedDeliveryDate(id, expectedDeliveryDate)`
  → `POST /api/supplier-cabinet/orders/{id}/expected-delivery-date`.
- `features/supplier-cabinet/hooks/useCabinetCooperation.ts` — `useSetExpectedDeliveryDate()`,
  invalidatе `CABINET_COOP_KEYS.orders` (= `["supplier-cabinet","orders"]`, той самий ключ що `useCabinetOrders`).
- `features/supplier-cabinet/components/CabinetOrdersTab.tsx` — новий `RescheduleDeliveryControl`
  рендериться в розгорнутому рядку `shipped`-замовлення одразу під `ShippingDetail`: `<input type="date" min={today}>`
  (default = `order.expectedDeliveryDate`) + `Btn size="sm"`. 400 → `toast.error(err.message)` (Ukrainian backend
  string), success → `toast.success`. Кнопка disabled коли pending / порожньо / без змін. Тільки в кабінеті
  постачальника — на `app/(dashboard)/marketplace/orders/page.tsx` контролю НЕ додавав (замовник лише бачить).

### 2. Tooltip розбивки «в дорозі»
- **Місце cell:** `features/orders/components/OrderLinesTable.tsx`, колонка `inTransit` (namespace `Dashboard.orders.table`).
  Це єдине місце — `features/ai-orders/` не показує in-transit і `AiOrderItem`/`AiOrderDto` не несе поля (backend
  теж лишив AiOrderService без DTO-змін, лише коментар). `BufferFunnel.tsx` уживає `line.inTransit` у позиції маркера,
  але окремим числом не показує — не чіпав.
- `features/orders/types.ts` — `OrderLine.inTransitFromMarketplace: number`.
- Рендер: `inTransit <= 0` → «—»; `inTransitFromMarketplace <= 0` → плоске число (без змін для не-marketplace тенантів);
  інакше `<span title>` з двома рядками (native `title`, як `BufferFunnel`) — «Прийоми постачальників: N» +
  «Відкриті marketplace-замовлення: M».

### 3. i18n (`messages/{uk,en}.json`, обидві)
- `Dashboard.notifications.eventTypes.marketplaceOrderDeliveryRescheduled` = «Нова дата доставки» / "Delivery date changed"
  + `eventSource.*` пара + `features/notifications/types.ts` (`NotificationEventType` union + `EVENT_TYPE_I18N_KEY`).
- `Dashboard.supplierCabinet.ordersTab.reschedule{Label,SaveButton,ToastSaved}`.
- `Dashboard.orders.table.inTransitTooltip.{supplierReceipts,marketplaceOrders}`.
- Парність: **5701 == 5701**, 0 diff (було 5691, +10 ключів кожна мова).

## Верифікація
- `npx tsc --noEmit` — clean.
- `npx next lint --file` (усі 7 торкнутих) — «No ESLint warnings or errors».
- `npx next build` — EXIT 0, «✓ Compiled successfully», «✓ Generating static pages (76/76)».
  `ENVIRONMENT_FALLBACK`-рядки під час static-gen — доринкова next-intl-шумка (не на моїх сторінках, build проходить).
- Preview не запускав (інша сесія тримає dev-порт).

## Deviations
- AI-order review UI — **нічого не зроблено** (обґрунтовано вище: не рендерить in-transit, DTO не несе поля).
- Backend / `mobile/` не чіпав. openapi.json — спільний борг. НЕ закомічено.
