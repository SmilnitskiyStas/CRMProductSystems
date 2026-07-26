# TASK-405: Loyalty program Application+Api layer (Фаза 0)

**Agent:** backend-developer
**Date:** 2026-07-26
**Status:** done

## Контекст

Task #2 з послідовності агентів плану `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md`
(Фаза 0). Слідує за TASK-404 (database-engineer) — схема/RLS/`TenantConnectionInterceptor`
вже готові, я їх НЕ чіпав (перевірено `git status`: жодних змін у `AppDbContext.cs`,
4 Loyalty-entities, міграції, `TenantConnectionInterceptor.cs`).

## Зроблено

**Consumer auth** (`Features/ConsumerAuth/`) — `ConsumerAuthController` (`/api/consumer-auth/
register`,`/login`, `[AllowAnonymous]`, reuse `"auth-login"` rate-limit policy). Окремий
сервіс, не вбудований в `AuthService` (форма claims суттєво відрізняється). Lockout —
буквально та сама логіка TASK-329 (5 спроб/15 хв), застосована вручну до
`ConsumerAccount.FailedLoginAttempts/LockoutUntil` (плейн mutable-властивості, немає domain-
методів на цій entity). `PhoneNormalizer.cs` (`Application/Common/`) — нормалізація до
`+380XXXXXXXXX`, структурна (без перевірки коду оператора), використовується лише тут.
`IJwtService.GenerateConsumerAccessToken` (нова, паралельна до staff-методу): `sub`+
`consumer_account_id`=consumerAccountId, `role="consumer"`, БЕЗ `tenant_id`; той самий
audience, що й staff-токен (не окремий, як 2FA-challenge) — інакше не пройде `[Authorize]`
на consumer-ендпоінтах. Довгоживучий (30д, `Jwt:ConsumerAccessTokenDays`) — свідоме рішення:
`RefreshToken` прив'язаний до `User`, не `ConsumerAccount` (схема заморожена цим тавском), і
без окремого refresh-флоу консюмер логінився б щохвилини. **Помічено для
security-reviewer**: немає механізму відкликання цього токена, крім чекати спливання.

**Membership/QR/ledger** (`Features/Loyalty/`) — `ILoyaltyService`/`LoyaltyService` (10
залежностей: `ILoyaltyRepository`, `ICustomerRepository`, `ITenantRepository`,
`IUserRepository`, `IConsumerAccountRepository`, `IPasswordHasher`, `ITotpService`,
`IResolveCodeAttemptTracker`, `IActivityLogRepository`, `ILogger`). `ITotpService` отримав
новий метод `GenerateCode(secret)` (Otp.NET `Totp.ComputeTotp()`) — на відміну від 2FA, тут
СЕРВЕР рахує поточний код і віддає клієнту, секрет ніколи не покидає бекенд.
QR-payload `SGLOY1.{membershipId}.{6-digit-code}`.

- `JoinAsync`/`JoinAsStaffAsync` — ідемпотентні (existing membership → return as-is, не
  помилка); find-or-create Customer за телефоном (`CustomerRepository.FindByPhoneAsync`,
  новий метод); перевірка `tenant.HasModule("loyalty")` РУЧНА в сервісі (НЕ
  `[RequireModule]` — той атрибут читає claim `tenant_id`, якого consumer-сесія не має
  ЖОДНОГО разу; для staff-сесії `[RequireModule("loyalty")]` на контролері теж стоїть,
  подвійний захист). `JoinAsStaffAsync` шукає ІСНУЮЧЕ membership за (tenantId,
  consumerAccountId) ПЕРШИМ ділом і лише бекфілить `LinkedUserId` — інакше падає на
  `uq_loyalty_memberships_tenant_consumer` (спіймано живим тестом при першому прогоні).
- `GetCurrentCodeAsync`/`GetHistoryAsync` — 404 якщо немає membership у цьому tenantId;
  НЕ гейтовані модулем (перегляд історії/балансу лишається доступним, навіть якщо tenant
  тимчасово вимкнув програму — вимикається лише нарахування/списання).
- `ResolveCodeAsync` — парсинг payload → rate-limit check → membership lookup → TOTP verify
  → атомарний claim timestep → результат. Rate-limit/lockout: **LoyaltyMembership не має
  FailedLoginAttempts/LockoutUntil колонок** (схема заморожена) — новий
  `IResolveCodeAttemptTracker` (Application-інтерфейс) + `MemoryResolveCodeAttemptTracker`
  (Infrastructure, `IMemoryCache` — вже в shared framework через
  `FrameworkReference Microsoft.AspNetCore.App`, нового NuGet не треба). Той самий
  5/15хв патерн що TASK-329, але in-process — **помічено для security-reviewer**:
  single-instance-deployment tradeoff, не переживає рестарт/не шариться між інстансами;
  Redis (вже є для BullMQ) — природний наступний крок, якщо стане реальною проблемою.
  Anti-replay claim — `ILoyaltyRepository.TryClaimTimestepAsync`: єдиний WHERE-guarded
  `ExecuteSqlInterpolatedAsync` UPDATE (атомарний на рівні рядка Postgres), НЕ EF
  concurrency-token (LoyaltyMembership його не має). Живий тест на реальному Postgres
  (`LoyaltyRepositoryIntegrationTests.cs`, 4 тести) підтвердив: перший claim OK, replay/
  earlier timestep — false, later timestep — OK, чужий tenantId — false без мутації.
- `ManualAdjustAsync` — guard проти від'ємного балансу; ActivityLog.
- `GetMyMembershipAsync`/settings CRUD — прямі, дзеркалять `PrroSettingsService`. Дефолти
  (3%/50%/0/30с) повертаються, якщо рядка ще нема — loyalty працює "з коробки" одразу після
  активації модуля, без обов'язкового візиту в Settings.

