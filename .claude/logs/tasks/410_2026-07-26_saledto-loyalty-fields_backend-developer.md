# TASK-410: SaleDto customer fields + loyalty mapping on GetSalesForShiftAsync

**Agent:** backend-developer
**Date:** 2026-07-26
**Status:** done (code + tests green) — but the feature is **not visible live yet**, blocked by a pre-existing, unrelated DB permissions gap found during verification (see below); flagged as a separate task, not fixed here.

## Контекст

TASK-408 (frontend) знайшов дві прогалини в бекенді: `SaleDto` не мав `CustomerId`/`CustomerName`
взагалі, і `GetSalesForShiftAsync` (те, що реально викликає веб `/pos`) ніколи не заповнював
`loyaltyAccrued/Redeemed/Balance` (лише `CreateSaleAsync` це робив). Це та задача, що закриває
обидві прогалини.

## Зроблено

- **`Features/Pos/Dtos/PosDtos.cs`** — `SaleDto` отримав `CustomerId: Guid?`/`CustomerName: string?`
  (додані в кінці, з `= null`, як і наявні `Loyalty*` поля — жоден виклик конструктора не зламався).
- **`PosService.CreateSaleAsync`** — додав `CustomerId: customer?.Id, CustomerName: customer?.Name`
  до вже наявного `new SaleDto(...)` (сам `customer` вже був у методі з TASK-405).
- **`PosService.GetSalesForShiftAsync`** — повністю перероблений мапінг:
  - `CustomerId`/`CustomerName` — з `PosTransaction.CustomerId`/`.Customer.Name` (потрібен був
    `.Include(t => t.Customer)` в `PosRepository.GetTransactionsByShiftAsync`, додав поруч з
    наявним `.Include(t => t.Items)`).
  - `LoyaltyAccrued/Redeemed/Balance` — **PosTransaction не має власного `LoyaltyMembershipId`**,
    тож єдине джерело істини — `LoyaltyLedgerEntry.PosTransactionId`. Додав
    `ILoyaltyRepository.GetLedgerEntriesForTransactionsAsync(tenantId, transactionIds)` (batch,
    одним запитом на весь шифт, а не по одному на рядок) + реалізацію в `LoyaltyRepository`
    (`Where + Contains + ToListAsync` — той самий патерн, що вже є в
    `ProviderStatsRepository`/`StockRepository`/інших — не новий ризик для EF/Npgsql).
  - Групую записи ledger по `PosTransactionId`, сортую по `CreatedAt`. `Accrued`
    = сума `Amount` записів типу `Accrual` (>0 → інакше null); `Redeemed` = `-`сума записів типу
    `Redemption` (знак розвернутий на позитивний — так само як `CreateSaleAsync` і як вже рендерить
    `SaleDetailDrawer.tsx`: `-{value} ₴`). **`Balance`** = `BalanceAfter` **останнього** запису
    (по `CreatedAt`) для цієї транзакції — це баланс одразу ПІСЛЯ цього конкретного продажу
    (історичний знімок), а не поточний живий баланс membership (той міг змінитися пізнішими
    продажами) — свідомий вибір семантики, задокументований у коді.
- **Тести:** додав `GetLedgerEntriesForTransactionsAsync` у 2 fake (`FakeLoyaltyRepo` в
  `PosServiceTests.cs`, `RetryFakeLoyaltyRepo` в `FiscalizationRetryTests.cs` — новий метод
  інтерфейсу, обидва мануальні fake мали його реалізувати; `LoyaltyServiceTests.cs` — NSubstitute,
  нічого не робити). Додав 3 нових тести в `PosServiceTests.cs`: клієнт + нуль loyalty-активності
  → всі loyalty-поля null; лише accrual → `Accrued` set, `Balance` = той запис; redemption+accrual
  → обидва set, `Balance` = запис accrual (останній), не redemption.

## Верифікація

