# TASK-392: User↔Location store-scoped assignment schema (Stage 1, Feature 2)

**Agent:** database-engineer
**Date:** 2026-07-19
**Status:** done (schema only, per brief — API/service logic + `app.user_id` session var are follow-up backend-developer tasks)

## Зроблено

1. **`User.StoreId` EF mapping fix** (`backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs`,
   `User` block) — додано `.HasColumnName("LocationId")`, FK `HasOne<Location>().WithMany()
   .HasForeignKey(u => u.StoreId).OnDelete(DeleteBehavior.SetNull).IsRequired(false)` — той самий
   nullable/optional shape, що й `ProviderRoleId`/`SupplierRoleId`/`TenantRoleId`/`LegalEntityId`
   на цій самій сутності. C#-властивість лишилась `StoreId` (навмисно, узгоджено з архітектором —
   той самий патерн, що й 19 інших pre-v4 сутностей: `ProductStock`, `WriteOff`, `PosShift`, ін.).
   Індекс `IX_users_LocationId` — авто-згенерований EF-конвенцією з FK, окремого `.HasIndex()` не
   писав (той самий підхід, що й `TenantRoleId`).

2. **`UserLocation` entity** (`backend/ShelfGuard.Domain/Entities/UserLocation.cs`, новий файл) —
   `Id`, `TenantId`, `UserId` (FK→users, Cascade), `LocationId` (FK→locations, Cascade),
   `AssignedByUserId` (FK→users, SetNull, nullable — той самий патерн що
   `TenantRole.CreatedByUserId`), `CreatedAt`. Private setters + static `Create()`, без
   navigation-властивостей (той самий "лише FK" стиль, що `TenantRole.CreatedByUserId`/
   `User.ProviderRoleId` — жодна з них теж не має nav property). Без soft-delete — leaf-таблиця,
   видалення = hard DELETE.

3. **EF config** (`AppDbContext.cs`) — `DbSet<UserLocation>`, `ToTable("user_locations")`,
   unique-індекс `(TenantId, UserId, LocationId)` → `uq_user_locations_tenant_user_location`,
   вторинний індекс `(TenantId, LocationId)` → `idx_user_locations_tenant_location`, 3 FK.

4. **RLS на `user_locations`** — канонічний tenant_isolation + provider_bypass + worker_bypass
   (скопійовано 1:1 зі стилю `20260713152826_AddTenantRoles.cs`). **НЕ** RESTRICTIVE
   store_scope-політика — та зʼявиться в Stage 3 на product_stock/daily_sales/pos_shifts/etc,
   не тут.

5. **Дві EF Core міграції**:
   - `20260719120419_FixUserLocationColumnMapping` — `RenameColumn StoreId→LocationId` (не
     drop+recreate — зберігає існуючі дані), `CreateIndex IX_users_LocationId`,
     `AddForeignKey FK_users_locations_LocationId` (SetNull).
   - `20260719120844_AddUserLocations` — `CreateTable user_locations` + 3 FK + 5 індексів
     (EF-авто + мій unique + мій secondary) + RLS SQL (додав вручну після генерації — EF не
     генерує RLS автоматично).
   Обидва Designer.cs мають `[DbContext(typeof(AppDbContext))]` + `[Migration("...")]` —
   перевірено `grep`.

## ⚠️ Конкурентний агент (TASK-391) у тій самій робочій директорії, без git-worktree ізоляції

Під час роботи виявив, що інший агент **одночасно** редагував ті самі файли (`AppDbContext.cs`,
`AppDbContextModelSnapshot.cs`, `Migrations/`) для НЕ повʼязаної задачі TASK-391
(`TenantRole.AllowedTabs`). Симптоми й що зробив:

1. Перша `migrations add FixUserLocationColumnMapping` згенерувала міграцію, змішану з їхнім
   `AddColumn AllowedTabs` (обидві зміни одночасно лежали в некомічених файлах — класична
   model-diff race: обидва процеси диффять "поточний код" проти того самого старого snapshot).
2. Виправляв **лише свої власні рядки** в `AppDbContext.cs` (тимчасовий коментар/`.Ignore()`,
   ніколи не редагував `TenantRole.cs`/`TenantRoleService.cs`/`TenantRoleDtos.cs` — файли іншої
   задачі) — генерував свою міграцію заново з їхньою властивістю тимчасово `e.Ignore()`-нутою,
   одразу відновлював.