**POS-інтеграція** (`PosService.CreateSaleAsync`) — вставлено МІЖ рядком `tx.TotalAmount =
txItemDtos.Sum(...)` і `tx.TaxAmount = ...` (не просто "після 344 і до коміту 358" з брифу —
саме до розрахунку ПДВ, щоб податок і нарахування рахувались від чистої, вже зменшеної суми).
Redemption: перевірка cap/balance/MinRedemptionBalance → зменшує `tx.TotalAmount` →
ledger-запис (Amount від'ємний). Accrual: рахується від (можливо вже зменшеного)
`tx.TotalAmount` → ledger-запис. `LoyaltyProgramSettings.IsEnabled=false` — тихо пропускає
нарахування/списання (сейл все одно проходить), не помилка. `Customer.TotalOrders/TotalSpent`
тепер справді оновлюються (були мертві поля) для будь-якого `CustomerId`, незалежно від
membership. Все — в ОДНОМУ `SaveChangesAsync` (той самий scoped `AppDbContext`, що й
`PosRepository`/`CustomerRepository`/`LoyaltyRepository` — підтверджено живим
`PosConcurrencySalesIntegrationTests` прогоном). Додав 3 опційні поля в `SaleDto`
(`LoyaltyAccrued/Redeemed/Balance`) — не було в брифі буквально, але без цього каса не
могла б показати касиру оновлений баланс одразу після продажу (баланс з resolve-code вже
застарілий на момент коміту сейлу); суто адитивно, дефолт null.

**Роль і модулі** — `AppRoles.Consumer="consumer"` (НЕ додано в `AppRoles.All` — те
перераховує ролі, що можна призначити `User`, а consumer ніколи не User-рядок).
`Tenant.UpdateModules` — додав `"loyalty"`, `"marketing_analytics"` у `valid[]`.
`frontend/lib/roles.ts` / `mobile/lib/roles.ts` — додав лише константу `Consumer`, НЕ додав
у жоден рольовий `Set`/масив (ті гейтують tenant-staff сторінки; consumer — інша сесія
взагалі). `tsc --noEmit` чистий на обох.

**Контролери:** `ConsumerAuthController`, `ConsumerLoyaltyController` (`/api/consumer/
loyalty/*`, `[Authorize]` + ручна перевірка claim `consumer_account_id` — не
`[RequireModule]`, з тієї ж причини, що вище), `LoyaltyController` (`/api/loyalty/*`,
`[RequireModule("loyalty")]` на класі + різні `[Authorize(Policy=...)]` на кожен екшн),
`LoyaltySettingsController` (`/api/settings/loyalty`, дзеркалить `PrroSettingsController`).

## Верифікація

- `dotnet build` — 0 err, 0 warn (чистий rebuild; попередній 1 warning з
  `MarketplaceServiceTests.cs` не з'явився цього разу).
- `dotnet test` — **1004/1004 green** (було 936 після TASK-404; +68 нових тестів:
  `PhoneNormalizerTests`, `ConsumerAuthServiceTests`, `LoyaltyServiceTests` (NSubstitute,
  дзеркалить стиль `AuthServiceTests`), 9 нових у `PosServiceTests` (accrual/redemption/
  cap/balance/blocked/customer-aggregate/no-op-без-полів), 1 у `TenantTests`,
  `LoyaltyRepositoryIntegrationTests` (4, живий Postgres — anti-replay SQL).
- Живий Postgres підтвердив: RLS-тести TASK-404 і далі зелені (не чіпав жодного RLS),
  concurrency-тест POS і далі зелений з новими залежностями, і нова anti-replay raw SQL
  коректна проти реального `loyalty_memberships` (FK на `consumer_accounts` спіймано й
  виправлено в тесті одразу — жива БД зловила те, що моки не могли).

## Свідомі рішення (без user sign-off, за судженням)

- Consumer JWT — довгоживучий (30д), без revoke-механізму. Флаговано для
  security-reviewer, не блокер v1.
- Rate-limit resolve-code — `IMemoryCache`, не БД-колонка (схема заморожена). Флаговано.
- `IsEnabled=false` в settings → тихо пропускає бонуси, сейл все одно проходить (не 400).
- Дефолтні settings повертаються без збереженого рядка — щоб не блокувати касу до
  обов'язкового візиту в Settings.

## Не в скоупі (наступні агенти)

- **security-reviewer:** consumer JWT lifetime/revocation, in-memory rate-limiter tradeoff,
  `consumer_accounts` без RLS (TASK-404), `TryClaimTimestepAsync` raw SQL parametrization
  (вже параметризовано через `ExecuteSqlInterpolatedAsync`, але варто зафіксувати рев'ю).
- **frontend-developer / mobile-developer (Task #4):** `SaleDetailDrawer`/`SalesTable`
  секція лояльності (backend вже віддає `LoyaltyAccrued/Redeemed/Balance` в `SaleDto`),
  `pos/loyalty.tsx` (QR-сканер, `barcodeTypes` вже включає `'qr'`), `(consumer)` route
  group, `auth/store.ts` `sessionKind`, QR-рендер залежність. **Контракт для мобільного
  "manual code entry"**: `POST /api/loyalty/resolve-code` очікує ПОВНИЙ рядок
  `SGLOY1.{id}.{code}` — і зі сканера, і з ручного вводу (екран консюмера має показувати
  цей рядок текстом поруч із QR, не лише графічно).
- **Features/MarketingAnalytics/** (Task #3) — не чіпав.
