# TASK-653 — Worker: `supplier-metrics-recompute.job.ts` (T6)

**Agent:** backend-developer · **Status:** done · **Date:** 2026-08-31
**Plan:** `eventual-whistling-rabbit.md` §«Worker-задача», risks 3/4/6 · **Depends on:** T2
(`20260831090731_AddSupplierPerformanceData`, already applied to the dev DB)

## Що зроблено

Новий нічний BullMQ cron-job, який нарешті наповнює колонки `supplier_metrics`, що з v4
існували наскрізно (entity → DTO → web/mobile UI), але ніколи не писались.

- `worker/src/jobs/supplier-metrics-recompute.job.ts` (новий) — структура точно за
  `loyalty-tier-recompute.job.ts`: прямий `pg`-пул через спільний `db`,
  `SET app.role = 'worker'` першим запитом на з'єднанні, цикл по постачальниках, чисті функції
  окремо + експортовані, фабрика `startSupplierMetricsRecomputeWorker()` з `completed`/`failed`.
- `worker/src/index.ts` — черга `supplier-metrics-recompute`,
  `upsertJobScheduler("supplier-metrics-recompute-cron", { pattern: "0 2 * * *" })`,
  `startSupplierMetricsRecomputeWorker()` у `main()`. **02:00** — чистий слот
  (cleanup 03:00, loyalty-tier 04:00, ai-order 05:00).

## Межі запису (load-bearing, в шапці job рамкою)

`UPDATE` пише РІВНО: `AvgDeliveryDays`, `DeliverySampleSize`, `DeliveryByRegion`,
`ResponseTimeHours`, `ResponseSampleSize`, `CancellationRate`, `OrderAccuracy`,
`AggregatesComputedAt` (+ `SupplierId`/`TenantId` лише на INSERT-гілці).

**Ніколи** `Rating` (власник — синхронний `MarketplaceRepository.UpsertMetricsRatingAsync`,
ADR-035, він же власник `UpdatedAt`), **ніколи** `QualityScore` (немає джерела даних).
`supplier_metrics` без `xmin` — безпеку дає саме те, що два писачі чіпають **неперетинні
колонки окремими statement'ами** (row-lock серіалізує). Будь-який майбутній «upsert усіх
метрик» повертає ризик клобера і потребує явного concurrency-токена.

Load-or-create: `INSERT ... ON CONFLICT ("SupplierId") DO UPDATE` — більшість постачальників
рядка не має взагалі (єдиний творець сьогодні — `UpsertMetricsRatingAsync` на першому відгуку).

## Логіка

- **Популяція:** `suppliers JOIN supplier_profiles`, без фільтра `IsPublic`.
  `suppliers."TenantId"` = `marketplace_orders."SupplierTenantId"` = ключ, який
  `UpsertMetricsRatingAsync` пише в `supplier_metrics."TenantId"` (звірено з
  `GetSupplierTenantIdAsync`).
- **Доставка:** вікно 365 днів, `Status='delivered'`, обидві мітки не null,
  `DeliveredAt >= ShippedAt` (відкидає clock-skew). SQL повертає **семпли по замовленню**,
  агрегація — у JS: overall avg (2 dp) + `GROUP BY` регіону, відсортований jsonb-масив.
  Замовлення з null-регіоном ідуть лише в overall → `DeliverySampleSize >= Σ sampleSize`.
- **Час відповіді:** медіана годин `first_client_msg → first_supplier_reply`, вікно 180 днів.
- **CancellationRate:** all-time (не вікно — скасування рідкісні, вікно б їх «випаровувало»),
  `cancelled / (delivered + cancelled)`. Замовлення в польоті не в знаменнику.
- **OrderAccuracy:** частка delivered-замовлень (у вікні) з фіналізованим (`Status='received'`)
  receipt, де всі рядки `QuantityReceived = QuantityOrdered`. Без фіналізованого receipt —
  **виключається зі знаменника** (відсутність доказу ≠ доказ недостачі).

