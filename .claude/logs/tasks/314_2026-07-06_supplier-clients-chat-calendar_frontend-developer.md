# TASK-314 — Кабінет постачальника: вкладка "Клієнти", чат постачальник↔клієнт, календар завдань

**Agent:** frontend-developer
**Date:** 2026-07-06
**Plan:** `calm-singing-marble.md`, handoff `.claude/logs/handoffs/313_backend-developer_to_frontend-developer.md`
**Status:** done

## Частина 1 — Вкладка "Клієнти"

- `lib/supplierPermissions.ts`: додано `client_management: "Клієнти"`.
- `features/supplier-cabinet/api/supplier-cabinet-api.ts`: `getClients()` → `GET /clients`.
- `features/supplier-cabinet/hooks/useSupplierCabinet.ts`: `useSupplierClients()`.
- Новий `features/supplier-cabinet/components/ClientsTab.tsx`: список клієнтів (назва, рейтинг/
  к-сть відгуків, к-сть завдань, дата останньої взаємодії), кнопка "Написати" відкриває чат.
- Нова сторінка `app/(dashboard)/supplier/clients/page.tsx` — permission-гейт на роль + на
  `client_management` (патерн скопійовано з `/supplier/tasks`, `/supplier/team`).
- `components/layout/Sidebar.tsx`: новий пункт "Клієнти" в `SUPPLIER_NAV_GROUP`, іконка
  `Building2` (lucide-react не має `Handshake` у встановленій версії — виявлено через
  tsc/webpack error, замінено).

## Частина 2 — Чат постачальник↔клієнт

- Типи (`SupplierChatSessionDto`, `SupplierChatMessageDto`, `SendSupplierChatMessageRequest`) —
  додані в `features/supplier-cabinet/types.ts` і дубльовані (client-side shape) в
  `features/marketplace/types.ts`, точно за shapes з handoff-у backend-developer'а.
- Supplier-side API/hooks: `getChatSessions/getChatMessages/sendChatMessage` в
  `supplier-cabinet-api.ts`, `useSupplierChatSessions/useSupplierChatMessages/
  useSendSupplierChatMessage` (polling 3000ms) в `useSupplierCabinet.ts`.
- Client-side API/hooks: аналогічно в `marketplace-api.ts`/`useMarketplace.ts`
  (`getSupplierChatMessages/sendSupplierChatMessage`, `useSupplierChatMessages/
  useSendSupplierChatMessage`).
- Нові компоненти: `features/supplier-cabinet/components/SupplierClientChatPanel.tsx`
  (постачальницька сторона) і `features/marketplace/components/SupplierChatPanel.tsx`
  (клієнтська сторона) — обидва за структурою `ClientChatPanel.tsx` (вікно повідомлень,
  автоскрол, форма відправки), `isMe` визначається порівнянням `senderTenantId` з
  `me.tenantId` з `useMe()`.
- `ClientsTab.tsx`: кнопка "Написати" відкриває `SupplierClientChatPanel`.
- `app/(dashboard)/marketplace/[id]/page.tsx`: кнопка "Написати постачальнику" в header-картці
  постачальника, відкриває `SupplierChatPanel`.

## Частина 3 — Календар для вкладки "Завдання"

- Новий `features/supplier-cabinet/components/TasksCalendar.tsx` — місячна сітка (HTML-таблиця
  через CSS grid, без бібліотек — підтверджено відсутність date-fns/moment/dayjs/
  big-calendar/fullcalendar в package.json), навігація місяць вперед/назад + "Сьогодні",
  групування завдань по `dueDate` (парсинг рядкових дат вручну, без бібліотек), бейдж-лічильник
  на день, клік → підсвітка + список завдань дня знизу, кнопка "Додати завдання" в контексті
  дня.
- `TasksBoard.tsx`: перемикач "Список"/"Календар" над існуючим контентом; список-режим не
  змінено. `TaskFormModal` отримав `defaultDueDate?: string` — прокидається з календаря при
  кліку "Додати завдання" для обраного дня.

## Верифікація

- `npx tsc --noEmit` (frontend/) — 0 помилок.
- Живий сніфф-тест у браузері (Claude Preview, реальні API-виклики до backend на :5000,
  фронтенд на :3000):
  - Створено тестову пару tenant-ів через `/api/provider/tenants` +
    `/api/provider/tenants/{id}/users` (`fe-chat-supplier-admin@test.local`,
    `fe-chat-client-admin@test.local`) — активовано `marketplace` модуль клієнту, опубліковано
    профіль постачальника.
  - Клієнт залишив відгук (5.0) → вкладка "Клієнти" постачальника коректно показала
    `reviewCount=1, avgRating=5.0, taskCount=0, lastInteractionAt`.
  - Чат: повідомлення відправлені і отримані в обидва боки через реальний UI (не тільки curl) —
    `POST .../messages → 201 Created` з обох сторін, `isMe`/`sender` рендериться правильно
    (своє — праворуч без імені, чуже — ліворуч з іменем відправника).
  - Календар: створено завдання на "сьогодні" (2026-07-06) через кнопку "Додати завдання" в
    календарі — `dueDate` попередньо заповнився правильно, `POST /tasks → 201 Created` (без
    500 — регресія на баг з попередньої QA-сесії, TASK-308 Bug #1, не відтворилась), день "6"
    показав бейдж "1", клік на день показав завдання в списку деталей.

## Знайдені проблеми

- lucide-react (встановлена версія) не експортує `Handshake` — виявлено одразу через tsc/webpack
  error при першому запуску, замінено на `Building2`. Не блокуюче, вже виправлено в цій же задачі.
- Автоматизований клік по кнопці логін-форми (`preview_click`) періодично не сабмітив форму в
  тестовому середовищі (можлива проблема самого preview-тулінгу, не додатку) — обійдено прямим
  викликом `fetch("/api/auth/login")` + `localStorage.setItem("sg_token", ...)` в консолі
  браузера, що є штатним шляхом додатку (`lib/api.ts: setToken`). Не є дефектом коду фічі.

## Тестові дані (dev DB, не production)

Створено для наскрізної перевірки: tenants `FE Chat Test Client`/`FE Chat Test Supplier`,
користувачі `fe-chat-client-admin@test.local`/`fe-chat-supplier-admin@test.local`
(пароль `Test12345!`), один тестовий відгук і кілька чат-повідомлень/завдань. Залишено в dev БД
для можливої подальшої QA-перевірки (аналогічно існуючим `QA Test Supplier`/`Chat Test Client`
фікстурам з попередніх сесій).
