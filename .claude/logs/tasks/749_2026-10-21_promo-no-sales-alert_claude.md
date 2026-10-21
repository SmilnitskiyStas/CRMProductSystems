# TASK-749 — Акційні товари без продажів (сповіщення + аналітика)

**Статус:** review · не закомічено · міграція не застосована до БД

## Модель акцій (з'ясовано в коді)
Три джерела, усі враховані: активні `Discount` (товар×магазин), опубліковані `PromotionCampaign` (+products/locations), опубліковані `PromotionRule` (JSON: itemIds + categoryIds, `scope.storeIds`, порожньо = усі магазини).

## Пороги (дефолти, `PromoNoSalesThresholds`; дзеркало у `worker/src/services/promo-no-sales.ts`)
- акція йшла ≥ 3 повних днів (до вчора), аналіз останніх ≤ 30 днів;
- залишок у магазині (не склад) ≥ 1;
- є ADU (`AduEffective`/`Adu30d`): очікується `adu×днів` (≥ 2 од.); сигнал, якщо продано < 20% очікуваного (`no_sales` при 0, інакше `low_sales`);
- без ADU: сигнал лише при 0 продажів за ≥ 7 днів;
- повторне сповіщення по тій самій (магазин, товар, акція) — не раніше ніж через 7 днів.

## Зміни
- Backend: `Application/Features/Analytics/PromoNoSales/*` (детектор, сервіс, DTO), `PromoNoSalesRepository` (без N+1: ~8 запитів на виклик), `GET /api/analytics/pos/promo-no-sales?store_id=` (`[RequireModule("analytics")]`, RLS/store-scope), сутність `PromoNoSalesAlert` + міграція `AddPromoNoSalesAlerts` (RLS tenant/provider/worker + store_scope), event `promo.no_sales`.
- Worker: `promo-no-sales.job.ts` (cron 07:00, SQL-кандидати, дедуп через `promo_no_sales_alerts`, один digest на магазин), `handlePromoNoSales` у `notification.job.ts` (telegram/email; store_manager/merchandiser лише своїх магазинів).
- Frontend: `PromoNoSalesBanner` на `/analytics/pos` (ховається, якщо порожньо), посилання на `/locations/{id}/zones/{zoneId}/shelves` (зона з планограми, інакше зона із залишком; інакше floor-plan), uk/en рядки.

## Перевірка
`dotnet build` ✅ · `dotnet test` 2754/2754 ✅ (11 нових у `PromoNoSalesDetectorTests`) · worker `tsc` ✅ · frontend `tsc` ✅, `npm run lint` ✅.
Не перевірено: міграція на живій БД, SQL worker-запиту (jsonb `?`), реальна відправка; UI в браузері.

## Відомі межі
Нема тестів репозиторію/worker (немає фреймворку у worker). `promo.no_sales` не додано в UI налаштувань сповіщень (дефолт telegram+email).