3. Один раз trapped на **stale build**: `dotnet ef ... --no-build` підхопив старий
   `ShelfGuard.Api`-бінарник (project-reference копія не оновилась) і знову дав контаміновану
   міграцію попри коректний вихідний код. Фікс: forced `dotnet build ... --no-incremental` на
   САМЕ `ShelfGuard.Api` (стартап-проєкт) безпосередньо перед кожним critical `migrations add`/
   `database update` викликом — після цього стабільно чисто.
4. Побачив, що інший агент сам, незалежно, встиг перегенерувати і закомітити (в робочу директорію,
   не в git) свою чисту `AddTenantRoleAllowedTabs` міграцію ПІСЛЯ моєї — залишив її без змін,
   не намагався генерувати за нього.
5. Ретельно перевірив (після завершення обох задач): жоден раніше закомічений файл не лишився
   пошкодженим — `20260716200731_AddUserPreferredLocale.{cs,Designer.cs}` 0 diff проти HEAD,
   `git diff --stat` по всьому репо показує рівно 5 очікуваних файлів.

**Наслідок для порядку міграцій:** `FixUserLocationColumnMapping` (моя, 120419) →
`AddTenantRoleAllowedTabs` (їхня, 120554) → `AddUserLocations` (моя, 120844). Валідний,
самоузгоджений ланцюжок — застосував і перевірив усі три разом.

## Знайдено попутно: RLS блокує FK-валідацію при `database update` через некоректну роль

`dotnet ef database update` через `shelfguard_app_dev` (non-superuser, `FORCE ROW LEVEL SECURITY`,
конфігурація з `appsettings.Development.json`) падав на
`ALTER TABLE users ADD CONSTRAINT FK_users_locations_LocationId ...` з
`23503: violates foreign key constraint`, хоча орфанів не було (перевірив `LEFT JOIN` — усі 4
non-null `StoreId` валідні). Корінь: без `app.tenant_id`/`app.role` сесійних змінних (міграції
виконуються поза request-контекстом) RLS на `locations` ховає ВСІ рядки навіть від власника
таблиці (`FORCE RLS`) — FK-валідатор Postgres не бачить жодного `locations.Id`, тому вважає всі
існуючі `users.LocationId` сирітськими. Підтвердив: `SELECT count(*) FROM locations` під
`shelfguard_app_dev` без сесійних змінних = 0, хоча рядки є. Це не мій баг і не регресія — це
властивість FORCE RLS + non-superuser app-роль, що вилазить на БУДЬ-ЯКІЙ майбутній міграції, яка
додає FK на вже заповнену колонку, що посилається на RLS-таблицю.

**Фікс (для застосування міграцій локально):** `dotnet ef database update` через `crm`
(bootstrap superuser, `rolbypassrls=true`) — саме та роль, для якої й призначений коментар в
`appsettings.Development.json` ("'crm' stays only for admin/psql").

## ⚠️ CRITICAL FIX (за запитом координатора): те саме падає в проді при старті контейнера

Початкове припущення "прод не мій скоуп" було **невірним** — координатор перевірив
`backend/ShelfGuard.Api/Program.cs:159-206`: прод застосовує міграції через
`db.Database.MigrateAsync()` на ТОМУ Ж самому з'єднанні, яким апка працює завжди (не superuser —
явна fail-fast перевірка KI-028 йде ОДРАЗУ ПІСЛЯ `MigrateAsync()`, тобто якщо сама міграція впаде,
до цієї перевірки код навіть не дійде). Це точно той самий сценарій, що я зловив локально
(`crm` vs `shelfguard_app_dev`) — `FixUserLocationColumnMapping`'s `AddForeignKey` валідував би
існуючі рядки `users.LocationId` синхронно, і на будь-якому оточенні, де non-superuser роль (як і
в проді) не бачить `locations` без `app.tenant_id`/`app.role` (яких немає під час міграції),
constraint-add впав би з 23503 → `MigrateAsync()` кидає виняток → контейнер не піднімається →
Bad Gateway до наступного вдалого деплою. `deploy.sh` спочатку зупиняє старі контейнери, тож це
був би реальний downtime-інцидент, не просто локальна незручність.

