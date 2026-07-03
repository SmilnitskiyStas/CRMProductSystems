# BUG-009 — 8 hand-written міграцій невидимі для EF (missing attributes)

**Agent:** database-engineer (обірвався на session limit; верифікацію завершила main session)
**Date:** 2026-07-03 · **Status:** done

## Root cause
Hand-written міграції створювались без `[Migration("...")]` + `[DbContext(typeof(AppDbContext))]`
атрибутів (у scaffolded вони живуть у Designer.cs). EF збирає модель міграцій через ці
атрибути → `MigrateAsync` їх не бачив. Прод отримав зміни історично (застосовані при
деплоях, коли існували в інших формах / вручну), але свіжа БД розгорталась неповною —
login падав 500 (виявлено в QA v4.1 при піднятті локального стека).

## Affected migrations (8)
20260621200000_AddProviderRoles · 20260622120000_AddNotificationIsRead ·
20260623000000_AddServiceDeskProviderBypassRls · 20260623010000_ExpandProviderBypassRlsForTeam ·
20260627120000_AddItemPerishabilityClass · 20260628000000_ForceRlsOnAllTenantTables ·
20260629000000_FixUsersRlsNullIfEmptyString · 20260629010000_FixAllRlsPoliciesNullIfEmptyString

## Fix
- Додано обидва атрибути кожній міграції (id = імʼя файлу).
- Тіла переписані на ідемпотентний raw SQL (CREATE TABLE IF NOT EXISTS,
  DROP POLICY IF EXISTS + CREATE, ADD COLUMN IF NOT EXISTS) — критично, бо на проді
  вони відсутні у `__EFMigrationsHistory` і виконаються повторно при наступному деплої.
- `AppDbContextModelSnapshot` синхронізовано.

## Verification
- `dotnet ef migrations list` — усі 9 (8 + V41SupplierSelfService) видимі.
- Ідемпотентність (симуляція прода): на локальній БД з уже існуючими обʼєктами
  видалено 8 рядків історії → `dotnet ef database update` повторно застосував усі 8
  без жодної помилки.
- `dotnet build` 0 errors; `dotnet test` 500/500 green.

## Deploy note
Наступний прод-деплой виконає ці 8 міграцій повторно (ідемпотентно) і допише їх
у `__EFMigrationsHistory` — це очікувано і безпечно.
