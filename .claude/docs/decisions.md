# Architecture Decisions (ADR Log)

**Owner:** project-architect
**Updated:** 2026-07-12

## ADR-018: Notification categories expansion + filter drawer — Postgres outbox instead of C# BullMQ producer
Date: 2026-07-12
Status: accepted

Context: `notifications` page only surfaces `weekly_report` in practice (expiry/IoT alerts exist
but `iot.temp_alert`/`iot.offline` have no frontend label — display bug). User wants 4 new
categories (надходження, поповнення/AI order, повідомлення постачальника, підписання документів)
with full triggers, plus a collapsible filter drawer (search/employee/category/date/store).
Today's delivery pipeline: `worker/src/jobs/notification.job.ts` (BullMQ "notifications" queue)
resolves role-based recipients + `notification_settings`, delivers via `deliver()`, and is the
only writer of real `NotificationQueue` history rows (`logNotifications`, one row per
user×channel, `Status` = sent/skipped/failed). `expiry-check.job.ts`/`mqtt-listener.ts` are
BullMQ producers, both in Node. `ai-order.job.ts` bypasses this pipeline entirely — it calls
`sendTelegramMessage` directly, no settings check, no history row. Backend (ASP.NET Core) has
**no** existing Redis/BullMQ producer (`grep` for `StackExchange.Redis`/`bullmq` under
`/backend` — zero hits) — the three new backend-originated triggers (receipt received, supplier
chat message, agreement signed) have no way to reach the worker's delivery logic today.

Decision:
1. **Backend-originated events use a Postgres outbox, not a new C# BullMQ producer.** Adding a
   BullMQ-compatible job producer in .NET (matching BullMQ's Lua-script job format) is new
   cross-language infra for 3 call sites. Instead, the triggering C# service inserts one
   broadcast-intent row directly into `NotificationQueue` (`UserId = null`, `Channel = "system"`,
   `Status = "pending"`) via `INotificationRepository` — reuses the existing table, no new
   dependency. A new worker cron `notification-dispatch.job.ts` (poll every 1 min, same shape as
   `fiscalization-retry.job.ts`) selects `Status = 'pending' AND Channel = 'system'` rows,
   resolves recipients by role (same matrix pattern as `EXPIRY_EVENT_ROLES`) +
   `notification_settings`, delivers, writes real per-user×channel rows via the existing
   `logNotifications`, then marks the intent row `Status = 'dispatched'` (terminal, excluded from
   `GetHistoryAsync` so it never appears as a phantom "system" notification in the feed).
2. **`ai-order.job.ts` is rewired to the same in-process pattern as `handleIotAlert`** (query
   users by role → check `notification_settings` → `deliver()` → `logNotifications()`), dropping
   its direct `sendTelegramMessage` loop — it already runs in the Node worker with DB access, so
   no outbox hop is needed there, only the missing settings/history integration.
3. **`NotificationQueue` gains `StoreId Guid?` and `Title string?`.** `StoreId` backs the "by
   store" filter (repeats the `EventType.namespace.action` DB-only-hardcoded-set pattern already
   used for events/channels — no new enum table). `Title` is a short human-readable line
   (e.g. "Надійшла поставка №1234 — Хрещатик") populated by whichever service enqueues the row,
   so keyword search runs `ILIKE`/trigram against `Title` instead of parsing the `Payload` JSONB
   on every query — cheaper and matches the existing "Payload is opaque, UI parses it lazily"
   convention in `NotificationDetailDrawer.tsx`. Add `pg_trgm` GIN index on `Title` for the
   keyword filter, plus btree indexes on `(TenantId, CreatedAt)`, `(TenantId, EventType)`,
   `(TenantId, StoreId)`, `(TenantId, UserId)` for the other filters.
4. **Filter drawer is a hand-rolled overlay, not a new shadcn `Sheet`.** `components/ui/sheet.tsx`
   does not exist in this repo and `NotificationDetailDrawer.tsx` already implements a fixed-panel
   + backdrop drawer by hand — the new `NotificationFilterDrawer` follows the same pattern for
   visual/behavioral consistency rather than introducing a new shadcn primitive for one page.
5. **Filter state lives in component state + React Query key, not the URL.** No page in this repo
   currently syncs filters to `useSearchParams` (checked — zero matches under `frontend/features`
   outside auth). Introducing URL-synced filters here would be a new, unprecedented pattern for a
   single page; skip it. React Query key includes the filter object so results stay cached per
   filter combination.

