# TASK-391: TenantRole.AllowedTabs schema (Stage 1, Feature 1 — per-role sidebar tab visibility)

**Agent:** database-engineer
**Date:** 2026-07-19
**Status:** done (schema + DTO + validation only, per brief — API/JWT wiring is a follow-up backend-developer task)

## Зроблено

1. **`backend/ShelfGuard.Domain/Constants/TenantRoleTabs.cs`** (новий файл) — структурно
   ідентичний `TenantRoleCapabilities.cs`: `TenantRoleTabDefinition(string Key, string LabelUa)`
   record, `All: HashSet<string>` (10 ключів), плюс `Catalog: IReadOnlyList<TenantRoleTabDefinition>`
   (флет-список з UA-лейблами — аналог `Groups`, без групування по спеціальності, бо вкладки й
   так top-level). Каталог і лейбли звірені проти реального коду, не вигадані:
   - 10 ключів підтверджено проти `frontend/components/layout/Sidebar.tsx` (`buildNavGroups` →
     `NavGroup.key`): `operations, sales, procurement, marketplace, auto_service, production,
     analytics, workforce, support` — точний збіг, включно з snake_case `auto_service`.
     `dashboard` — НЕ NavGroup.key (це окремий standalone `NavItem`, href `/dashboard`), але
     реальна, окрема nav-дестинація, тому включена per бриф.
   - UA-лейбли скопійовані буквально з `frontend/messages/uk.json`
     (`Dashboard.sidebar.dashboard` / `Dashboard.sidebar.groups.*.label`), не придумані заново.
   - Підтверджено виключення (реально існують у Sidebar.tsx, свідомо не в каталозі):
     `admin` (NavGroup, provider-only), `supplier_cabinet` (NavGroup з `buildSupplierNavGroup`,
     supplier_admin-only), `settings` (standalone NavItem, завжди видимий).

2. **`TenantRole` entity** (`backend/ShelfGuard.Domain/Entities/TenantRole.cs`) — додано
   `AllowedTabs: List<string>` (default `[]`). `Create`/`Update` отримали новий параметр
   `IEnumerable<string>? allowedTabs = null` (trailing, optional) — свідомо НЕ зробив required,
   щоб не чіпати 9 існуючих позиційних викликів `TenantRole.Create(...)` у
   `TenantRoleServiceTests.cs` (адитивний підхід, той самий принцип що й у міграції).

3. **`TenantRoleService.cs`** — `Validate` тепер приймає і `allowedTabs`, відкидає ключі поза
   `TenantRoleTabs.All` (дзеркально до Capabilities-перевірки). `CreateAsync`/`UpdateAsync`
   прокидають `request.AllowedTabs ?? []` через `Validate` → `TenantRole.Create/Update`. `Map`
   повертає `r.AllowedTabs` у DTO.

4. **DTOs** (`TenantRoleDtos.cs`) — `TenantRoleDto` отримав `AllowedTabs` (позиційно після
   `Capabilities`); `CreateTenantRoleRequest`/`UpdateTenantRoleRequest` отримали
   `List<string>? AllowedTabs = null` (той самий trailing-optional підхід — існуючий 2-arg виклик
   у тестах лишився компільованим без змін).

5. **EF Core міграція** `AddTenantRoleAllowedTabs` — строго адитивна: `ALTER TABLE tenant_roles
   ADD "AllowedTabs" text[] NOT NULL DEFAULT ('{}')`. RLS не чіпав (існуючі
   tenant_isolation/provider_bypass/worker_bypass покривають весь рядок — перевірено
   `\d tenant_roles` на dev-базі, збігається 1:1). Designer.cs має `[DbContext]`/`[Migration]`.

### ⚠️ Відхилення від букви брифу: `text[]`, не `jsonb`

Бриф у двох місцях писав "AllowedTabs (jsonb, default [])" і одночасно "той самий патерн
зберігання, що й Capabilities". Це суперечність — реальний `Capabilities` у
`AppDbContext.cs:1969` це **`text[]`** (нативний Postgres-масив, без `HasConversion`/
`EnableDynamicJson`/`ValueComparer`), з явним коментарем у коді "text[], not jsonb — matches
ProviderRole.Permissions/SupplierRole.Permissions exactly". Перевірив: обидва ProviderRole.
Permissions і SupplierRole.Permissions (рядки 1597, 1628) — той самий `text[]`, без жодного
value comparer. Це перевірений, робочий патерн (3 сутності), а не гіпотеза.

Рішення: `AllowedTabs` зроблено `text[]` — буквально "той самий патерн, що й Capabilities",
ігноруючи помилкове припущення "jsonb" з контекстного опису брифу (project-architect, схоже,
не звірив це проти реального `AppDbContext.cs`). Це judgment call з об'єктивною відповіддю
(архітектурна консистентність — CLAUDE.md), не продуктове рішення, тому не зупиняв роботу на
уточнення, але фіксую тут явно.

