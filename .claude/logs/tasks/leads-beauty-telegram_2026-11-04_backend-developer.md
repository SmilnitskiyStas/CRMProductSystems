# Beauty leads -> LandingLead + Telegram push (backend-developer, 2026-11-04)

Branch: feat/beauty-leads-telegram. Not committed, migration NOT applied.

## Що зроблено
- `LandingLeadService.AllowedSources` += `beauty`, `beauty-join`.
- `LeadCriticalityEvaluator` (Application/Features/Leads): правила network / urgent / repeat(7д, телефон до цифр) в одному місці.
- `LandingLead.IsCritical` + `CriticalReason` (varchar(50)); міграція `AddLandingLeadCriticality` (AddColumn, Down = DropColumn, існуючі рядки -> false/null).
- `ILeadNotifier` + `TelegramLeadNotifier` (Infrastructure/Integrations): sendMessage, plain text, HttpClient 5 с, `RemoveAllLoggers()` (токен в URL), логи без токена/PII.
- Сервіс: після SaveChanges fire-and-forget (`Task.Run`), помилки глушаться з логом типу винятку; пошук повторів best-effort.
- `GetPhonesSinceAsync` у репозиторії. DTO: `isCritical`, `criticalReason`.
- Frontend: тип, бейдж «КРИТИЧНО/CRITICAL» (текст) у LeadsPanel і LeadDetailDrawer, ключ `badgeCritical` uk/en. Джерело показується як є (словника підписів немає).
- Конфіг: `Leads__Telegram__{Enabled,BotToken,ChatId}`; compose (prod/staging) і .env*.example з порожніми значеннями.

## Rate limit / honeypot
`public-leads`: 5 req/хв на IP (fixed window) + honeypot `website` (тихий 204, нічого не зберігається, Telegram не шле). CORS не змінювався.

## Build/test
dotnet build OK; `dotnet test --filter Lead`: 86 passed. Frontend `tsc --noEmit` OK. Vitest для provider-тестів немає.
Інтеграційний тест з БД (LandingLeadRepositoryIntegrationTests) самопропускається без БД.
