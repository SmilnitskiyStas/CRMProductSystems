# TASK-724 — Frontend: обробка service_unavailable (follow-up TASK-723)

Status: review (not deployed)

## Що зроблено
- `lib/api.ts`: `isServiceUnavailable(err)` — 503 + `code: "service_unavailable"`.
- `lib/query-client.ts`: React Query не ретраїть service_unavailable (open circuit відповідає миттєво).
- Геокодер (`LocationFormDialog`): окремий toast «введіть координати вручну».
- AI: асистент-віджет і сторінка AI-замовлень показують м'яке повідомлення замість англійського `e.message`.
- Погода (`/events`): помилка запиту → нотатка «погода тимчасово недоступна»; раніше порожні дані давали хибний callout «задайте координати».
- i18n uk/en: `Locations.geocodeUnavailable`, `Dashboard.aiOrders.page.serviceUnavailable`, `weather.unavailable`.
- Тест: `lib/serviceUnavailable.test.ts` (існуючий `lib/api.test.ts` не чіпали).

## Перевірка
- `tsc --noEmit` OK, `vitest run` OK (усі файли), eslint на змінених файлах без помилок.
- НЕ перевірено в браузері (503 не відтворювався вживу).

## Відкрито
- Інші AI-блоки (marketing/promo/consumer assistant) не переглядались поштучно — вони підхоплять лише глобальне `retry`.
