# TASK-382: Dashboard i18n (uk/en) — Block 4: Analytics & Home Dashboard

**Agent:** frontend-developer
**Date:** 2026-07-17
**Status:** done

## Зроблено

Переклав через існуючий `useTranslations`/`DashboardIntlProvider` (Block 1, TASK-376) —
жодного нового provider-wiring. Нові секції в `frontend/messages/{uk,en}.json`: сусідні
ключі під уже наявним `Dashboard.analytics` (Block 3 залишив там лише `pos`), і новий
top-level `Dashboard.dashboard` (домашня сторінка).

**11 TSX-файлів + 2 JSON:**
- `features/analytics/components/{CategoryStatusChart,ExpiryDonut,LossesByReasonChart,
  LossesByStoreChart}.tsx` — 4 файли (не 9, як оцінював план; решта 5 Pos*-файлів вже
  зроблені в Block 3). `PosRevenueTrendChart.tsx` свідомо не чіпав — його 1 "кирилична"
  літера це вже перекладений в Block 3 inline-суфікс `"k"/"к"` для тисяч, не хардкод.
  `CategoryStatusChart`: `dataKey`/`Bar` використовували самі кириличні лейбли як ключі
  об'єкта (`Норма`, `Попередження`, ...) — замінив на нейтральні `safe/warning/critical/
  expired` + `name={tStatus(...)}` на кожному `<Bar>` (recharts бере підпис з `name`, не
  з `dataKey`), той самий рендер, без кирилиці в коді.
- `app/(dashboard)/analytics/page.tsx` (610 рядків, найбільша сторінка) — 5 секцій
  (expiry summary, write-offs, by-zone, by-category, losses-by-store), усі toLocaleString
  → `intlLocale`, `REASON_LABELS` модульний конст видалений (резолвиться через
  `Dashboard.analytics.reason` + `.has()`-guard).
- `features/dashboard/components/{StatsCards,WeeklyKpiCards,StoreMap,AttentionTable,
  QuickActions}.tsx` — усі 5. `StatsCards.tsx` не мав кирилиці (лейбли "Safe/Warning/..."
  вже хардкоджено англійською) — судженнєвий момент без потреби узгодження: переклав і
  його, бо (а) явно один з "8 файлів" фічі в постановці задачі, (б) той самий домен-enum
  (FEFO-статус), що й решта Block 4, і без цього укр-локаль показувала б англійські слова.
  `WeeklyKpiCards`/`StatsCards`: `CARDS`-масиви перенесені з module-level в тіло компонента
  (лейблам потрібен `useTranslations()`). `AttentionTable`: `FILTERS: {label,value}[]` →
  `FILTER_VALUES: value[]`, лейбл рахується при рендері (`tStatus(value)` / `t("filterAll")`).
  `QuickActions.tsx` (783 рядки, найбільший файл) — 5 під-компонентів (Modal/CriticalModal/
  WriteOffDrawer/OrderDrawer/ItemDetailDrawer/головний), кожен зі своїм локальним
  `const t = useTranslations(...)`; два модульні `STATUS_LABEL`-конст видалені (той самий
  split, що `shiftStatusMeta` у Block 3), лейбл резолвиться через `tStatus(item.status)` —
  без `.has()`-guard, бо `item.status: ItemStatus` — закритий union з рівно 4 значень, що
  збігаються з ключами `Dashboard.dashboard.status`.
- `app/(dashboard)/dashboard/page.tsx` — заголовок/підзаголовок, невелика сторінка.

**Locale-aware formatting:** усі `toLocaleString/toLocaleDateString("uk-UA", ...)` →
`intlLocale` (`locale === "en" ? "en-US" : "uk-UA"` через `useLocale()`) — 6 місць у
`analytics/page.tsx`, по 2 у `LossesByReasonChart`/`LossesByStoreChart`, 3 у
`WeeklyKpiCards`, 2 (`toLocaleDateString`) у `QuickActions`' `ItemDetailDrawer`.

**Спільні i18n-неймспейси (enum-style, той самий принцип, що Block 2/3):**
- `Dashboard.analytics.status` (safe/warning/critical/expired/needsVerification) —
  спільний для `CategoryStatusChart`, `ExpiryDonut`, `analytics/page.tsx` (metric cards +
  таблиці; абревіатура заголовка "Попередж." — окремий `page.headers.warningShort`, бо
  тільки цей один лейбл скорочується в таблицях).
- `Dashboard.analytics.reason` (expired/damaged/theft/production_loss/other) — спільний
  для `LossesByReasonChart` + `analytics/page.tsx`. Локальна копія, не перевикористання
  вже наявного `Dashboard.writeOffs.reason` (той самий текст, інша фіча) — узгоджено з
  прецедентом Block 2 (`locations.zoneStatus` vs `stores.zoneStatus` — теж дублікати).
- `Dashboard.dashboard.status` (safe/warning/critical/expired) — спільний для
  `StatsCards`/`StoreMap`/`AttentionTable`/`QuickActions` (усі 4 компоненти в одній фічі).
- `AttentionTable`'s ActionMenu-пункт "Аналітика товару" — перевикористав уже наявний
  `Dashboard.ui.productAnalyticsLink.title` (крос-фічовий `ui`-неймспейс, той самий
  паттерн, що `AccessDenied`/`TrendIndicator`), замість ще однієї локальної копії.

## Верифікація

- `npm run build` — exit 0 (перевірено двічі, окремим синхронним викликом з явною
  перевіркою коду завершення). Усі 52 сторінки згенеровано, включно з `/analytics`
  (10.6 kB) і `/dashboard` (10.8 kB). `ENVIRONMENT_FALLBACK`-шум у логах — той самий
  pre-existing діагностичний код під час static generation, не помилка (підтверджено в
  Block 2a/2b/3, не звязаний з цими змінами).
- `npm run lint` — exit 0, 0 warnings/errors.
- `docker build -f frontend/Dockerfile frontend` (з кореня репо, синхронно, exit code
  перевірено напряму) — exit 0. Лог підтверджує повний прогін усередині контейнера:
  `npm ci` → `Compiled successfully` → `Generating static pages (52/52)` → той самий route
  table з `/analytics`/`/dashboard`, що й локальний білд.
- Key-resolution скрипт (scratchpad, position-aware парсер, той самий підхід, що
  Block 2b/3: прив'язує кожен `t("key")`/`t.has("key")` до найближчого попереднього
  `const x = useTranslations(ns)` у файлі за позицією в тексті, критично для
  `QuickActions.tsx`, де 5 функцій кожна оголошує свій локальний `t`) — 163 літеральних
  виклики з 11 файлів, усі резолвляться в обох `messages/{uk,en}.json`, 0 missing, 0
  dynamic-без-перевірки, 0 unresolved-scope. Динамічні виклики (`tStatus(item.status)`,
  `tStatus(zone.status)`, `tStatus(s)`, `tStatus(value)`, `tReason.has(r.reason)`) звірені
  вручну: 4/5 ключів `Dashboard.{dashboard,analytics}.status` і 5 ключів
  `Dashboard.analytics.reason` присутні ідентично в обох файлах (`Object.keys` diff = 0).

## Файли

`frontend/features/analytics/components/{CategoryStatusChart,ExpiryDonut,
LossesByReasonChart,LossesByStoreChart}.tsx`, `frontend/app/(dashboard)/analytics/page.tsx`,
`frontend/features/dashboard/components/{StatsCards,WeeklyKpiCards,StoreMap,AttentionTable,
QuickActions}.tsx`, `frontend/app/(dashboard)/dashboard/page.tsx`,
`frontend/messages/{uk,en}.json` (нові `Dashboard.analytics.{status,reason,
categoryStatusChart,expiryDonut,lossesByReasonChart,lossesByStoreChart,page}` +
новий top-level `Dashboard.dashboard.{status,page,statsCards,weeklyKpi,storeMap,
attentionTable,quickActions}`).

## Не в скоупі (свідомо)

- Усе перекладене в Block 1-3, включно з `Pos*.tsx` в `features/analytics/components/`.
- Решта фіча-модулів (Block 5+), лендінг (Block 0).
- Git commit/push — за інструкцією, користувач сам комітить і стежить за CI/деплоєм.
