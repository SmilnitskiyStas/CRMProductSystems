# BUG-019 — Supplier chat inbox unreachable for staff without client_management

**Agent:** frontend-developer · **Date:** 2026-07-07 · **Status:** done

## Причина
BUG-018 додав вкладку «Повідомлення» всередину `/supplier/clients` (`ChatInboxTab`).
Ця сторінка гейтиться правом `client_management` — скріншот користувача підтвердив,
що поточний обліковий запис постачальника цього права не має (в сайдбарі відсутні
«Профіль», «Клієнти», «Команда»), тому весь `/supplier/clients` (разом із новою
вкладкою чату) був недоступний і повідомлення від клієнтів лишались невидимими.

## Виправлення (тільки фронтенд)
- `frontend/components/layout/Sidebar.tsx` — новий пункт меню «Повідомлення»
  (`/supplier/messages`) у групі `SUPPLIER_NAV_GROUP`, **без** `permission`-ключа —
  за тим самим принципом, що й «Заявки на співпрацю» / «Замовлення» / «Підтримка»
  (TASK-318): бекенд для цих ендпоінтів не має permission-гейту в
  `SupplierPermissions`, тож і фронтенд їх не гейтить.
- Нова сторінка `frontend/app/(dashboard)/supplier/messages/page.tsx` — рендерить
  `ChatInboxTab` без перевірки `client_management`, лише базова роль `SUPPLIER_ONLY`.
- `frontend/app/(dashboard)/supplier/clients/page.tsx` — відкат до BUG-018: прибрано
  таб-перемикач і `ChatInboxTab`, сторінка знову рендерить тільки `ClientsTab`
  (переписка тепер живе на окремому, завжди доступному маршруті).

## Перевірка
`npx tsc --noEmit` — чисто. `ChatInboxTab.tsx` не змінювався (перевикористаний як є).
