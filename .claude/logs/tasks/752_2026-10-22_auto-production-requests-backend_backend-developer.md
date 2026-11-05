# TASK-752 — Авто-аналіз потреби власного виробництва, backend (Етап 3)

Статус: review. Не закомічено.

## Що зроблено
- `ProductionDemandService` + `ProductionDemandCalculator` (Application/Features/Production), `IProductionDemandRepository` (+ EF impl).
- Самовиробний товар = `OutputItemId` активного рецепта. Виробнича локація = активна `Type=="production"` (кілька -> та, де останній виробничий ордер, далі за назвою; немає -> 422).
- Потреба на магазин+товар: базові дані з `IOrderCalcService.CalculateAsync` (буфер, safety, коефіцієнти подія/погода/промо); залишок = лише непрострочені партії; у дорозі = чернетки приймань + marketplace + transfers `in_transit`; мінус відкриті заявки (submitted/accepted/partial; чернетки не рахуються).
  `deficit = BufferTotal + Safety - onHand - inTransit - openRequests; raw = deficit x (event x weather x promo); need = ceil(raw) якщо raw >= 1, інакше 0`.
- Source: `auto_promo`, якщо подія/промо-коефіцієнт > 1 (погода сама - ні), інакше `auto_buffer`.
- Ідемпотентність: одна авто-чернетка (Source != manual, Status draft) на (магазин, вир. локація); оновлюється на місці (рядки додаються/оновлюються/видаляються), без змін - не переписується; потреба зникла -> чернетка `cancelled`. Ручні чернетки не чіпаються; ручні правки авто-чернетки перезаписуються. `NeededBy = сьогодні + 2 дні` (константа `DefaultLeadDays`), прострочену оновлює.
- `ProductionRequestLine.AutoReason` (текст) + міграція `AddProductionRequestLineAutoReason` (застосована до dev-БД); `ProductionRequest.Source` стало settable; `ProductionRequestLineDto.AutoReason`.
- `POST /api/production-requests/auto-generate` (AtLeastStoreManager, body `{storeLocationId?}`) -> `ProductionRequestDetailDto[]` (створені/змінені).
- Нотифікація `production_request.auto_draft` (outbox, StoreId = магазин, лише при створенні нової чернетки) + запис у `DISPATCH_EVENT_ROLES`/icons у worker.
- Worker: `production-demand.job.ts`, cron `30 5 * * *`, service-account login як в ai-order.

## Build / tests
`dotnet test` (Release) 2811/2811; `npx tsc --noEmit` у worker чистий. Debug-білд Api блокується запущеним ShelfGuard.Api (PID з цього worktree) - використано `--configuration Release`.

## Обмеження
- Worker service account одно-тенантний (як ai-order): аналізується лише його тенант.
- Промо/подія береться на сьогодні (як в OrderCalc), майбутні акції в межах lead time не враховуються.
- Товар без `ProductBuffer` у магазині (немає ADU/буфера) пропускається.
- Нових таблиць немає, RLS не змінювався.
