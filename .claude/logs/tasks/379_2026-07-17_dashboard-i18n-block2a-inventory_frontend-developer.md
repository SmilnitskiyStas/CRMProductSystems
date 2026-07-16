# TASK-379: Dashboard i18n (uk/en) — Block 2a: Inventory/Shelf/Stock/Catalog

**Agent:** frontend-developer
**Date:** 2026-07-17
**Status:** done

## Зроблено

Переклав усі UI-рядки (13 файлів мали кирилицю з 15 перевірених) через існуючий
`useTranslations`/`DashboardIntlProvider` (Block 1, TASK-376) — жодного нового
provider-wiring, тільки нові ключі в `frontend/messages/{uk,en}.json`.

**Feature `inventory`** (`ProductForm.tsx`, `ProductsTable.tsx`, `ProductAnalyticsTab.tsx`):
- `ITEM_TYPE_OPTIONS`/`ITEM_TYPE_LABELS`/`PERISHABILITY_CLASS_OPTIONS` (модульні консти з
  укр. текстом, розшарені між 3 файлами) → `ITEM_TYPE_VALUES`/`PERISHABILITY_CLASS_VALUES`
  (тільки значення) + `Dashboard.inventory.itemTypes.*` / `form.perishability.*`, лейбл
  резолвиться через `tItemTypes(value)` в кожному компоненті.
- zod-схема форми (`productSchema`) мала повідомлення валідації в module-scope, де хук
  недоступний → винесена в `buildProductSchema(t)`, будується всередині компонента через
  `useMemo` (той самий патерн, що `buildNavGroups(t)` у `Sidebar.tsx`, Block 1).
- `ProductAnalyticsTab.tsx` (найважчий файл, 605 рядків): `LINES`/`ZONES`/`BUFFER_LINES`/
  `RANGES`/`MOVEMENT_LABELS` — module-level масиви з укр. лейблами → `buildLines(t)`/
  `buildMovementLabels(t)` + `zoneForStock()` helper (уніфікував 3 копії однакової
  if/else-логіки визначення зони в `CustomTooltip`, `currentZone` useMemo, легенді).
- Спільний `Dashboard.inventory.fields.*` namespace — лейбли полів (Мін./Макс. залишок,
  Буфер безпеки, секції drawer'а і т.д.) буквально ідентичні між `ProductsTable`'s drawer,
  `[id]/page.tsx` і buffer-лейблами в аналітиці — розшарив замість дублювання (сам вихідний
  код теж використовував один спільний JS-об'єкт для частини цих лейблів).
- `Каталог товарів` → "Product Catalog", `Товар` (itemType) → "Product" (узгоджено з
  усією рештою фронтенду, де ця сутність всюди `Product`/`productsApi`/`/inventory` —
  v4 rename Product→Item стосується backend-домену, не UI-термінології тут).

**Feature `shelf`** (`StatusBadge.tsx`, `StockFilters.tsx`, `StockTable.tsx`,
`AddBatchForm.tsx`): `STATUS_LABEL` (спільний `Record<BatchStatus,string>` з `types.ts`,
імпортувався в 2 файли) → видалений, лейбл резолвиться через
`useTranslations("Dashboard.shelf.status")` напряму по значенню `BatchStatus` (ключі
збігаються 1:1) — `StatusBadge.tsx` не мав `"use client"`, додав (тепер викликає хук).

**Feature `catalog`**: 0 кирилиці (тільки `types.ts`/`api/`/`hooks/`, без компонентів) —
пропущено, підтверджено grep.

**`stock` (немає окремого `features/stock/`)**: тільки
`app/(dashboard)/stock/page.tsx` — перекладено, під `Dashboard.stock.*`.

**Locale-aware formatting (виходить за межі "просто перекласти", але без цього
англомовна демка все одно показувала б українські назви місяців):** усі
`toLocaleDateString/toLocaleString("uk-UA", ...)` у 4 торкнутих файлах замінені на
`locale === "en" ? "en-US" : "uk-UA"` через `useLocale()` — точно той самий inline-патерн,
що вже є в `TrendIndicator.tsx`/`SupportChatWidget.tsx` (Block 1), нового helper-файлу не
створював. Дефолтне значення `unit` у формі нового товару ("шт" → "pcs" для en) — теж
locale-aware.

**Пропущено свідомо:** кольорове виділення назви товару в тексті діалогу видалення
(`ProductsTable.tsx` `DeleteDialog`) — оригінал обгортав `{product.name}` в
`<span style={{color:...}}>` всередині речення; спрощено до plain `t("body", {name})`
замість `t.rich(...)` з тегом — менший ризик під час build/docker-гейту, візуально
непомітна відмінність (сірий колір імені товару в реченні зникає, решта форматування
без змін).

## Верифікація

- `npm run build` — 0 помилок, exit 0, всі 52 сторінки (`/inventory`, `/inventory/[id]`,
  `/stock` включно). Шум `ENVIRONMENT_FALLBACK` у логах — той самий pre-existing
  діагностичний код з Block 1, не помилка.
- `npm run lint` — чисто.
- `docker build -f frontend/Dockerfile frontend` — успішно, image експортовано
  (`exporting to image ... DONE`).
- Скрипт-звірка (scratchpad, за прикладом TASK-376): витягнув усі 226 літеральних
  `t("...")`/`tXxx("...")`/`tXxx.has("...")` викликів з 12 торкнутих файлів, звірив
  namespace+key проти обох `messages/{uk,en}.json` — знайшов 1 реальний баг
  (`StockTable.tsx` drawer викликав `t("barcode")`, ключа не було в
  `Dashboard.shelf.stockTable.drawer`) → виправив в обох locale-файлах → повторний прогін:
  226/226 резолвляться. 5 динамічних (template-literal) викликів (`zones.${key}`,
  `ranges.${RANGE_KEY[days]}`, `perishability.${value}`) звірив вручну проти доменів
  значень (ZoneKey/RANGE_KEY/PERISHABILITY_CLASS_VALUES) — всі відповідають.
- Після фіксу — build/lint/docker build перепрогнав ще раз, всі зелені.
- Dev-сервер + браузер: `/login` рендериться коректно (англ. за browser-locale фолбеком,
  консоль без помилок) — підтверджує, що новий вміст `messages/{uk,en}.json` не ламає
  `DashboardIntlProvider`. Живий логін на `/inventory`/`/stock` НЕ робив — локальний
  backend (`localhost:5000`) не піднятий (тільки Docker Postgres/Redis), а піднімати
  повний ASP.NET Core + EF seed стек заради двох сторінок непропорційно scope; це
  прямо позначено як "якщо можливо" в задачі. Compile-time доказ (build + type-check +
  key-resolution script) покриває основний ризик (typo в namespace/ключі).

## Файли

`frontend/features/inventory/{types.ts,components/ProductForm.tsx,
components/ProductsTable.tsx,components/ProductAnalyticsTab.tsx}`,
`frontend/features/shelf/{types.ts,components/StatusBadge.tsx,components/StockFilters.tsx,
components/StockTable.tsx,components/AddBatchForm.tsx}`,
`frontend/app/(dashboard)/{stock/page.tsx,inventory/page.tsx,inventory/[id]/page.tsx}`,
`frontend/messages/{uk,en}.json` (нові секції `Dashboard.inventory`, `Dashboard.shelf`,
`Dashboard.stock`).

## Не в скоупі (свідомо)

- `receipts`/`transfers`/`write-offs`/`locations`/`stores` — Block 2b, наступна задача.
- `features/catalog/*` — 0 кирилиці, нічого перекладати.
- Мобільний застосунок — поза хвилею per план.