- `dotnet build` — 0 err (1 попередній непов'язаний warning, Marketplace тести).
- `dotnet test` — **1086/1086** (було 1083 → +3 нових), включно з live-Postgres
  `PosConcurrencySalesIntegrationTests`/`LoyaltyRepositoryIntegrationTests` (dev Postgres
  `crmproductsystems-postgres-1:5435` був up, реально виконались, не skip).
- **Живий прогін**: підняв `backend-dev` (`dotnet run`), залогінився `manager@demo.local`,
  відкрив зміну, створив клієнта (`POST /api/customers`), продав товар з `customerId` —
  `POST /api/pos/sales` відповів коректним `customerId`/`customerName` в camelCase JSON.
  **`GET /api/pos/sales?shiftId=...` (сама ціль цієї задачі) впав з 500** — не через мій код.

## КРИТИЧНА знахідка (не виправлено тут, окрема задача заведена)

`GET /api/pos/sales` кинув `Npgsql.PostgresException 42501: permission denied for table
loyalty_ledger_entries`. Перевірив напряму (`psql \dp`, `pg_tables.tableowner`): всі 4 таблиці
з TASK-404 (`consumer_accounts`, `loyalty_memberships`, `loyalty_ledger_entries`,
`loyalty_program_settings`) належать суперюзеру `crm` (той, яким накатували міграцію), і
GRANT нічого не дає робочій ролі застосунку (`shelfguard_app_dev` — підтвердив
`rolsuper=f, rolbypassrls=f`, тобто це не superuser-bypass кейс, а банально відсутній GRANT).
Для порівняння `customers`/`pos_transactions` коректно належать `shelfguard_app_dev`. Тобто
**весь loyalty-ланцюжок задач (404-408, 410) сьогодні не працює через реальний застосунок**
(тільки через `crm`-з'єднання тестів/міграцій) — будь-який запит, що чіпає ці 4 таблиці, впаде
з 42501, у dev (підтверджено живо) і ймовірно в staging/prod (та ж міграційна процедура).

Не чіпав жодних прав/власників БД сам — це поза скоупом `Features/Pos`, і за прецедентом цього ж
репо (TASK-371/KI-027: аналогічна знахідка на staging, агента заблокував permission classifier
при спробі живого фіксу, задокументовано а не обійдено) DB ownership/grants — окрема, review-
потребуюча задача, не бекенд-патч на льоту. Завів фонову задачу (`task_693b439c`,
"Grant app DB role access to the 4 loyalty tables") для `database-engineer` з повним
root-cause і кроками; нічого в БД не міняв (ні dev, ні тим паче staging/prod).

Прибрав за собою: закрив тестову зміну, зупинив dev-сервер (підтвердив PID/ProcessName перед
`Stop-Process`). Тестовий клієнт "TASK-410 Live Check Customer" залишився в dev БД (нешкідливо,
легко впізнати за назвою).

## Не в скоупі / не займав

`Features/Loyalty/`, `Features/ConsumerAuth/`, `Features/MarketingAnalytics/` — не чіпав, як і
було вказано. Frontend (`frontend/features/pos/`) — не чіпав; camelCase назви полів
(`customerId`/`customerName`) звірив з `frontend/features/pos/types.ts` (там цих двох полів ще
нема — за задумом TASK-408, фронт додасть їх окремо, коли забажає; JSON вже сумісний за
конвенцією нейминга).

## Файли

- `backend/ShelfGuard.Application/Features/Pos/Dtos/PosDtos.cs`
- `backend/ShelfGuard.Application/Features/Pos/PosService.cs`
- `backend/ShelfGuard.Domain/Interfaces/ILoyaltyRepository.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/LoyaltyRepository.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/PosRepository.cs`
- `backend/ShelfGuard.Tests/Pos/PosServiceTests.cs`
- `backend/ShelfGuard.Tests/Pos/FiscalizationRetryTests.cs`

Не закомічено (main-сесія/користувач комітить, за конвенцією проєкту).