Consequences: `notification.job.ts` and the new `notification-dispatch.job.ts` share the
role-matrix + settings-check + `logNotifications` pattern — worth extracting to a shared helper
in a follow-up if a 4th producer appears. `Channel = "system"` is an internal sentinel, not added
to `ValidChannels` in `NotificationService.cs` (backend inserts the outbox row directly via the
repository, bypassing the public validate path, same way the worker's `logNotifications` already
bypasses `NotificationService` entirely). `GetHistoryAsync` must filter out `Channel = 'system'`
rows so undispatched intents never leak into the UI feed.

## ADR-017: Provider nav split (Клієнти/Постачальники) + per-item категорії з JSONB attributes
Date: 2026-07-03
Status: accepted

Context: v4.1 (ADR-016) додав supplier-as-tenant. Два подальші UX/дані запити:
(A) провайдер-панель показує всіх тенантів одним списком (`ProviderService.GetTenantsAsync`,
`frontend/features/provider/`, сторінка `/provider` з табами `tenants`/`logs`) — незручно шукати
серед клієнтів і постачальників разом; (B) `SupplierItem` (marketplace listing постачальника,
не Item catalog) не має категорії — постачальник, який працює в кількох галузях (продукти,
автозапчастини, медикаменти, будматеріали), не може задати категорійно-специфічні поля
(OEM-номер, дозування/рецептурний статус, партія/термін придатності, клас сертифікації) для
кожного товару окремо.

Decision:
1. **Feature A — один список, client-side split, без нового роуту.** Сторінка `/provider`
   лишається одна; `activeTab` розширюється з `"tenants" | "logs"` на
   `"clients" | "suppliers" | "logs"`. Дані й API-виклик (`useTenants()`) без змін — фільтрація
   `business_type === "supplier"` виконується на клієнті над уже завантаженим списком (список
   тенантів невеликий, provider-only, пагінації немає). Причина проти нового бекенд-ендпоінта
   чи нового Next-роуту: нуль нових абстракцій, нуль ризику розсинхронізації лічильників
   (health-картки лишаються на весь список), TenantDetailPanel/CreateTenantWizard реюзаються
   без змін. Лічильник міняється лише в лейблі табу (`Клієнти (N)` / `Постачальники (M)`).
2. **Feature A — фільтрація по business_type, не по slug.** `platform-marketplace` (BUG-014,
   системний, IsActive=false) вже виключається на рівні `TenantRepository.GetAllAsync` — таб
   «Постачальники» бачить тільки реальні supplier-tenant-и, створені онбордингом (ADR-016 п.3/TASK-289).
