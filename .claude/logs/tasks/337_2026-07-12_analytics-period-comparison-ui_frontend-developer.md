# TASK-337 — Analytics period comparison UI (ADR-016)

**Agent:** frontend-developer · **Date:** 2026-07-12 · **Status:** done

## Що зроблено
- `frontend/components/ui/TrendIndicator.tsx` — новий shared компонент, сам рахує % зміни з `current`/`previous`; зелена стрілка вгору / червона вниз / сірий тире (previous null/0). `format` (number/currency/percent) впливає на hover-tooltip.
- `frontend/components/ui/DateRangePicker.tsx` — новий компонент на нативних `<input type="date">` (react-day-picker в проекті відсутній, нову бібліотеку не додавав) + `Switch` для compare-режиму. Авто-обчислює попередній період тим самим алгоритмом зсуву, що бекенд (`compareTo=from-1d`, `compareFrom=compareTo-(to-from)`), редаговане прев'ю. Експортує `computePreviousPeriod`, `toDateInputValue`, `parseDateInputValue`.
- **Dashboard**: `dashboard/types.ts`/`api/dashboard.ts`/`hooks/useDashboard.ts` — додано `getExpirySummaryCompare` (`/api/analytics/expiry-summary/compare`, camelCase `storeId`) і `getWeeklyKpi` (`/api/analytics/dashboard/weekly-kpi`). `StatsCards.tsx` — TrendIndicator під кожною карткою. Нова `WeeklyKpiCards.tsx` (Продажі/Виручка/Списання, 7д vs 7д) підключена на `dashboard/page.tsx`.
- **Analytics**: `analytics/types.ts` + `api/analytics.ts` — TS-overloads на `getWriteOffs`/`getLosses` (`compare` omitted/false → стара flat DTO без змін; `compare:true` → `{current, comparison, totalLossPercentChange}`). Нові хуки `useWriteOffAnalyticsCompare`/`useLossesCompare`. `analytics/page.tsx` — доданий `DateRangePicker` (раніше фільтра дат не було взагалі), write-offs/losses тепер scoped по діапазону, TrendIndicator на "Всього документів"/"Загальні збитки"/"Всього списань".
- **POS Analytics**: `api/pos-analytics.ts` — той самий overload-патерн для `getSummary`/`getRevenueTrend`. Нові хуки `usePosSummaryCompare`/`usePosRevenueTrendCompare`. `analytics/pos/page.tsx` — примітивні `<input type="date">` замінено на `DateRangePicker`. `PosSummaryCards` приймає `previous` і показує TrendIndicator для виручки/транзакцій. `PosRevenueTrendChart` — накладає comparison-лінію (пунктир, `connectNulls`) поверх поточної area-серії; вирівнювання точок за offset від старту діапазону (sparse-масиви, не по індексу — як застережено в handoff), не по календарній даті.

## Верифікація
- `npx tsc --noEmit` — 0 помилок. `npm run build` — success (51/51 сторінок, включно з `/analytics`, `/analytics/pos`, `/dashboard`).
- Живий прогін у браузері (piali docker compose up, backend+frontend dev, логін `manager@demo.local`/`password`):
  - Dashboard: StatsCards показують нейтральний тире (previous=null, снепшот воркера ще не існує — очікувано), WeeklyKpiCards рендерять без помилок.
  - `/analytics/pos`: DateRangePicker + toggle працюють, авто-compare діапазон обчислився правильно (`06/12–07/12` → compare `05/12–06/11`, звірено вручну з формулою); дані пусті (демо-сід без POS-транзакцій) → коректний neutral dash, без крашу.
  - `/analytics`: увімкнув compare — секція "Списання" показала реальний **червоний -100.0%** (current=0 < previous>0, демо-дані списань є за попередній період) — підтверджує коректний розрахунок і колір стрілки на живих даних.
  - Консоль браузера чиста на обох сторінках.

## Не зроблено / нотатки
- POS revenue-trend overlay (пунктирна лінія) не побачив візуально з реальними даними — демо-сід не містить POS-транзакцій; логіка вирівнювання по offset перевірена лише кодом/типами, не на живому графіку з двома серіями.
- `expiry-summary/compare` previous буде `null` до першого запуску `stock-snapshot` cron (00:10) — очікувано за handoff, не помилка.
