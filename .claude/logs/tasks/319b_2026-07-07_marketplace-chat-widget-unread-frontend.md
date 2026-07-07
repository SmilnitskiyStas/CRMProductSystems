# TASK-319 (frontend half) — Marketplace chat: bottom-right widget + unread badges

**Date:** 2026-07-07 · **Status:** done
(Три спроби спавнити frontend-developer агента для цієї частини зависли — кожна
повертала "I'll wait for the agent" замість роботи; одна фонова інстанція все ж
тихо доробила частину `Sidebar.tsx` між сповіщеннями. Решту довершено напряму
в основній сесії.)

## Зміни

**Репозиціонування панелей** (з центрованого затемненого модального вікна на
плаваючу панель праворуч-внизу, як `SupportChatWidget`: fixed bottom:24 right:24,
380×540, maxHeight calc(100vh-100px), без backdrop):
- `frontend/features/marketplace/components/SupplierChatPanel.tsx` (клієнт)
- `frontend/features/supplier-cabinet/components/SupplierClientChatPanel.tsx` (постачальник)

**Бейджі непрочитаних:**
- `frontend/app/(dashboard)/marketplace/[id]/page.tsx` — `useSupplierChatMessages`
  піднято на рівень сторінки (раніше монтувався лише всередині панелі, коли
  `chatOpen`), тому 3-секундний polling працює і поки чат закритий; лічильник
  = `messages.filter(m => m.senderTenantId !== me.tenantId && !m.isRead).length`;
  червоний бейдж на кнопці «Написати постачальнику» (видно лише коли `!chatOpen`).
- `frontend/features/supplier-cabinet/components/ChatInboxTab.tsx` — бейдж
  `session.unreadCount` в кожному рядку списку чатів.
- `frontend/components/layout/Sidebar.tsx` — агрегований бейдж (сума
  `unreadCount` по всіх сесіях) на пункті «Повідомлення»; нове поле `badge?:
  number` в `NavItem`, рендер в `NavLink`, обчислення й підстановка в
  `visibleGroups` тільки для `/supplier/messages`.
- `frontend/features/supplier-cabinet/hooks/useSupplierCabinet.ts` —
  `useSupplierChatSessions(enabled = true)` отримав параметр `enabled`, щоб
  Sidebar міг гейтити запит лише для `supplier_admin` (інші ролі не мають
  доступу до цього ендпоінта).
- `frontend/features/supplier-cabinet/types.ts` — `SupplierChatSessionDto.unreadCount: number`.

## Перевірка
`npx tsc --noEmit` — чисто. `npm run build` — green (48 роутів). `dotnet build` —
0 помилок. `dotnet test` — 645/645 green.
