# TASK-380: Dashboard i18n (uk/en) — Block 2b: Receipts/Transfers/Write-offs/Locations/Stores

**Agent:** frontend-developer
**Date:** 2026-07-17
**Status:** done

## Зроблено

Переклав усі UI-рядки через існуючий `useTranslations`/`DashboardIntlProvider` (Block 1,
TASK-376) — жодного нового provider-wiring, тільки нові секції в
`frontend/messages/{uk,en}.json`: `Dashboard.receipts`, `Dashboard.transfers`,
`Dashboard.writeOffs`, `Dashboard.locations`, `Dashboard.stores`.

**24 файли зі стрічками:**
- `features/receipts/{types.ts,components/ReceiptStatusBadge.tsx}` — `RECEIPT_STATUS_LABEL`
  → `Dashboard.receipts.status.*` (той самий патерн, що `STATUS_LABEL` у Block 2a),
  `RECEIPT_STATUS_COLOR` лишився (кольори не мовозалежні).
- `features/transfers/types.ts`, `features/write-offs/types.ts` — аналогічно:
  `TRANSFER_STATUS_LABEL`/`WRITE_OFF_STATUS_LABEL`/`WRITE_OFF_REASON_LABEL` видалені,
  `*_COLOR` лишились. `WRITE_OFF_REASON_LABEL` → `WRITE_OFF_REASON_VALUES` (values-only
  const, консистентно з `ITEM_TYPE_VALUES` з Block 2a), лейбл резолвиться через
  `tReason.has(reason) ? tReason(reason) : reason`.
- `features/locations/types.ts` — `LOCATION_TYPE_LABELS` → `LOCATION_TYPE_VALUES` +
  `Dashboard.locations.types.*`.
- `features/locations/components/{LocationFormDialog,ZoneDialog,FloorPlanCanvas,
  FloorPlanSidePanel}.tsx` — повний переклад. Zod-схема в `LocationFormDialog` винесена в
  `buildSchema(t)` + `useMemo` (той самий патерн, що `buildProductSchema(t)` в Block 2a).
  `STATUS_CONFIG` в `FloorPlanCanvas.tsx` втратив поле `label` (залишились тільки
  color/bg/border) — лейбл резолвиться через `useTranslations("Dashboard.locations.zoneStatus")`.
- `features/stores/components/{FloorPlanCanvas,FloorPlanSidePanel}.tsx` — це не
  re-export shim, а окрема (застаріла/дубльована) копія тих самих компонентів — перекладена
  окремо під `Dashboard.stores.*` namespace. `features/stores/{types,api/stores,
  hooks/useStores,hooks/useFloorPlan}.ts` — чисті re-export shims на `features/locations/*`,
  0 кирилиці, не займав.
- 9 сторінок: `receipts/{page,[id]/page}.tsx`, `transfers/page.tsx`, `write-offs/page.tsx`,
  `locations/{page,[id]/floor-plan/page,[id]/zones/[zoneId]/shelves/page}.tsx`,
  `floor-plan/page.tsx`, `stores/[id]/floor-plan/page.tsx` — повний переклад, включно з
  toast-повідомленнями, zod/validation-текстом, ActionMenu/DetailDrawer-контентом.

**Locale-aware formatting:** усі `toLocaleDateString/toLocaleString("uk-UA", ...)` у
торкнутих сторінках замінені на `locale === "en" ? "en-US" : "uk-UA"` через `useLocale()`
(той самий inline-патерн, що в `TrendIndicator.tsx`/Block 2a) — суми списань/цін
(`toLocaleString` для ₴) теж стали locale-aware, не тільки дати.

**Судженнєвий момент (без потреби узгодження, задокументовано):** в
`features/locations/components/FloorPlanCanvas.tsx` tooltip-рядки на floor plan мали
хардкод англійських слів "Safe"/"Warning"/"Critical"/"Expired" навіть в україномовній
збірці (залишок недоробленого перекладу в оригінальному коді) — замінив на
`t("safe")`/`t("warning")`/... з того ж `Dashboard.locations.zoneStatus`, що й badge-лейбл
на самій зоні, щоб uk-версія була послідовно українською. Те саме в `features/stores/
components/FloorPlanCanvas.tsx`.

**Спільні i18n-неймспейси (STATUS_LABEL-style):** `Dashboard.{receipts,transfers,
writeOffs}.status.*`, `Dashboard.writeOffs.reason.*`, `Dashboard.locations.{types,
zoneTypes,zoneStatus}.*` — усі колишні `Record<Status,string>` module-level консти. Решта
дубльованого тексту (наприклад однакове "Загальна інформація"/"Аналітика товару" в кількох
drawer-секціях) лишена локальною в межах відповідного namespace, per Block 2a конвенція.

## Верифікація

- `npm run build` — exit 0, усі 52 сторінки згенеровано (включно з 9 торкнутими).
  `ENVIRONMENT_FALLBACK` шум у логах — той самий pre-existing діагностичний код, не помилка
  (підтверджено в Block 2a).
- `npm run lint` — чисто, 0 warnings/errors.
- `docker build -f frontend/Dockerfile frontend` (з кореня репо) — успішно (обов'язковий
  additional-гейт після інциденту з EUSAGE, TASK-377/378).
- Key-resolution скрипт (scratchpad, за методологією TASK-379): position-aware парсер —
  зіставляє кожен `t("key")`/`t.has("key")` виклик з НАЙБЛИЖЧИМ попереднім `const x =
  useTranslations(ns)` у файлі (на відміну від naive global-regex підходу, який плутає
  namespace коли один файл має кілька функцій з локальним `const t = useTranslations(...)`,
  напр. `ReceiptDetail` vs `ReceiptsPage` в одному файлі) — витягнув 233 літеральних виклики
  з 16 торкнутих файлів, усі резолвляться в обох `messages/{uk,en}.json`. 3 динамічних
  (template-literal `status.${value}`) + 1 переданий як параметр функції (`buildSchema(t)`)
  звірив вручну — усі відповідають.

## Файли

`frontend/features/{receipts,transfers,write-offs,locations,stores}/**` (15 файлів),
`frontend/app/(dashboard)/{receipts,receipts/[id],transfers,write-offs,locations,
locations/[id]/floor-plan,locations/[id]/zones/[zoneId]/shelves,floor-plan,
stores/[id]/floor-plan}/page.tsx` (9 файлів), `frontend/messages/{uk,en}.json` (нові секції
`Dashboard.{receipts,transfers,writeOffs,locations,stores}`).

## Не в скоупі (свідомо)

- inventory/shelf/stock/catalog — Block 2a, вже зроблено.
- Решта фіча-модулів (sales, orders, pos, marketplace, і т.д.) — наступні блоки плану.
- Git push — за інструкцією, користувач сам зробить commit/push і простежить CI/деплой.
