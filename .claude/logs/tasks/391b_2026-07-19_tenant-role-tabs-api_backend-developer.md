# TASK-391b: TenantRole tabs API — GET /api/tenant-roles/tabs, JWT "tabs" claim, AuthUserDto.Tabs

**Agent:** backend-developer
**Date:** 2026-07-19
**Status:** done

## Зроблено

1. **`GET /api/tenant-roles/tabs`** (`TenantRolesController.cs`, `GetTabs()`) — новий action поруч
   з `GetCapabilities()`, той самий клас-рівня `[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]`
   gate (нічого додатково не чіпав). Повертає `IReadOnlyList<TenantRoleTabDto>` через новий
   `ITenantRoleService.GetTabCatalog()` / `TenantRoleService.GetTabCatalog()`, джерело —
   `TenantRoleTabs.Catalog` (10 записів, вже існував з TASK-391).
2. **`TenantRoleTabDto(string Key, string LabelUa)`** — новий DTO в `TenantRoleDtos.cs`, дзеркало
   `TenantRoleCapabilityDto`.
3. **JWT `tabs` claim** — `IJwtService.GenerateAccessToken`/`JwtService.GenerateAccessToken`
   отримали новий trailing-optional `List<string>? tabs = null`. Comma-joined `"tabs"` claim,
   додається тільки якщо непорожній — точно той самий патерн, що й `"capabilities"`.
4. **`AuthService.BuildEffectiveTabsAsync(User, ct)`** — нове, буквальне дзеркало
   `BuildEffectiveCapabilitiesAsync` (той самий null-check на `TenantRoleId`/`TenantId`, той самий
   archived-role check), читає `TenantRole.AllowedTabs` замість `Capabilities`. Свідомо окремий
   `_tenantRoles.GetByIdAsync` виклик (не переюзаний з Capabilities-версії, тобто одна TenantRole-
   строка читається двічі при кожному login/refresh/getCurrentUser) — це дзеркалить навмисний
   розподіл Capabilities/AllowedTabs як двох незалежних осей, задокументований у
   `TenantRoleTabs.cs`; другий PK-лукап по крихітній per-tenant таблиці — прийнятний, свідомий
   trade-off, не недогляд.
5. Усі 3 місця, що будують `AuthUserDto` (`IssueTokensAsync` — login + 2FA verify, `RefreshAsync`,
   `GetCurrentUserAsync`) тепер також викликають `BuildEffectiveTabsAsync` і прокидають
   `effectiveTabs` в `_jwt.GenerateAccessToken(...)` (тільки перші два — `GetCurrentUserAsync` не
   мінтить токен) і в `ToDto(...)`.
6. **`AuthUserDto.Tabs`** — `IReadOnlyList<string>? Tabs = null`, trailing-optional, останній
   параметр рекорда (після `PreferredLocale`).
7. **`UserDto`** — НЕ чіпав: перевірив, там немає `Capabilities` взагалі, тож умова брифу
   "якщо є Capabilities — додай симетрично Tabs" не спрацьовує, дзеркалити нічого.
8. Жодних нових authorization requirements/handlers/policies — Tier 1 enforcement свідомо поза
   скоупом, підтверджено grep'ом — `[Authorize]`/`RequireModule`/`RoleOrCapability*` не чіпав.

## Тести

- Оновив 3 існуючі test-файли (`AuthServiceCapabilitiesTests.cs`, `AuthServiceTests.cs`,
  `TwoFactorAuthTests.cs`) — їхні NSubstitute-стаби `_jwt.GenerateAccessToken(...)` мали рівно
  8 arg-matchers; після 9-го параметра (`tabs`) вони б перестали матчитись (AuthService тепер
  завжди передає конкретний, не-`null` список — NSubstitute матчить пропущений trailing-optional
  arg на буквальний `null`, не на "будь-яке значення"), тому додав 9-й `Arg.Any<List<string>?>()`
  у кожен setup (включно з `Arg.Do`-captor варіантом).
- Новий файл `AuthServiceTabsTests.cs` — 7 тестів, точне дзеркало
  `AuthServiceCapabilitiesTests.cs` (login active/no-role/archived/dangling-id, refresh,
  getCurrentUser) + 1 додатковий тест, що Tabs і Capabilities резолвляться незалежно один від
  одного з того самого `TenantRole`.
- `TenantRoleServiceTests.cs` — +1 тест `GetTabCatalog_ReturnsEveryTabInAll_WithLabels` (мірор
  `GetCapabilityCatalog_...`).

## Верифікація

- `dotnet build` — 0 помилок, 1 pre-existing warning (не мій код,
  `MarketplaceServiceTests.cs:534`, той самий що фіксував TASK-391).
- `dotnet test` — **866/866 passed** (858 було в TASK-391 + 8 нових).
- ⚠️ Одразу після коміту повторний `dotnet build` у спільній робочій директорії впав
  (`CS7036`, `UserService` конструктор раптом вимагає `ILocationService`/`IUserLocationRepository`
  у 5 test-файлах) — це НЕ моя зміна: паралельний TASK-392b-агент саме зараз має незакомічений,
  ще недороблений constructor-рефактор `UserService.cs` у тій самій робочій директорії (некомітнуті
  зміни в `UsersController.cs`, `ILocationService.cs`, `LocationService.cs`, `UserDtos.cs`,
  `IUserService.cs`, `UserService.cs`, `DependencyInjection.cs`, `TenantConnectionInterceptor.cs`
  + нові `IUserLocationRepository.cs`/`UserLocationRepository.cs`). Перевірив свій коміт
  (`a8d6cd62`) ізольовано через тимчасовий `git worktree` (без чужих uncommitted файлів) —
  build 0 помилок, test 866/866 passed, потім worktree видалив. Мій код чистий; хтось (той
  агент або оркестратор) має перевірити стан `UserService.cs` окремо, коли TASK-392b завершиться.

## Точний shape для фронтенду (окрема задача, паралельно — не робив)

- `GET /api/tenant-roles/tabs` → `200 [{ "key": string, "labelUa": string }, ...]` (camelCase,
  default System.Text.Json), той самий `AtLeastEnterpriseAdmin` gate що й `/capabilities`.
- JWT access-token claim `"tabs"` — comma-joined string, ВІДСУТНЯ якщо порожньо (як і
  `"capabilities"`).
- `AuthUserDto` (`GET /api/auth/me`, `POST /api/auth/login`/`refresh` → `response.user`) — нове
  поле `"tabs": string[] | null`.
- Повний бриф для frontend-агента: `.claude/logs/handoffs/391b-to-frontend_backend-developer.md`.

## Знайдено під час роботи (не моє, FYI — без дій)

`backend/ShelfGuard.Application/Features/Locations/ILocationService.cs` мав некомітнуту зміну
(новий метод `BelongsToTenantAsync`, коментар "TASK-392b") від паралельного агента в тій самій
робочій директорії — не чіпав, не мій скоуп. Підтвердив легітимність:
`.claude/logs/handoffs/392-to-392b_backend-developer.md` вже існує, тобто це відомий, окремий,
активний тред роботи, не сирота. Мій `build`/`test` пройшли зелено попри цю паралельну незакомічену
зміну в робочій директорії — конфлікту файлів з моїм скоупом не було (перевірив `git status` на
цільові файли перед стартом — чисто).

## Git

Локальний commit зроблено. **Push НЕ виконано** — за прямою вказівкою в брифі (продукт-овнер
попросив паузу на деплой сьогодні).
