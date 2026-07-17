# TASK-381: Dashboard i18n (uk/en) — Block 3: Sales & POS Flow

**Agent:** frontend-developer
**Date:** 2026-07-17
**Status:** done

## Зроблено

Переклав усі UI-рядки через існуючий `useTranslations`/`DashboardIntlProvider` (Block 1,
TASK-376) — жодного нового provider-wiring, тільки нові секції в
`frontend/messages/{uk,en}.json`: `Dashboard.sales`, `Dashboard.orders`, `Dashboard.pos`,
`Dashboard.aiOrders`, і новий top-level `Dashboard.analytics.pos` (перший запис у ще не
створеному `Dashboard.analytics` — Block 4 додасть сусідні секції для решти analytics).

**23 файли зі стрічками (з 25+5+5 файлів у скоупі — types/api/hooks без кирилиці не
чіпав, це відповідає плану):**
- `features/sales/{components/{CsvImportDialog,SaleEntryForm,SalesTable}.tsx}` —
  `SaleEntryForm`: zod-схема винесена в `buildSaleSchema(t)` + `useMemo`, той самий патерн
  що `buildProductSchema(t)` (Block 2a).
- `features/orders/components/{BufferFunnel,OrderLinesTable}.tsx` — `roundingLabels`
  (MOQ/USQ) лишились без змін — мовно-нейтральні домен-абревіатури, не кирилиця.
- `features/pos/components/*` (7 файлів) — `FiscalBadge.tsx` отримав `"use client"`
  (раніше був presentation-only без hooks); `META`/`shiftStatusMeta`/`PAYMENT_LABEL`
  втратили `label`-поле — лейбли резолвляться через `t.has(status) ? t(status) : status`
  з `Dashboard.pos.{fiscalStatus,shiftStatus,paymentType}` (літеральні enum-ключі: PascalCase
  для `ShiftStatus`/`PaymentType`, snake_case для `FiscalStatus` — прямо як тип у backend).
- `features/ai-orders/{types.ts,components/AiOrderReview.tsx}` — `STATUS_META` втратив
  `label` (той самий split, що `RECEIPT_STATUS_COLOR`/`_LABEL` у Block 2b), лейбл резолвиться
  через `Dashboard.aiOrders.status`. Судженнєвий момент (без потреби узгодження,
  задокументовано): `item.confidence` ("high"/"medium"/"low") виводився хардкодженим
  англійським словом навіть в україномовній збірці — залишок недоперекладеного коду, як
  "Safe"/"Warning" у Block 2b. Додав `Dashboard.aiOrders.review.confidence.*` для консистентної
  укр-локалізації.
- `features/analytics/components/Pos{TopProductsTable,SummaryCards,RevenueTrendChart,
  PaymentPieChart,CashierStatsTable}.tsx` — тільки ці 5 файлів з analytics, решта
  (CategoryStatusChart, ExpiryDonut, LossesByReasonChart, LossesByStoreChart) свідомо не
  чіпав (Block 4).
- 5 сторінок: `sales/page.tsx`, `orders/page.tsx`, `pos/page.tsx`, `ai-orders/page.tsx`,
  `analytics/pos/page.tsx` — включно з toast-повідомленнями, `AccessDenied title={t(...)}`.

**Locale-aware formatting:** усі `toLocaleDateString/toLocaleString/toLocaleTimeString
("uk-UA", ...)` замінені на `intlLocale` (`locale === "en" ? "en-US" : "uk-UA"` через
`useLocale()`) — той самий inline-патерн, що в `TrendIndicator.tsx`/Block 2. Module-level
`formatDateTime`/`formatTime`/`formatDate` функції отримали `intlLocale` як параметр (той
самий патерн, що `formatDate(s, intlLocale)` у `receipts/page.tsx`). `PosRevenueTrendChart`'s
Y-axis "к" (тисяча) суфікс — inline `locale === "en" ? "k" : "к"`, той самий патерн, що
`unit: locale === "en" ? "pcs" : "шт"` у `ProductForm.tsx` (без окремого JSON-ключа для
однієї літери).

**Спільні i18n-неймспейси (STATUS_LABEL-style):** `Dashboard.pos.{fiscalStatus,shiftStatus,
paymentType}`, `Dashboard.aiOrders.status` — усі колишні `Record<Enum,{label,...}>`
module-level консти. `Dashboard.pos.paymentType` спільний для `SaleDetailDrawer`+`SalesTable`
(один feature); `PosPaymentPieChart` (analytics feature) має свій локальний `paymentPie.
{cash,card}` — дублювання свідоме, той самий принцип, що `writeOffs.drawer.analyticsAction`
не перевикористав вже наявний `Dashboard.ui.productAnalyticsLink.title` у Block 2b. За тим
самим принципом "Аналітика товару" в ActionMenu-пунктах (`sales.table.actionMenu`,
`orders.table.actionMenu`, `aiOrders.review.actionMenu`) — локальні копії, не переспільнені.

## Верифікація

- `npm run build` — exit 0, усі 52 сторінки згенеровано (включно з 5 торкнутими).
  `ENVIRONMENT_FALLBACK` шум у логах — той самий pre-existing діагностичний код, не помилка
  (підтверджено в Block 2a/2b).
- `npm run lint` — чисто, 0 warnings/errors.
- `docker build -f frontend/Dockerfile frontend` (з кореня репо, синхронно) — exit 0,
  успішний production-образ (обов'язковий additional-гейт після інциденту з EUSAGE,
  TASK-377/378).
- Key-resolution скрипт (scratchpad, position-aware парсер за методологією TASK-379/380):
  прив'язує кожен `t("key")`/`t.has("key")` виклик до найближчого попереднього `const x =
  useTranslations(ns)` у файлі; для двох випадків, де `t` передається як параметр функції,
  оголошеної textually вище виклику (`shiftStatusMeta(status, t)` у `ShiftStatusCard.tsx`,
  `DeltaBadge({item, t})` у `AiOrderReview.tsx`) — fallback на єдине оголошення `t` у файлі
  (обидва файли мають рівно одне). Витягнув 222 літеральних виклики з 23 торкнутих файлів,
  усі резолвляться в обох `messages/{uk,en}.json`, 0 failures. 13 динамічних викликів
  (`t(status)`, `tPayment(sale.paymentType)`, `tStatus(order.status)`,
  `tConfidence(item.confidence)` і їх `.has()`-варіанти) звірив вручну проти TS union-типів
  і JSON-ключів — усі відповідають.

## Файли

`frontend/features/{sales,orders,pos,ai-orders}/**` (16 файлів), `frontend/features/
analytics/components/Pos{TopProductsTable,SummaryCards,RevenueTrendChart,PaymentPieChart,
CashierStatsTable}.tsx` (5 файлів), `frontend/app/(dashboard)/{sales,orders,pos,ai-orders,
analytics/pos}/page.tsx` (5 файлів), `frontend/messages/{uk,en}.json` (нові секції
`Dashboard.{sales,orders,pos,aiOrders}` + новий `Dashboard.analytics.pos`).

## Не в скоупі (свідомо)

- inventory/shelf/stock/catalog/receipts/transfers/write-offs/locations/stores — Block 2,
  вже зроблено.
- Решта `features/analytics/*` (CategoryStatusChart, ExpiryDonut, LossesByReasonChart,
  LossesByStoreChart) + `features/dashboard/*` — Block 4, наступний.
- Git push — за інструкцією, користувач сам зробить commit/push і простежить CI/деплой.