**Рішення щодо форми SQL vs JS.** Агрегація доставки/відповіді робиться в JS, а не в SQL
(`AVG`/`PERCENTILE_CONT`) — на це прямо вказують сигнатури чистих функцій з брифу:
`computeAvgDeliveryDays(rows)` / `computeMedianResponseHours(hoursArray)` приймають семпли,
а `computeCancellationRate(cancelled, total)` / `computeOrderAccuracy(accurate, evaluated)` —
лічильники (їх SQL і рахує). Побічний плюс: усі 6 чистих функцій — на реальному шляху
виконання, а не мертвий дубль логіки, який майбутній тест-харнес перевірятиме вхолосту.
Фільтри/вікна/округлення — точно як у плані. JS-медіана = `PERCENTILE_CONT(0.5)` (лінійна
інтерполяція середини).

**Семантика «немає даних»** (задокументовано в коді): `sampleSize` завжди реальний **0**,
ніколи NULL («на основі 0» — факт, який UI може показати; NULL нерозрізненний із «job не
запускався»). `AvgDeliveryDays` / `ResponseTimeHours` / `CancellationRate` / `OrderAccuracy` —
NULL. `DeliveryByRegion` — NULL (а не `[]`), в парі з `AvgDeliveryDays = NULL`.
`AggregatesComputedAt` виставляється завжди, навіть коли даних нема.

## Verification

- `npx tsc --noEmit` + `npm run build` — чисто. `npm run lint` недоступний (`eslint` не в
  devDependencies worker'а; передіснуюча прогалина, не чіпав).
- **Dry-run SQL** на dev-БД (`crmproductsystems-postgres-1`, port 5435) під роллю
  `shelfguard_app_dev` (NOSUPERUSER/NOBYPASSRLS — не під `crm`-суперюзером, KI-027) з
  `SET app.role='worker'`: усі 5 запитів виконались без помилок. Реальні дані: 8
  постачальників із профілем, 0 marketplace-замовлень, 6 чат-сесій / 12 повідомлень,
  з них 2 сесії з відповіддю постачальника (2 неотвічені коректно не дали семпла).
- **Транзакційний seed + ROLLBACK** (8 замовлень: 2×UA-30, 1×UA-32, 1 без регіону,
  1 cancelled, 1 в польоті, 1 clock-skew, 1 поза вікном + 3 receipt'и):
  доставка → 4 семпли (3/4/2/1 дн.), skew/поза-вікном/в-польоті коректно відкинуті;
  cancellation → 1/7 = `0.1429`; accuracy → evaluated 2, accurate 1 = `0.5000` (draft-receipt
  виключено); upsert → `AvgDeliveryDays 2.50`, `n=4`,
  `[{UA-30,3.5,n2},{UA-32,2,n1}]`. **`Rating` 5.00 і `UpdatedAt` 2026-07-06 не змінились**,
  `QualityScore` лишився NULL. INSERT-гілка на постачальнику без рядка — ок. ROLLBACK чистий.
- **E2E реальним кодом**: скомпільований `startSupplierMetricsRecomputeWorker()` + одне
  завдання в чергу на dev-Redis (без реєстрації cron, щоб нічого не лишити контейнеру worker'а)
  → `suppliers: 8, with delivery data: 0, with response data: 2, region rows: 0`.
  У БД: 5 нових рядків метрик створено, 3 наявні оновлено з **збереженими** Rating 5.00/5.00/4.00
  і незміненими `UpdatedAt`; `ResponseTimeHours` 0.01 / 0.00 для двох постачальників з чатом.
  Тимчасові ключі `bull:supplier-metrics-recompute*` з Redis прибрано.

## Обмеження вимірювання (у шапці job, плану ризики 3/4)

1. `DeliveredAt` існує лише після фіналізації receipt клієнтом (ADR-033) → відвантажені, але
   не прийняті замовлення невидимі метриці; середнє зміщене до сумлінних клієнтів.
   `ConfirmedAt` немає → «відповідь на замовлення» не вимірювана взагалі, лише чат.
2. `DestinationRegionCode` — снапшот, NULL для всієї історії → per-region стартує з n=0.
   UI зобов'язаний показувати «на основі N», інакше drill-down виглядає зламаним.
3. Медіана відповіді рахує лише сесії, де постачальник **врешті** відповів — той, хто ігнорує
   половину тредів, виглядає як той, хто відповідає на всі. «Response rate» поза скоупом.

## Не зроблено (свідомо, поза скоупом T6)

Тест-харнес для `worker/` не створювався (його в проєкті немає); чисті функції експортовані,
щоб його можна було додати без переробки job'а. Docs/ADR — задача T15.
