# BUG-021 — provider_agent / provider_admin still blocked from Live Chat inbox

**Agent:** database-engineer · **Date:** 2026-07-08 · **Status:** done
**Follow-up to:** BUG-020

## Причина
Дві окремі перепони блокували саме `provider_agent`/`provider_admin`
(перевірено читанням коду):
1. `AdminChatController.cs` мав `[Authorize(Policy = AppPolicies.ProviderOnly)]`
   (тільки роль `provider`) замість `AppPolicies.ProviderTeamMember`
   (`provider` + `provider_admin` + `provider_agent`), яку вже використовують
   `AdminServiceDeskController`, `ProviderRolesController`,
   `ProviderScheduleController`, `ProviderTeamController` — 403 ще до RLS.
2. `provider_bypass` на `chat_sessions` (доданий у BUG-020) перевіряв лише
   `app.role = 'provider'` — той самий клас бага, що вже чинили для
   `support_tickets`/`ticket_comments` в
   `20260623010000_ExpandProviderBypassRlsForTeam`.

## Виправлення
- `backend/ShelfGuard.Api/Controllers/AdminChatController.cs:16` —
  `ProviderOnly` → `AppPolicies.ProviderTeamMember`. Інші контролери
  (`AdminController`, `MarketplaceAdminController`, `ProviderController`)
  свідомо не чіпали — лишаються `ProviderOnly`.
- Нова міграція
  `20260708070158_ExpandChatSessionsProviderBypassRlsForTeam`:
  `DROP POLICY IF EXISTS provider_bypass` + `CREATE POLICY provider_bypass ON
  chat_sessions USING (current_setting('app.role', true) IN ('provider',
  'provider_admin', 'provider_agent'))`. Down відкочує до вузької
  `= 'provider'` версії (стиль як у `20260623010000`).

## Перевірка
- `dotnet build` — success, 0 errors (1 pre-existing warning в
  `MarketplaceServiceTests.cs`, не пов'язаний).
- Міграція застосована до локальної dev БД (docker
  `crmproductsystems-postgres-1`, db `crm`, порт 5435): підтверджено
  `pg_policy` — `provider_bypass` на `chat_sessions` тепер
  `= ANY (ARRAY['provider', 'provider_admin', 'provider_agent'])`.
- Продакшн не чіпали (без деплою/SSH).
