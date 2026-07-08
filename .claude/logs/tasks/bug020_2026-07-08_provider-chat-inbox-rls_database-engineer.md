# BUG-020 — Provider support chat inbox always empty (RLS blocked)

**Agent:** database-engineer · **Date:** 2026-07-08 · **Status:** done

## Причина
`chat_sessions` мала лише tenant-scoped RLS-політику. `ChatService`
(`GetAllSessionsForProviderAsync` та інші provider-методи) намагався обійти її
через `SET LOCAL app.tenant_id = ''` — але Npgsql/EF Core виконує кожен
`ExecuteSqlRawAsync` як окрему автокомітну команду поза явною транзакцією, тож
`SET LOCAL` відкочувався ще до виконання наступного `SELECT`. Реальне значення
`app.tenant_id` для ролі `provider` (немає `tenant_id` в JWT) — нульовий GUID,
який не проходить жодну гілку політики → 0 рядків завжди. Той самий баг вже
чинили для `support_tickets`/`ticket_comments` в
`20260623000000_AddServiceDeskProviderBypassRls`.

## Виправлення
- Нова міграція `20260708055021_AddChatSessionsProviderBypassRls`:
  `DROP POLICY IF EXISTS provider_bypass` + `CREATE POLICY provider_bypass ON
  chat_sessions USING (current_setting('app.role', true) = 'provider')` —
  той самий патерн, що й для service desk. `chat_messages` RLS не має
  (ніколи не вмикався в `20260621161638_AddChatFeature`), тож політика не
  потрібна.
- `backend/ShelfGuard.Infrastructure/Services/ChatService.cs` — прибрано 4
  недієві виклики `ExecuteSqlRawAsync("SET LOCAL app.tenant_id = ''", ct)` у
  `GetAllSessionsForProviderAsync`, `GetMessagesForProviderAsync`,
  `ProviderSendMessageAsync`, `ProviderCloseSessionAsync`. Інша логіка не
  змінювалась.

## Перевірка
- `dotnet build` — success, 0 errors (1 pre-existing warning в тестах,
  не пов'язаний).
- Міграція застосована до локальної dev БД (docker `crmproductsystems-postgres-1`,
  db `crm`): підтверджено в `__EFMigrationsHistory` і
  `pg_policies` — `provider_bypass` на `chat_sessions` активна.
- Продакшн не чіпали (без деплою/SSH).
