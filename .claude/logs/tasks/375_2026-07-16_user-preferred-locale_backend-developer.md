# TASK-375: User.PreferredLocale (backend + міграція) — i18n Block 1

**Agent:** backend-developer / database-engineer
**Date:** 2026-07-16
**Status:** done

## Зроблено

- `ShelfGuard.Domain/Entities/User.cs` — `string? PreferredLocale` (nullable, private setter) + `SetPreferredLocale(string?)`. Null = браузерний fallback.
- `AppDbContext.cs` — `HasMaxLength(5).IsRequired(false)` (лишає запас під `uk-UA`-стиль теги).
- Міграція `20260716200731_AddUserPreferredLocale`: `AddColumn<string>("PreferredLocale", "users", "character varying(5)", nullable: true)` — суто адитивна, без data-змін, RLS не чіпала. Designer-файл має `[DbContext(typeof(AppDbContext))]` + `[Migration(...)]`.
- `AuthUserDto` (Auth/Dtos/AuthDtos.cs) і `UserDto` (Users/Dtos/UserDtos.cs) — додано `PreferredLocale` (обидва mapping-и — `AuthService.ToDto`, `UserService.ToDto` — оновлено).
- `UpdateMyProfileRequest` — додано опційне `PreferredLocale` (4-й параметр, default null). Валідація в `UserService.UpdateMyProfileAsync`: тільки "uk"/"en" (`SupportedLocales` HashSet, той самий патерн що `ValidPages`); невалідне значення → 400 до будь-якого DB-звернення. `null` = значення не змінюється (не скидається) — навмисно, щоб старий клієнт без цього поля не стирав вибір користувача.
- Ендпоінт не новий — існуючий `PUT /api/auth/me` (`AuthController.UpdateMe`) вже приймає `UpdateMyProfileRequest`; тіло розширено, роут/контракт відповіді (`UserDto`) не змінились.
- Тести: новий `ShelfGuard.Tests/Users/UserServicePreferredLocaleTests.cs` (той самий konstruktor/NSubstitute патерн, що і `UserServicePasswordTests`) — приймає uk/en, відхиляє невалідний код, null не чіпає збережене значення.

## Верифікація

- `dotnet build` — 0 errors, 1 pre-existing warning (не звʼязаний, MarketplaceServiceTests.cs).
- `dotnet test` — спочатку 1 fail (`PosConcurrencySalesIntegrationTests`, реальний Postgres на `localhost:5435` — локальний dev-контейнер `crmproductsystems-postgres-1`, не прод) через відсутню колонку. Застосував нову міграцію лише до цього **локального** контейнера (`dotnet ef database update` з explicit connection string на :5435) — прод (Hetzner) і навіть staging-контейнер (:5436) не чіпав. Після цього: **858/858 passed** (854 існуючих + 4 нових).
- Прод-міграцію НЕ застосовував, deploy НЕ запускав.

## API shape (для frontend-агента)

`PUT /api/auth/me` — тіло розширено:
```jsonc
// request
{ "fullName": "string", "phone": "string | null", "preferredLocale": "uk" | "en" | null }
// preferredLocale omitted/null → залишає збережене значення без змін
```
Response (`UserDto`) і `GET /api/auth/me`, login/refresh (`AuthUserDto`) тепер містять:
```jsonc
"preferredLocale": "uk" | "en" | null
```
Невалідне значення (не "uk"/"en"/null) → `400 { "error": "Unsupported locale '<value>'. Supported: uk, en." }`.

## Файли
- `backend/ShelfGuard.Domain/Entities/User.cs`
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs`
- `backend/ShelfGuard.Infrastructure/Migrations/20260716200731_AddUserPreferredLocale.cs` (+ `.Designer.cs`)
- `backend/ShelfGuard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`
- `backend/ShelfGuard.Application/Features/Auth/Dtos/AuthDtos.cs`
- `backend/ShelfGuard.Application/Features/Auth/AuthService.cs`
- `backend/ShelfGuard.Application/Features/Users/Dtos/UserDtos.cs`
- `backend/ShelfGuard.Application/Features/Users/UserService.cs`
- `backend/ShelfGuard.Tests/Users/UserServicePreferredLocaleTests.cs` (новий)

## Не в скоупі
Frontend wiring (NextIntlClientProvider, language switcher в Settings/Profile) — наступний крок Block 1, окремий frontend-developer TASK.
