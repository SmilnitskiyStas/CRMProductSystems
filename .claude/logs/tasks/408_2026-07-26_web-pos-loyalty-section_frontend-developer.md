# TASK-408: Web POS — read-only "Лояльність" block (manager view)

**Agent:** frontend-developer
**Date:** 2026-07-26
**Status:** done (frontend-only; UI stays hidden until a follow-up backend task closes a mapping gap — see below)

## Контекст

Task #3 (frontend half, mobile-developer runs the other half in parallel) з послідовності
агентів плану `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` (Фаза 0, розділ "Зміни
в POS" → "Web"). Слідує за TASK-405 (backend). Scope — тільки
`frontend/features/pos/` + `frontend/messages/{en,uk}.json`, read-only UI, без
checkout/форм (веб POS ніколи не мав checkout-UI, це не змінюється).

## Знахідка: фактичний backend-контракт відрізняється від брифу — задокументовано, не вигадано

Прочитав `backend/ShelfGuard.Application/Features/Pos/Dtos/PosDtos.cs` +
`PosService.cs` напряму (не покладався на бриф) і живо перевірив обидва ендпоінти POS
(`POST /api/pos/sales`, `GET /api/pos/sales`) через реальний dev-стек (backend+frontend+
Postgres, seeded `manager@demo.local`, реальний товар/шифт/продаж, деталі нижче):

1. **`SaleDto` не має `CustomerId`/`CustomerName` взагалі** — ні на `CreateSaleAsync`
   (`PosService.cs:534-548`), ні на `GetSalesForShiftAsync` (`PosService.cs:682-700`).
   `PosTransaction.CustomerId` ДІЙСНО пишеться при створенні продажу (`PosService.cs:324`),
   але ніколи не мапиться назад у DTO жодним з двох шляхів. Тобто "ім'я клієнта +
   посилання на картку" (п.1 брифу) неможливо показати без розширення backend DTO —
   це НЕ поле, яке "поки що null", це поле, якого не існує в контракті. **Потрібна окрема
   backend-задача** (додати `CustomerId`/`CustomerName` в `SaleDto` + замапити в обох місцях).
2. **`loyaltyAccrued`/`loyaltyRedeemed`/`loyaltyBalance` — реальні поля `SaleDto`
   (додані TASK-405), але заповнюються лише в `CreateSaleAsync`** (миттєва відповідь на
   `POST /api/pos/sales` — тобто те, що бачить мобільна каса одразу після продажу).
   **`GetSalesForShiftAsync` (те, що реально викликає веб — `GET /api/pos/sales`,
   `frontend/features/pos/api/pos.ts:getShiftSales`) НІКОЛИ не заповнює ці три поля** —
   живо підтверджено: створив реальний продаж через API, потім забрав його через
   `GET /api/pos/sales` — усі три поля повернулись `null`, хоча в JSON вони присутні
   (не відсутні, просто null). Отже на вебі ці суми сьогодні ЗАВЖДИ `null`, незалежно від
   того, чи був насправді нарахований/списаний бонус. Потрібне (окреме) backend-розширення:
   замапити `LoyaltyLedgerEntry` (за `PosTransactionId`) у `GetSalesForShiftAsync`.
3. **`features/customers/` не має deep-link на конкретного клієнта** — `CustomerDetail`
   відкривається як drawer через client-side `useState` в
   `app/(dashboard)/customers/page.tsx` (`selected`/`setSelected`), нема `/customers/[id]`
   роуту й нема `useSearchParams` для попереднього відкриття. Навіть після (1) "перехід на
   картку клієнта" зможе вести лише на список `/customers`, не на конкретний запис, якщо
   це також не буде додано окремо.

Дотримався інструкції "не вигадуй дані": не додав `customerId`/`customerName` в
TypeScript-тип (бо їх справді немає у фактичному wire-контракті — додавання було б
вигадкою, а не "опційним полем з бекенду"), і секція лояльності рендериться виключно за
реальними (нехай і завжди-null сьогодні) полями `loyaltyAccrued/Redeemed/Balance`.

## Зроблено

**`frontend/features/pos/types.ts`** — додав `loyaltyAccrued/loyaltyRedeemed/loyaltyBalance:
number | null` на `SaleDto` (стиль файлу: `| null`, без `?:`, як `fiscalNumber`/`closedAt`
у сусідніх інтерфейсах) + doc-коментар з повним поясненням п.2 вище. Додав
`saleHasLoyaltyActivity(sale)` — єдина точка правди "чи є дані лояльності" (`!= null` по
всіх трьох полях), щоб `SalesTable` і `SaleDetailDrawer` завжди узгоджувались і логіка не
дублювалась.

**`SaleDetailDrawer.tsx`** — нова `DrawerSection` "Лояльність" між "Загальна інформація" і
"Товари", рендериться лише коли `saleHasLoyaltyActivity(sale)` true (сьогодні — ніколи,
до backend-фіксу п.2). Показує нараховано/списано/баланс-після через наявні
`DrawerField`/`DrawerGrid` (той самий патерн, що вже є в файлі — не міняв
`DetailDrawer.tsx`, `title` там типізований як `string`, тому іконку в заголовок секції не
додавав). Розлогий код-коментар прямо над секцією документує обидві знахідки (1) і (3) з
точними шляхами файлів — щоб наступний backend/frontend агент не переоткривав це заново.

**`SalesTable.tsx`** — індикатор (іконка `Gift`, lucide-react, вже в package.json) поруч з
номером чека, рендериться за тим самим `saleHasLoyaltyActivity`. Обгорнув у `<span
title=...>` замість передачі `title` напряму в `<Gift>` — `LucideProps` в
lucide-react@0.312.0 не приймає `title` (спіймано `tsc`, виправлено).

**Переклади** — `frontend/messages/en.json`/`uk.json`: нові ключі
`Dashboard.pos.saleDetail.loyalty.{title,accrued,redeemed,balance}` і
`Dashboard.pos.salesTable.loyaltyIndicator`, дзеркально в обох файлах, той самий рівень
вкладеності що сусідні ключі. Перевірив `node -e "JSON.parse(...)"` на обох файлах —
валідні. **Не займав** незалежний, вже наявний до цієї задачі uncommitted diff у цих же
файлах (activity-log labels, `actions.user.*`/`actions.auth.*` — судячи з
`.claude/logs/tasks/403_...` в git status на старті) — `git diff` підтверджує мій hunk
ізольований, нічого спільного.

## Верифікація

- `npx tsc --noEmit` — чисто (після виправлення `title`-пропа на `Gift`).
- `npm run build` — успішно, exit code 0 (`/pos` route в таблиці маршрутів,
  8.4 kB). Повторювані `ENVIRONMENT_FALLBACK` стек-трейси в логу — існували вже до цієї
  задачі, зачіпають десятки непов'язаних сторінок під час "Generating static pages",
  build все одно завершується успішно (52/52 сторінок, exit 0) — не з моєї зміни.
- **Живий end-to-end прогін на dev-стеку** (не тільки tsc/build): підняв
  `backend-dev`/`frontend-dev` (`.claude/launch.json`), Postgres вже був up. Backend
  автопризначив порт 65004 (3000 зайнятий чужим, не-CRM процесом
  `WaterTracker\website` — не займав його, натомість перезапустив backend з
  `Cors__Origins=http://localhost:65004` env-override, тимчасово, не редагуючи жодного
  backend-файлу). Залогінився як seeded `manager@demo.local`, відкрив зміну (API), створив
  реальний продаж (баркод `4820000130013`, "Вода Моршинська 1,5л", 2×14.00=28.00₴, без
  `CustomerId`/`LoyaltyMembershipId`). Перевірив і `POST /api/pos/sales`, і
  `GET /api/pos/sales` напряму (це і є доказ знахідки з п.2 вище). У браузері (вже
  автентифікована сесія іншого seeded-юзера, той самий tenant): `/pos` показав відкриту
  зміну й рядок продажу коректно, БЕЗ іконки Gift (правильно — немає даних лояльності);
  відкрив `SaleDetailDrawer` кліком по рядку — "Загальна інформація" відрендерилась як і
  раніше (оплата/сума/решта/фіскалізація), секція "Лояльність" коректно НЕ з'явилась
  (`saleHasLoyaltyActivity` = false), "Товари (1)" відрендерились коректно. Жодної
  консольної помилки. Прибрав за собою: закрив тестову зміну (`POST
  /api/pos/shifts/close`), зупинив обидва прев'ю-сервери, вбив осиротілий backend-процес
  (`Stop-Process`, підтвердив PID/CommandLine перед цим).

## Свідомі рішення (без user sign-off, за судженням)

- Не додав `customerId` в TS-тип і не будував "ім'я клієнта" UI — поля нема в реальному
  wire-контракті жодного з двох ендпоінтів (не "поки null", а буквально відсутнє). Замість
  вигаданого поля — код-коментар з точним планом backend-розширення.
- Секція/індикатор лояльності гейтовані на `loyaltyAccrued/Redeemed/Balance != null`
  (proxy "має дані лояльності"), а не на `customerId` (як буквально написано в брифі) —
  єдиний реально доступний сигнал сьогодні. Задокументовано в коді й тут.
- Іконка `Gift` (lucide-react) — не було усталеної конвенції в кодовій базі для
  "лояльність", вибрав найбільш очевидну; легко змінити.
- Обидві нові секції UI сьогодні НІКОЛИ не рендеряться на живому бекенді (перевірено), і
  це навмисно — краще коректний, "мертвий до backend-фіксу" код, ніж вигадані дані.

## Не в скоупі / потрібні окремі задачі

- **backend:** додати `CustomerId`+`CustomerName` (або хоча б `CustomerId`, фронт сам
  зробить lookup) в `SaleDto`, замапити в `CreateSaleAsync` І `GetSalesForShiftAsync`.
- **backend:** замапити `LoyaltyAccrued/Redeemed/Balance` в `GetSalesForShiftAsync` (join
  `LoyaltyLedgerEntry` за `PosTransactionId`) — без цього щойно додана веб-секція
  лишається невидимою назавжди, навіть для продажів із реальним нарахуванням/списанням.
- **frontend (майбутнє, окремо):** якщо колись з'явиться `/customers/[id]`-роут — секцію
  "Лояльність" можна доповнити посиланням на клієнта; сьогодні нема куди лінкувати.
- Мобільний бік (`pos/loyalty.tsx`, `(consumer)` route group тощо) — паралельна задача
  mobile-developer, не чіпав.

## Файли

- `frontend/features/pos/types.ts`
- `frontend/features/pos/components/SaleDetailDrawer.tsx`
- `frontend/features/pos/components/SalesTable.tsx`
- `frontend/messages/en.json`
- `frontend/messages/uk.json`

Не закомічено (main-сесія/користувач комітить, за конвенцією проєкту).