## ⚠️ Знайдено: конкурентний агент (TASK-392) в тій самій робочій директорії

Під час `dotnet ef migrations add` виявив, що інший процес **одночасно** редагував ті самі
файли (`AppDbContext.cs`, `AppDbContextModelSnapshot.cs`, папку `Migrations/`) без git-worktree
ізоляції — коментарі в коді прямо називають "TASK-392 Stage 1" (User.StoreId→LocationId column
mapping). Симптоми, у хронологічному порядку:

1. Перша `migrations add` згенерувала мою міграцію ЗМІШАНУ з їхнім `RenameColumn StoreId→
   LocationId` (обидві зміни одночасно лежали в незакомічених файлах).
2. `dotnet ef migrations remove --force` один раз **помилково видалив реальну, вже закомічену**
   `20260716200731_AddUserPreferredLocale` (гонка: інший процес вже сам прибрав свої/мої
   контамінованні міграції до того, як мій `remove` встиг побудуватись і вирахувати "останню").
   **Відновив негайно** через `git checkout HEAD -- ...AddUserPreferredLocale.*`.
3. Обидва процеси одночасно намагались ізолювати свою частину (я тимчасово нейтралізував їхній
   `User.StoreId` hunk через відсутність-у-моделі; вони — мій `AllowedTabs` через
   `e.Ignore(r => r.AllowedTabs)` з коментарем "TASK-392-TEMP: ... unrelated concurrent TASK-391
   change, isolating my diff only"). Був момент дублювання — два файли з ІДЕНТИЧНОЮ назвою класу
   `AddTenantRoleAllowedTabs` (різні timestamp, байт-в-байт однаковий вміст) — видалив дублікат,
   лишив свій.
4. Фінально спрацювало: коли їхня `FixUserLocationColumnMapping` вже осіла в спільному
   `AppDbContextModelSnapshot.cs`, моя наступна `migrations add` дала чистий, ізольований diff
   (лише `AllowedTabs`). Перевірив build + вміст файлу — чисто.
5. Один `dotnet build` під час усього цього впав з `CSC : error CS2012 ... file may be locked by
   VBCSCompiler` (спільні `obj/`-директорії, конкурентний build) — транзієнтне, ретрай пройшов.

**Мій фінальний стан:** рівно одна пара `20260719120554_AddTenantRoleAllowedTabs.{cs,Designer.cs}`,
вміст — тільки `AllowedTabs`, нічого зайвого. Верифіковано build + grep на LocationId/StoreId
(відсутні). Файли `FixUserLocationColumnMapping.*` / `AddUserLocations.*` / `UserLocation.cs` у
робочій директорії — **не мої, не чіпав** (TASK-392, окремий агент).

**Дію користувачу/оркестратору:** якщо TASK-392 ще активна — варто перевірити, що фінальний
`AppDbContext.cs`/snapshot/migrations в порядку з їхнього боку теж (я відновив лише свою
частину й одну втрачену чужу committed-міграцію; далі не втручався). Рекомендація на майбутнє:
паралельні агенти в цьому репо мають отримувати окремий `git worktree` (сама CLAUDE.md вже
формулює це правило для Codex-паралелізму — тут воно, схоже, не спрацювало).

## Верифікація

- `dotnet build` — 0 помилок (1 pre-existing warning, не мій код, `MarketplaceServiceTests.cs:534`).
- `dotnet test` — **858/858 passed**. Один прогін дав 1 fail
  (`PosConcurrencySalesIntegrationTests` — real-Postgres concurrency race, нічого спільного з
  TenantRole) під час пікового навантаження від конкурентного агента на той самий dev-контейнер;
  повторний прогін в ізоляції — passed. Не регресія, довів окремим ре-раном.
- `dotnet test --filter TenantRole` — 31/31 passed.
- Міграція застосована на локальний dev Postgres (`crmproductsystems-postgres-1`, порт 5435,
  `crm`/`shelfguard_app_dev`) напряму через згенерований `dotnet ef migrations script` SQL
  (не через `database update` — попередня в ланцюжку `FixUserLocationColumnMapping` падає на
  цій dev-базі через сирітські `users.StoreId` FK-посилання, це їхній, не мій, баг) —
  `\d tenant_roles` підтвердив колонку `text[] NOT NULL DEFAULT '{}'::text[]`, потім відкотив
  (DROP COLUMN + видалення рядка з `__EFMigrationsHistory`), щоб не лишати спільну dev-базу в
  проміжному стані.
- Прод/staging — не чіпав, деплой не робив.

## Не в скоупі (свідомо, для наступного backend-developer кроку)

- `GET /api/tenant-roles/tabs` — не додавав, `TenantRolesController.cs` не чіпав.
- JWT/AuthService "tabs" claim — окрема задача.
- Frontend — окрема задача.