3. **Feature B — категорія товару: `category` string (nullable) + `attributes JSONB (nullable)` на `SupplierItem`.**
   Обрано (b) єдину JSONB-колонку над (a) фіксованими nullable-колонками per category:
   набір категорій зростатиме (спека вже передбачає 4 старт-категорії, будматеріали/медикаменти
   реально розширяться підкатегоріями), і кожна нова категорія з підходом (a) означала б нову
   міграцію + розпухання entity. Прецедент у кодовій базі: `Item.Barcodes` — `List<string>` →
   `jsonb`, EF Core вже сконфігурований на dynamic JSON (Npgsql `EnableDynamicJson`, див.
   пам'ять проєкту); тут форма JSON я — довільний `Dictionary<string, object?>` (не List), тому
   на рівні EF — `.HasColumnType("jsonb")` + serialize/deserialize через `System.Text.Json`
   (той самий патерн, без потреби у нових Npgsql-налаштуваннях). Значення в `attributes`
   ніколи не беруть участі в SQL WHERE/JOIN (лише читання/показ у формі) — тому втрата
   SQL-запитів по конкретних полях прийнятна: категорійний пошук/фільтр (якщо колись знадобиться)
   іде через `category`, не через вміст attributes.
4. **Довідник категорій і полів живе в backend (C# const/enum + shared DTO), не тільки в
   фронтенд-мапі.** `SupplierItemCategories` (`ShelfGuard.Domain.Constants`) — фіксований
   список ключів категорій (`food`, `auto_parts`, `medical`, `construction`) + для кожної:
   список полів з `{key, label, type, required}` — **backend є джерелом істини**, бо валідація
   обов'язкових полів (медикамент без терміну придатності — invalid) має відбуватись на
   сервері, а не тільки в React-формі. Ендпоінт `GET /api/marketplace/item-categories`
   (публічний, кешується на фронті) віддає цей довідник як DTO — фронтенд не хардкодить форму,
   а рендерить її з відповіді. Це трохи важче за "фронтенд-only мапу", але усуває клас багів
   (фронт і бек розходяться в тому, що обов'язково) і дає єдине місце для розширення категорій.
5. **Зворотна сумісність.** `category` і `attributes` — нові nullable-колонки, DEFAULT NULL.
   Existing `SupplierItem` (provider-created legacy, TASK-275, і вже створені кабінетом TASK-286)
   лишаються з `category = null` — трактуються фронтом як «без категорії» (стара форма
   customName/price/minQty/unit, без динамічних полів). Валідація обов'язкових
   категорійних полів застосовується **тільки** коли `category` заданий (create/update DTO);
   `category = null` — валідний стан назавжди, не тимчасова міграційна яма.
6. **DTO shape:** `AdminAddSupplierItemDto`/`AdminUpdateSupplierItemDto`/`SupplierItemDto`
   (Cabinet-варіанти теж) отримують `string? Category` + `Dictionary<string, object?>? Attributes`.
   Немає окремих DTO per категорія — один generic shape, валідація обов'язкових полів
   виконується сервісним методом `SupplierItemCategories.Validate(category, attributes)`,
   що повертає список помилок (400 з переліком відсутніх полів).

Consequences:
+ Нова категорія (наприклад «Текстиль») — тільки зміна в `SupplierItemCategories` (C#) +
  фронтенд рендерить нову форму автоматично через API-довідник, без міграції
+ Один generic DTO/контролер-шлях для всіх категорій — мінімум нового коду в MarketplaceService/SupplierCabinetService
+ Existing товари (без категорії) не ламаються, стара форма продовжує працювати
- Не можна ефективно фільтрувати/сортувати marketplace за конкретним атрибутом (напр. "OEM-номер X")
  без повного сканування JSONB — прийнятно, бо публічний пошук сьогодні йде по `ItemName`/`Region`, не по атрибутах
- Валідація обов'язкових полів існує тільки в коді (C# + дзеркальна перевірка у формі), не в БД CHECK constraint —
  узгоджено з існуючим правилом "Validate at boundaries only"
- Provider-панель `/provider` тепер має 3 таби замість 2 — трохи вищий когнітивний навантаження, без нового роутингу

## ADR-016: Supplier self-service — supplier як окремий tenant (business_type = "supplier")
Date: 2026-07-02
Status: accepted

Context: Потрібна роль «Постачальник», який сам наповнює маркетплейс (профіль, товари) і бачить свої відгуки/рейтинг. Сьогодні marketplace-постачальників створює провайдер вручну (TASK-275, `TenantId = Guid.Empty`). Entities `Supplier/SupplierProfile/SupplierItem/SupplierMetrics/SupplierReview` вже існують з RLS `tenant_isolation` + `provider_bypass`; публічний листинг читається через provider-level DB context (`app.role = 'provider'`) з фільтром `is_public = true`.

Decision:
1. **Supplier = окремий tenant** з `business_type = "supplier"` і default-модулем `["marketplace_supplier"]`. НЕ нова роль усередині клієнтського tenant. Rationale: існуючий RLS `tenant_isolation` автоматично дає постачальнику видимість ТІЛЬКИ своїх рядків (`Supplier.TenantId` = його власний tenant), а публічний cross-tenant read маркетплейсу вже працює через provider-context + `is_public` — нових RLS-механізмів не треба.
2. **Нова app-роль `supplier_admin`** (tenant-scoped, у `AppRoles` + `roles.ts`). Юзер постачальника — звичайний User з `TenantId` = supplier-tenant, `Role = supplier_admin`. Auth/JWT без змін.
3. **Онбординг — провайдер запрошує** через існуючий Admin tenant onboarding (`business_type = "supplier"`). При створенні такого tenant автоматично створюється пара `Supplier` + `SupplierProfile` (`IsPublic = false` до заповнення). Self-registration — фаза 2.
4. **Зв'язок User ↔ Supplier — через TenantId.** Нова колонка `supplier_profiles.IsOwnerManaged bool` + partial unique index на `TenantId WHERE IsOwnerManaged` — детермінований lookup «мій профіль» (suppliers-таблиця double-duty: локальний довідник клієнтів і marketplace-записи, тому unique по TenantId неможливий).
5. **Supplier cabinet** — новий `SupplierCabinetController` (`/api/supplier-cabinet/*`), `[RequireModule("marketplace_supplier")]` + роль supplier_admin: GET/PUT профіль (+ publish toggle), CRUD товарів, read-only відгуки/метрики. Реюз логіки `MarketplaceService` (Admin*-методи параметризуються supplierId, resolved by tenant).
6. **Відгуки:** лишають тільки клієнтські tenant-и (existing `POST /api/marketplace/suppliers/{id}/reviews`; unique (supplier_id, tenant_id) вже є). Guard від накруток: reviewer tenant ≠ supplier.TenantId і `business_type != "supplier"`. Rating у `SupplierMetrics.Rating` перераховується синхронно в `CreateReviewAsync` (AVG по відгуках). Додається публічний `GET /suppliers/{id}/reviews`.
7. Існуючі provider-created suppliers (`TenantId = Guid.Empty`) лишаються як є; кабінет для них недоступний, поки провайдер не привʼяже supplier-tenant.
   > **Amendment (BUG-012, 2026-07-03):** `Guid.Empty` ніколи не працював — FK `suppliers→tenants` існував завжди, тож admin-create завжди падав 500 і рядків з `TenantId = Guid.Empty` у prod немає. Provider-created suppliers тепер привʼязуються до системного tenant «Platform Marketplace» (slug `platform-marketplace`, `business_type = supplier`, inactive, без users), який створюється ліниво в `MarketplaceRepository.GetOrCreatePlatformTenantIdAsync`. Кабінет його не бачить: профілі мають `IsOwnerManaged = false`, а лукап кабінету фільтрує `IsOwnerManaged = true`.
   > **Amendment (TASK-305, 2026-07-05, план `calm-singing-marble.md`):** компроміс BUG-012 визнано остаточно проблемним — два шляхи створення постачальника (Admin/Провайдер vs Маркетплейс/Постачальники) дублювали функціонал і залишали "напівживі" записи. Рішення: **лишити тільки шлях через `CreateTenantWizard`** (Admin/Провайдер/Постачальники), а legacy-шлях (`MarketplaceAdminController.CreateSupplier`) видаляє backend-developer окремою задачею. Дані-міграція `MigrateOrphanSuppliersToTenants` (database-engineer) переносить кожного постачальника з `platform-marketplace` на власний реальний активний tenant (`IsOwnerManaged = true`), після чого провайдер додає керівника через уже існуючий `AddTenantUserModal`. Після підтвердження, що жоден рядок більше не вказує на `platform-marketplace`, сам системний tenant і `GetOrCreatePlatformTenantIdAsync`/`PlatformTenantSlug`/`PlatformTenantName` видаляються.
   > Заодно додана ієрархія кастомних ролей команди постачальника (`supplier_roles`, tenant-scoped — на відміну від глобального `provider_roles`, кожен supplier tenant керує своїми ролями незалежно) і нова окрема сутність дошки завдань `supplier_tasks` (не привʼязана до існуючих заявок/замовлень). Обидві таблиці — стандартний RLS `tenant_isolation` + `provider_bypass`. Деталі схеми: `database-schema.md` розділ "v4.1 — Supplier tenant migration + roles/tasks".
   > **Amendment (TASK-306, 2026-07-05, backend-developer):** `MarketplaceAdminController.CreateSupplier`, `MarketplaceService.AdminCreateSupplierAsync`, `AdminCreateSupplierDto` — видалені. `GetOrCreatePlatformTenantIdAsync`/`PlatformTenantSlug`/`PlatformTenantName` (`MarketplaceRepository.cs`) НЕ видалені — `TenantRepository.GetAllAsync` досі фільтрує провайдерський список тенантів за цим slug'ом, а `MarketplaceRepositoryPlatformTenantTests` досі покриває цю поведінку; видалення відкладено до підтвердження (наступна ітерація/QA), що жоден рядок `suppliers`/`supplier_profiles` більше не вказує на `platform-marketplace` в жодному оточенні. Додано `ISupplierRolesService`/`SupplierRolesService` + `ISupplierTaskService`/`SupplierTaskService` (Application/Marketplace), CRUD endpoints на `SupplierCabinetController` (`/api/supplier-cabinet/roles`, `/api/supplier-cabinet/tasks`). `SupplierCabinetService.InviteStaffAsync` тепер приймає опційний `SupplierRoleId` — резолвиться в `Dictionary<string,bool>` через `IUserRepository` (той самий підхід, що й `ProviderTeamService`), відсутність ролі = повний доступ (без змін).

Consequences:
+ Нуль нових RLS-механізмів; ізоляція та публічний read — існуючими політиками
+ Максимальний реюз: entities, MarketplaceService, marketplace UI-компоненти
+ Онбординг = існуючий tenant onboarding + один hook
- supplier-tenant «носить» повний tenant-каркас (stores, modules), хоча використовує лише кабінет
- Подвійна семантика suppliers-таблиці лишається (локальний довідник vs marketplace) — розділення відкладено

## ADR-015: Module-based tenant activation pattern
Date: 2026-06-15
Status: accepted

Context: v4-spec вимагає, щоб кожен тенант міг активувати тільки потрібні йому модулі (Inventory, Procurement, POS, AutoService, Production, Marketplace). Поле `modules` (JSONB) вже існує на таблиці tenants (додано в TASK-074). Потрібно визначити, як модулі активуються і як API захищає модульні ендпоінти.

Decision:
1. Ключі в `tenant.modules` JSONB відповідають ідентифікаторам модулів: `"inventory"`, `"procurement"`, `"pos"`, `"auto_service"`, `"production"`, `"marketplace"`. Значення `true` = активовано.
2. Default-набір модулів при онбордингу визначається полем `business_type` (ADR-014): retail → `{inventory, procurement, pos}`, auto_service → `{auto_service, procurement}`, restaurant → `{inventory, pos, production}` і т.д.
3. На рівні ASP.NET Core додається `[RequireModule("module_key")]` attribute + відповідний `IAsyncActionFilter`, який читає `ITenantContext.Modules` і повертає `403 { error: "Module not activated" }` якщо модуль вимкнений.
4. API для управління модулями: `GET /api/admin/tenants/{id}/modules`, `PATCH /api/admin/tenants/{id}/modules` (ProviderOnly), `GET /api/settings/modules` (enterprise_admin — власний тенант). Активація/деактивація модуля не видаляє дані — тільки приховує доступ.
5. Frontend: sidebar-групи показуються/ховаються за комбінацією RBAC (роль) + модуль (активований). Хук `useModules()` читає з `/api/settings/modules`.

Consequences:
+ Один механізм для всіх модулів — легко додати новий
+ Дані ніколи не видаляються при деактивації (безпечно)
+ Provider panel повністю контролює набір модулів тенанта
- На кожен запит потрібен доступ до tenant.modules (мінімізується через ITenantContext кеш у request scope)
- UI sidebar ускладнюється (подвійна умова: роль + модуль)

## ADR-014: Platform transformation — Universal Location/Item model
Date: 2026-06-15
Status: accepted

Context: v4-spec вимагає перетворити платформу з retail-специфічної (Store, Product) на universal Business Operations Platform (Location, Item). Поточна схема: `stores`, `catalog_products`, `store_manager` role, `store_inventory`. Трансформація зачіпає 15+ таблиць, RLS policies, усі шари (DB, Domain, Application, API, Frontend, Mobile).

Decision:
1. **DB rename** (через EF Core migration): `catalog_products` → `items` (+ `item_type` column), `stores` → `locations` (+ `location_type` column), `store_zones` → `location_zones`. Роль `store_manager` → `location_manager` в AppRoles enum (UI label змінюється, значення в DB теж — UPDATE users SET role='location_manager').
2. **Поетапна міграція** (не big bang): спочатку DB + Backend, потім Frontend, потім Mobile. На кожному етапі працює production.
3. **API routes** змінюються: `/api/stores` → `/api/locations`, `/api/catalog` → `/api/items`. Для зворотньої сумісності мобільного APK — тимчасові 301-редіректи зі старих маршрутів (протягом 1 спринту, потім видаляються).
4. **Entity rename у коді**: `Store` → `Location`, `StoreZone` → `LocationZone`, `CatalogProduct` → `Item`. POC `Products`/`Product` entity видаляється разом з legacy `Products` table (давно заплановано ADR-006).
5. **business_type** додається до `tenants` table як PostgreSQL enum: `retail` (default), `auto_service`, `warehouse`, `restaurant`, `production`, `distribution`.
6. **item_type** enum: `product`, `service`, `spare_part`, `consumable`, `raw_material`, `kit`. Default: `product`.
7. **location_type** enum: `retail_store`, `warehouse`, `auto_service`, `office`, `production`, `restaurant`. Default: `retail_store`.
8. **FEFO, RLS, batch_number/expiry_date rules незмінні** — трансформація виключно в іменуванні.

Consequences:
+ Платформа відкривається для нових індустрій без зміни архітектурних патернів
+ POC Products table нарешті видаляється (ADR-006 виконується)
+ item_type дозволяє Procurement і AutoService працювати з тим самим каталогом
- Великий обсяг rename-роботи (15+ файлів backend, 20+ frontend, mobile)
- 301-редіректи потрібно прибрати через 1 спринт щоб не залишати dead code
- Тести треба оновити (entity names)

## ADR-013: Per-tenant fiscal provider config in DB, env as fallback, per-tenant IFiscalService resolution
Date: 2026-06-12
Status: accepted

Context: ADR-012 point 5 configures the Checkbox provider via deployment-level env vars (`PRRO__*`), so one process = one fiscal provider for all tenants. ShelfGuard is multi-tenant: each tenant has its own cash register (license key, cashier creds, test vs prod environment). The Claude API key already solved the same problem (TASK-058/060): per-tenant `integration_configs` row (service='claude', JSONB config, RLS) managed via «Налаштування → Інтеграції», with env (`Claude:ApiKey`) as deployment-level fallback — see `ClaudeOrderAdvisor.ResolveAsync`.

Decision:
1. Fiscal provider config moves to the same mechanism: `integration_configs` row with `service='prro'`, JSONB shape `{provider, base_url, license_key, cashier_login, cashier_password, cashier_pin_code}`. `provider` is an extensible enum: `"checkbox"` now, `"disabled"` → NoopFiscalService; future providers (direct-ДПС etc.) are new enum values, no schema change.
2. Resolution order (same as Claude key): tenant's `integration_configs` (service='prro', IsEnabled, RLS-scoped) → fallback to `PRRO__*` env vars (current ADR-012 behavior, kept for single-tenant deployments and CI) → Noop if neither configured.
3. `IFiscalService` resolution becomes per-tenant: a scoped `IFiscalServiceFactory` (Infrastructure/Integrations/Prro) reads the tenant's settings through the RLS-scoped AppDbContext and returns the matching implementation. The startup-time DI switch on `PRRO:PROVIDER` (DependencyInjection.cs) is replaced by the factory; consumers (TASK-068 POS endpoints, TASK-069 retry job) resolve through the factory, never the concrete client. `CheckboxTokenStore` must key cached bearer tokens by tenant+license key, not globally.
4. Secrets are write-only in the API: GET returns masked values (e.g. `••••` + last 4); PUT treats a masked/empty secret field as "keep existing value". This rule applies to the generic integrations endpoint too (known gap: today GET /api/integrations/{service} returns raw credentials).

Consequences:
+ Each tenant connects its own Checkbox register from the web UI — no redeploy, no shared register
+ Same UX and code path as the Claude key — one pattern to learn and audit
+ Env fallback keeps existing prod deployment and live e2e tests working unchanged
- Factory adds a DB read on the fiscal path (mitigated by per-request scoping; config row is tiny)
- Token cache becomes per-tenant — more states to reason about on credential rotation

Extends: ADR-012 (point 5 becomes the fallback layer, not the primary source).

## ADR-012: Checkbox as fiscal provider behind IFiscalService
Date: 2026-06-12
Status: accepted

Context: ADR-011 planned direct integration with the ДПС fiscal server (fs.tax.gov.ua) with our own КЕП signing. КЕП + 1-ПРРО registration is still blocked on the user, which blocks any real fiscalization. The user registered a test cash register with Checkbox (checkbox.ua) — a Ukrainian SaaS ПРРО provider (фіскальний номер TEST582378, test mode). Checkbox handles КЕП signing server-side, fiscalization, offline numbering, and ДПС submission; we talk to its REST API. Auth model: `X-License-Key` header identifies the cash register; a cashier signs in (login/password or PIN) to obtain a bearer token; receipts and shifts go through that token.

Decision:
1. Checkbox becomes the fiscal provider. ADR-011's isolation rule stands: everything Checkbox-specific (HTTP client, DTOs, auth/token handling) lives in `ShelfGuard.Infrastructure/Integrations/Prro`; the Application layer sees only `IFiscalService` and never Checkbox shapes.
2. `IKepSigner` is NOT needed for the Checkbox path — Checkbox signs documents server-side with its own КЕП. The interface stays in the codebase only if/when a direct-ДПС provider is added.
3. The offline-first rule from ADR-011 stays unchanged: sale committed locally first (pos_transaction + items + FEFO write-down in one DB transaction), fiscalization is async with a retry job; `Status = 'pending_fiscalization'` until Checkbox returns a fiscal number.
4. Provider is pluggable behind `IFiscalService`: a future direct-ДПС client (with a real KEP signer) can be added via config switch without any flow changes in Application/API/worker.
5. Config via env (secrets only in `.env`, never committed): `PRRO__PROVIDER=checkbox`, `PRRO__BASEURL` (test: `https://dev-api.checkbox.in.ua/api/v1`, prod: `https://api.checkbox.ua/api/v1`), `PRRO__LICENSEKEY`, `PRRO__CASHIER__LOGIN` / `PRRO__CASHIER__PINCODE`. License key is stored in `.claude/private/access.md`.

Consequences:
+ No ПРРО certification / КЕП burden on our side — Checkbox is already certified with ДПС
+ Demo-able today: test cash register works without waiting for КЕП / 1-ПРРО registration
+ Checkbox handles offline numbering per ПРРО rules — we don't reimplement it
+ Flow (offline-first, async fiscalization, retry job) identical regardless of provider
- Vendor dependency + per-receipt cost on the production plan
- Cashier credentials (login/PIN) still pending from the user — token flow can't be live-tested end-to-end yet

Supersedes: ADR-011 points 2 (IKepSigner/StubKepSigner) for the Checkbox path; points 1, 3, 4 remain in force.

## ADR-011: PRRO fiscal integration — isolated client, pluggable signer, offline-first
Date: 2026-06-12
Status: accepted

Context: v3 Phase 4 needs integration with the ДПС fiscal server (ПРРО). Connectivity confirmed: POST fs.tax.gov.ua:8609/fs/cmd `{"Command":"ServerState"}` → 200 unsigned. All fiscal documents (checks, Z-reports, shift open/close) must be signed with КЕП, which is not yet available (user registering 1-ПРРО). Legal flow also requires offline mode (ПРРО must keep selling when ДПС is unreachable, with offline fiscal numbers).

Decision:
1. Fiscal client lives in `ShelfGuard.Infrastructure/Integrations/Prro` only (same isolation rule as Claude API). Application layer talks to `IFiscalService`; controllers never see ДПС shapes.
2. Signing behind `IKepSigner` (`SignAsync(byte[] document)`). Until КЕП arrives, `StubKepSigner` runs the pipeline in test mode: documents get local numbers, `FiscalNumber = null`, `Status = 'pending_fiscalization'`.
3. Offline-first: every sale is committed locally first (pos_transactions + stock_events + FEFO write-down in one DB transaction); fiscalization is a follow-up step that updates FiscalNumber. A BullMQ retry job re-submits unfiscalized documents.
4. POS UI = new screens in the existing Expo app (tablet layout), not a separate app. Same auth, same API client.

Consequences:
+ Sales never blocked by ДПС availability or missing КЕП — demo-able today
+ КЕП drop-in later: implement real signer + config, no flow changes
+ Single mobile codebase
- Fiscal numbers arrive asynchronously — receipt print/SMS must handle "fiscalization pending"
- Test mode receipts are legally non-fiscal — clearly marked in UI until КЕП configured

## ADR-010: MQTT ingestion lives in the Node worker
Date: 2026-06-12
Status: accepted

Context: v3 Phase 1 needs an MQTT consumer for weight/temperature sensors (v3-spec §1, §4). Options: (a) MQTT client hosted inside ASP.NET Core API; (b) a dedicated subscriber in the existing Node worker service.

Decision: The worker subscribes to Mosquitto (`mqtt` npm package, topic `shelfguard/{tenant_id}/{store_id}/#`) and owns the full ingestion path: validate device → write temperature_readings / weight_readings → derive stock_events → enqueue notifications via the existing BullMQ pipeline. The ASP.NET API never talks to MQTT; it only serves CRUD for iot_devices and read endpoints for readings. Mosquitto runs as a docker-compose service.

Consequences:
+ Reuses the worker's existing always-on process, pg pool, notification queue, and Telegram path (same pattern as telegram-listener)
+ API stays request/response only — no hosted background services
+ Ingestion can be scaled/restarted independently of the API
- Sensor business rules (confidence, alert thresholds) live in TypeScript, not C# — acceptable: they are stream-processing rules, not request-path domain logic
- Worker now requires MQTT_URL env; local dev needs Mosquitto up for IoT features

## ADR-009: IAnalyticsRepository in Application layer
Date: 2026-06-04
Status: accepted

Context: Analytics queries return DTO aggregates (ExpirySummaryDto, LossesDto etc.), not domain entities. The IRepository pattern in Domain requires returning entities; placing IAnalyticsRepository in Domain would create a Domain → Application circular reference.

Decision: IAnalyticsRepository is defined in ShelfGuard.Application.Features.Analytics (same namespace as IAnalyticsService). Infrastructure implements it. Domain is unaware of analytics contracts.

Consequences:
+ Avoids circular dependency
+ Analytics stays as a read-model concern, cleanly separated
- Minor inconsistency: most IRepository interfaces live in Domain.Interfaces; this one does not
- Future devs must know the exception exists (documented here)

## ADR-001: BullMQ with ASP.NET Core
Date: 2026-06-03
Status: accepted

Context: v1-spec requires BullMQ for background jobs. BullMQ is Node.js-only. Main API is ASP.NET Core.

Decision: Separate /worker Node.js service. API writes to Redis via StackExchange.Redis. Worker reads via BullMQ.

Consequences:
+ BullMQ used as specified; .NET remains primary business logic layer; worker scales independently
- Extra service to maintain; Redis required in infrastructure

---

## ADR-002: Modular Monolith over Turborepo
Date: 2026-06-03
Status: accepted

Context: v1-spec mentioned Turborepo monorepo.

Decision: Single ASP.NET Core solution with feature-based modules. No Turborepo. Frontend and mobile are separate npm projects.

Consequences: + Simpler deployment. - Less isolation between modules (mitigated by strict layer rules).

---

## ADR-003: Expo SDK 56 for Mobile
Date: 2026-06-03
Status: accepted

Decision: Expo SDK 56 with Expo Router, NativeWind v4 (spec said SDK 51+, updated to latest stable).

---

## ADR-004: Port Mapping (avoid local conflicts)
Date: 2026-06-03
Status: accepted

Decision:
- Docker PostgreSQL → port 5435 (avoids conflict with local 5432)
- Docker Redis → port 6380 (avoids conflict with local 6379)
- Connection string: `Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password`

---

## ADR-005: Worker scaffold in TASK-000
Date: 2026-06-03
Status: accepted

Decision: /worker scaffold created upfront (package.json, tsconfig, Dockerfile, job stubs). Real logic in TASK-008 / TASK-017.

---

## ADR-006: Separate catalog_products table (not replacing Products)
Date: 2026-06-04
Status: accepted

Context: TASK-002 (full schema) needed to add the v1 tenant-aware `products` table from the spec. The POC `Products` table (EF Core default name = "Products", no tenant_id) already exists and powers the catalog API.

Decision: Create new `catalog_products` table (EF entity `CatalogProduct`) for the v1 tenant-aware product catalog. Keep legacy `Products` table intact until TASK-003b migrates the catalog API.

Consequences:
+ No breaking change to existing catalog API
+ Full schema deployed without disrupting running dev environment
- Two product tables exist temporarily; devs must know which one to use
- `product_stock` references `catalog_products`, not legacy `Products`

Supersedes: nothing — this is additive.

---

## ADR-007: Dashboard data from POC Products (temporary proxy)
Date: 2026-06-04
Status: accepted (temporary)

Context: Dashboard stat cards (Safe/Warning/Critical/Expired) require real `product_stock` batch data with expiry dates. That endpoint does not exist yet.

Decision: Derive dashboard stats from POC `/api/products` using `stockQuantity vs reorderLevel` as proxy. Clearly documented as placeholder. "Expired" = stockQuantity is 0 (incorrect semantically, acceptable for demo).

Superseded by: TASK-011 + TASK-016 (real analytics endpoint from `product_stock`).

---

## ADR-008: RLS column names must be double-quoted
Date: 2026-06-04
Status: accepted

Context: EF Core creates columns with PascalCase names (e.g., `"TenantId"`). PostgreSQL folds unquoted identifiers to lowercase. Raw SQL in RLS policies using `tenant_id` (unquoted) throws `column "tenant_id" does not exist`.

Decision: All column references in manually-written RLS SQL must be double-quoted to match EF Core's PascalCase: `"TenantId"`, `"Id"`, `"StoreId"`, etc.

Rule: applies to all `migrationBuilder.Sql()` calls that reference column names.
