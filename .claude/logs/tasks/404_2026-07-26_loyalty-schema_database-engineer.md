# TASK-404: Loyalty program schema — ConsumerAccount / LoyaltyMembership / LoyaltyLedgerEntry / LoyaltyProgramSettings

**Agent:** database-engineer
**Date:** 2026-07-26
**Status:** done

## Контекст

Фаза 0 плану `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` (RFM-аналітика + програма
лояльності) — schema-only слайс, перший з послідовності агентів (Task #1). Дає механізм
чек↔клієнт прив'язки (`PosTransaction.CustomerId`), якого сьогодні жоден код не заповнює.

## Зроблено

**4 нові entities** (`backend/ShelfGuard.Domain/Entities/`) у стилі `Customer.cs`/`PosTransaction.cs`
(sealed class, public `init`/`set`, nav-властивості — не DDD private-setter/factory стиль
`User.cs`/`UserLocation.cs`):
- `ConsumerAccount.cs` — глобальна, без TenantId, без RLS.
- `LoyaltyMembership.cs` — tenant-scoped, + `LoyaltyMembershipStatus` constants (active/blocked),
  той самий паттерн що `DiscountStatus`/`DiscountReason`.
- `LoyaltyLedgerEntry.cs` — append-only (всі властивості `init`), + `LoyaltyEntryType` constants.
- `LoyaltyProgramSettings.cs` — tenant-scoped, one row per tenant.

**AppDbContext.cs** — 4 DbSets + fluent config (`ToTable`, indekси, FK). Delete-поведінка:
`ConsumerAccountId`/`TenantId`→Restrict (явно з брифу); `CustomerId`/`LinkedUserId`/
`PosTransactionId`/`CreatedByUserId`→SetNull (явно з брифу); `LoyaltyLedgerEntry.MembershipId`→
Restrict (не було в брифі — обрав за прецедентом `StockMovement.ProductId→catalog_products
RESTRICT`, "log-рядки проти довгоживучого master-запису", захищає append-only ledger від
випадкового каскадного видалення; на практиці нерелевантно, бо `LoyaltyMembership` ніколи
не видаляється, лише Status active/blocked). Customer.cs/PosTransaction.cs/User.cs/Tenant.cs
НЕ чіпав — усі зв'язки `HasOne(...).WithMany()` без inverse nav, той самий паттерн що
`SupplierTask`/`WorkSchedule`/`UserLocation`.

**Migration `AddLoyaltyProgram`** (`20260726132332_AddLoyaltyProgram.cs`), згенерована
`dotnet ef migrations add`, RLS дописана вручну:
- `consumer_accounts` — жодного RLS SQL (навмисно, детальний коментар у класі міграції +
  XML-doc з прямою цитатою з плану про ризик для security-reviewer).
- `loyalty_memberships`/`loyalty_ledger_entries`/`loyalty_program_settings` — канонічна тріада
  (tenant_isolation NULLIF-guard + provider_bypass + worker_bypass).
- `loyalty_memberships` + `loyalty_ledger_entries` (через EXISTS на membership) — нова
  `consumer_self_access` policy точно за SQL з брифу.
- **Відхилення від буквального шаблону в брифі:** `provider_bypass` на всіх 3 tenant-таблицях
  одразу написана як `IN ('provider', 'provider_admin')`, не лише `'provider'`. Причина:
  `20260714150000_ExpandProviderBypassToProviderAdmin` вже retroактивно виправила рівно цей
  розрив на 71 існуючій таблиці (provider_admin має ідентичні права з provider —
  `ProviderPermissions.SystemRoleDefaults`); писати нову таблицю тільки з `'provider'` відтворило
  б той самий баг з першого дня. Той самий judgment call, що явно зробив автор
  `AddLocationStoreScopeRlsPolicies` для свого bypass-списку. Задокументовано в XML-doc класу
  міграції.

**TenantConnectionInterceptor.cs** — додано читання claim `consumer_account_id`, `SET
app.consumer_account_id` (той самий always-set/null-uuid-fallback паттерн що `app.tenant_id`/
`app.user_id`), `"consumer"` у `ValidRoles`. Unauthenticated RESET-гілка тепер скидає і цю змінну.

**Верифікація RLS `customers`** (п.4 брифу) — підтверджено, НЕ переробляв: живий `psql` запит
до `pg_policies`/`pg_class` показав повну канонічну тріаду (tenant_isolation fail-closed,
provider_bypass з provider+provider_admin, worker_bypass, RLS enabled+forced). Збігається з
твердженням плану.

**Тести:**
- `TenantConnectionInterceptorTests.cs` — +1 `InlineData("consumer")` у наявний Theory, +4 нові
  Fact для `app.consumer_account_id` (дзеркалять наявний блок `app.user_id`).
- Новий `LoyaltyRlsIntegrationTests.cs` (живий Postgres, той самий skip/cleanup паттерн що
  `RlsCrossTenantIntegrationTests.cs`) — 4 тести: consumer читає власні membership в 2 tenant-ах
  але не чужі; ledger EXISTS-scoping через membership; staff-сесія й далі бачить тільки свій
  tenant (consumer_self_access нічого не розширює); full-reset → 0 рядків (fail-closed).

## Верифікація

- `dotnet build` (весь solution) — 0 err, 0 warn (1 pre-existing warning в
  `MarketplaceServiceTests.cs` з'являється лише на deep rebuild, непов'язаний).
- `dotnet test` — 936/936 green.
- Міграція застосована до dev DB через `crm` superuser (обійшов FK-validation-під-RLS gotcha з
  `database-schema.md`). Down()/Up() round-trip — чисто (усі 4 таблиці зникли й повернулись).
- Живий `psql`: RLS-флаги + повний текст усіх policy на 4 нових таблицях підтверджені байт-в-байт
  за дизайном.
- Знайшов і виправив власний баг гігієни тестів під час верифікації: перша версія
  `LoyaltyMemberships_FullyResetSession_...` використовувала спільний seed-helper (створює 2
  tenant + 2 consumer) але чистила лише половину → лишало по 1 сирітському рядку в `tenants`/
  `consumer_accounts` за кожен прогін. Виправлено (чистить усе, що насіяв helper); стряс
  рядки з dev DB видалені вручну; повторний прогін — 0 залишкових рядків.

## Не в скоупі (для наступних агентів)

- **backend-developer (Task #2):** `AppRoles.Consumer`, `PosService.CreateSaleAsync` інтеграція,
  `ConsumerAuthController`/`LoyaltyController`, `ITotpService` перевикористання для QR-кодів,
  `Tenant.UpdateModules` (`"loyalty"` ключ).
- **`Item.ItemType` "packaging"** — рядкове поле (`Item.cs:31`, default `"product"`), міграція
  не потрібна. backend-developer (Фаза 1) додає `"packaging"` у список допустимих значень де
  воно валідується.
- **security-reviewer:** `consumer_accounts` без RLS — навмисне архітектурне рішення з плану
  (прецедент `tenants`), обов'язковий окремий review перед релізом; нова identity-based RLS
  policy (`consumer_self_access`) — перша така в репозиторії, теж потребує review.

## Git

Не комітив — working tree лишає нові/змінені файли для рев'ю.
