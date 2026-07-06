# TASK-318 — Frontend: client cooperation UX + supplier cabinet (requests, contract settings, orders, support)

**Agent:** frontend-developer · **Date:** 2026-07-06/07 · **Status:** done
**Depends:** TASK-317 (handoff `.claude/logs/handoffs/317-to-318_frontend-developer.md`)
**Виконано у два проходи** (перший агент обірвався після типів/api/hooks/компонентів
клієнтської сторони; другий — довів сторінки, кабінет постачальника і верифікацію).

## Прохід 1 (готове раніше)
- `features/marketplace/types.ts` — cooperation/order/ticket types.
- `features/marketplace/api/marketplace-api.ts` — клієнтські ендпоінти (cooperation,
  orders, tickets, download договору).
- `features/marketplace/hooks/useCooperation.ts` — React Query hooks.
- Компоненти: `CooperationBadges`, `CooperationRequestModal`, `SupplierOrderCart`,
  `SupportTicketsPanel`; `components/ui/ReasonModal.tsx`; `lib/download.ts`.
- `SupplierItemsTab` — prop `onAddToCart` (AddToCartCell був визначений, але НЕ
  підключений у рядки — виправлено в проході 2).

## Прохід 2 (цей)
**Клієнт:**
- `app/(dashboard)/marketplace/[id]/page.tsx` — статус-бейдж угоди + дії за статусом
  (заявка / договір / hint «Підпишіть через Вчасно…»), кнопка «Служба підтримки»,
  кошик (CartLine state) → `SupplierOrderCart` при active. Все ховається для
  provider team і supplier_admin (гейт `TENANT_ROLES`). Угода шукається по
  `supplierName` (handoff: supplierTenantId ≠ публічний supplierId).
- Нова `app/(dashboard)/marketplace/orders/page.tsx` — таби «Замовлення»
  (expandable items, скасування з причиною лише для `new`) / «Співпраця»
  (угоди, № договору, download).
- Sidebar: «Мої замовлення» в групі Маркетплейс (roles: TENANT_ROLES).

**Кабінет постачальника (`features/supplier-cabinet/`):**
- `types.ts` — `SupplierContractSettingsDto`, `UpsertContractSettingsRequest`,
  `UpdateMarketplaceOrderStatusRequest` (спільні cooperation-DTO імпортуються з
  `features/marketplace/types.ts`, без дублювання).
- `api/supplier-cabinet-api.ts` — 17 нових функцій (cooperation-requests CRUD-дії,
  contract-settings + multipart uploads через `api.postForm`, orders + status,
  support-tickets).
- Новий `hooks/useCabinetCooperation.ts`.
- Компоненти: `CooperationRequestsTab` (фільтр-таби, approve з підказкою-лінком на
  реквізити при 400, reject/terminate через ReasonModal, договір/Вчасно/перегенерація/
  mark-signed), `ContractSettingsForm` (форма + upload підпису/печатки з превʼю,
  404 = порожня форма), `CabinetOrdersTab` (матриця переходів new→confirmed→shipped→
  delivered, cancel з причиною), `CabinetSupportTab` (список + тред + статус-select).
- Сторінки: `/supplier/requests`, `/supplier/contract-settings`, `/supplier/orders`,
  `/supplier/support` (+ 4 пункти в SUPPLIER_NAV_GROUP, без permission-ключів —
  у supplierPermissions поки немає відповідних прав).

## Верифікація
- `npx tsc --noEmit` — чисто (фікс: `Handshake` → `HeartHandshake`, lucide 0.312).
- `npm run build` — green, 48 маршрутів, усі нові сторінки в манифесті.
- Ручне UI-тестування проти живого бекенду не проводилось (потрібен QA-прохід
  повного флоу: заявка → approve → договір → mark-signed → замовлення → тікет).