**Виправлено:** `FixUserLocationColumnMapping.Up()` тепер додає FK через raw SQL з `NOT VALID`
замість `migrationBuilder.AddForeignKey(...)`:
```sql
ALTER TABLE users ADD CONSTRAINT "FK_users_locations_LocationId"
  FOREIGN KEY ("LocationId") REFERENCES locations ("Id")
  ON DELETE SET NULL NOT VALID;
```
`NOT VALID` — стандартний Postgres zero-downtime патерн: обмеження діє одразу для ВСІХ нових/
змінюваних рядків (закриває дірку "будь-який GUID приймається мовчки"), але НЕ валідує вже існуючі
рядки в момент `ADD CONSTRAINT` — валідації існуючих рядків просто не відбувається, тому
RLS-під-час-валідації проблема не виникає взагалі. `Down()` не чіпав — `DropForeignKey` за назвою
працює однаково незалежно від validated-стану. TODO залишив у коментарі міграції: окремий
`ALTER TABLE users VALIDATE CONSTRAINT "FK_users_locations_LocationId";` можна прогнати пізніше
вручну через superuser-з'єднання (не блокує деплой).

**Повторна перевірка (обов'язкова умова від координатора):** відкотив усі 3 міграції локально
(через `crm`, разовий reset), відредагував файл, forced full rebuild, і застосував **ЗАНОВО через
`shelfguard_app_dev` (non-superuser, той самий сценарій що й прод)** — усі 3 міграції пройшли без
помилок. Підтвердив:
- `SELECT conname, convalidated FROM pg_constraint WHERE conname = 'FK_users_locations_LocationId'`
  → `convalidated = f` (правильно, NOT VALID).
- `\d users` показує `... ON DELETE SET NULL NOT VALID` явно в тексті constraint.
- Live-тест enforcement: `UPDATE users SET "LocationId" = '00000000-...'` → відхилено (23503,
  правильно — FK ДІЄ для нових записів); `UPDATE ... SET "LocationId" = (валідний Id)` → успішно.
  Тестовий рядок (`merch1@demo.local`) відновив до оригінального значення після перевірки.

## Верифікація

- `dotnet build` (весь solution) — 0 помилок, 1 pre-existing warning (не мій код,
  `MarketplaceServiceTests.cs:534`). Повторив після NOT VALID фіксу — так само чисто.
- `dotnet test` — **858/858 passed** (двічі: до фіксу, і знову після NOT VALID фіксу +
  reapply через non-superuser). Один проміжний прогін дав 1 fail
  (`PosConcurrencySalesIntegrationTests` — очікувано, БД ще не мала застосованої міграції в той
  момент, код уже очікував колонку `LocationId`).
- Усі 3 міграції застосовані на локальний dev Postgres (`crmproductsystems-postgres-1`, порт
  5435) **через `shelfguard_app_dev` (non-superuser, прод-еквівалентна роль)** — саме так, як
  запросив координатор, і саме так, як реально відбувається в `Program.cs` при старті контейнера.
  Перевірено: `users.LocationId` + FK (NOT VALID, `convalidated=f`) + індекс; `user_locations` —
  усі колонки, 3 FK, 5 індексів (2 auto + unique + secondary + PK); RLS enable+force+3 policies
  підтверджено через `pg_policies`/`pg_class.relforcerowsecurity`; enforcement-тест на новий/
  змінюваний рядок (bad GUID відхилено, valid GUID прийнято). `__EFMigrationsHistory` — усі 3
  міграції по порядку.
- Прод/staging — не чіпав, деплой не робив. Локальна перевірка тепер точно відтворює прод-шлях
  (`MigrateAsync()` під non-superuser з'єднанням), тому цей блокер закритий саме там, де він мав
  значення.

## Не в скоупі (свідомо, для наступних задач)

- Stage 3 RESTRICTIVE store_scope RLS-політики на product_stock/daily_sales/pos_shifts/etc.
- Сесійна змінна `app.user_id` в `TenantConnectionInterceptor` (backend-developer, TASK-392b).
- `UserService`/API логіка запису в `user_locations` (наступна задача).
