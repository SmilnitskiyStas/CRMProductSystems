# Current Sprint — v4.1 «Supplier Self-Service» (started 2026-07-02)

Архітектура: ADR-016 (`.claude/docs/decisions.md`). Supplier = окремий tenant
(`business_type = "supplier"`, модуль `marketplace_supplier`), роль `supplier_admin`,
кабінет `/api/supplier-cabinet/*` + frontend `/supplier/*`. RLS — існуючі політики.

---

## TASK-282 — DB: supplier business_type, IsOwnerManaged, дефолтні модулі
**Status:** done (2026-07-02, migration `20260702192126_V41SupplierSelfService`, log: `282_2026-07-02_supplier-self-service-db_database-engineer.md`) · **Agent:** database-engineer · **Depends:** — 
Міграція `V41SupplierSelfService`:
- `supplier_profiles.IsOwnerManaged boolean NOT NULL DEFAULT false` + partial unique index
  `UX_supplier_profiles_owner_tenant ON supplier_profiles ("TenantId") WHERE "IsOwnerManaged"` 
  (колонки в raw SQL — у подвійних лапках, ADR-008).
- Domain: `Tenant.DefaultModulesForBusinessType` — новий кейс `"supplier"` → `["marketplace_supplier"]`.
- Перевірити, що існуючі RLS-політики supplier_* мають NULLIF-guard (патерн d8abc4d8); якщо ні — включити в цю міграцію.
- Дані не мігруються: existing suppliers (`TenantId = Guid.Empty`) без змін.
**Accept criteria:** міграція up/down чиста на dev-базі; unique index не конфліктує з existing rows; `dotnet build` + тести green.

---

## TASK-283 — Backend: роль supplier_admin + онбординг supplier-tenant
**Status:** done (2026-07-02, log: `283-285_2026-07-02_supplier-self-service-backend_backend-developer.md`) · **Agent:** backend-developer · **Depends:** TASK-282
- `AppRoles`: додати `SupplierAdmin = "supplier_admin"` (+ у `All`).
- Admin tenant onboarding (`Admin` feature): при створенні tenant з `business_type = "supplier"` — 
  автоматично створити `Supplier` (`TenantId` = new tenant id) + `SupplierProfile`
  (`IsOwnerManaged = true`, `IsPublic = false`); перший user tenant-а отримує роль `supplier_admin`.
- Policy/authorization: supplier_admin НЕ входить у tenant-staff політики (stock/pos/etc.) — тільки кабінет.
**Accept criteria:** створення supplier-tenant через `/api/admin/tenants` дає tenant + user + Supplier + Profile однією транзакцією; supplier_admin отримує 403 на `/api/stock`; тести на онбординг-hook.

---

## TASK-284 — Backend: SupplierCabinetController (профіль, товари, відгуки)
**Status:** done (2026-07-02, log: `283-285_2026-07-02_supplier-self-service-backend_backend-developer.md`) · **Agent:** backend-developer · **Depends:** TASK-283
Новий `SupplierCabinetController` (`/api/supplier-cabinet`), `[Authorize]` роль supplier_admin + `[RequireModule("marketplace_supplier")]`. Resolve «мій Supplier» по `tenant_id` через `IsOwnerManaged`-профіль:
- `GET /profile`, `PUT /profile` (region, categories, website, delivery_regions, working_hours, payment_terms), `POST /profile/publish` (toggle `IsPublic`)
- `GET /items`, `POST /items`, `PUT /items/{id}`, `DELETE /items/{id}` — реюз Admin*-методів `MarketplaceService` (параметризувати supplierId)
- `GET /reviews` (read-only), `GET /metrics`
**Accept criteria:** усі ендпоінти працюють лише в контексті свого tenant (RLS-перевірка: другий supplier-tenant не бачить чужі items); provider-created suppliers (Guid.Empty) недоступні через кабінет; unit-тести на resolve + CRUD.

---

## TASK-285 — Backend: reviews hardening + публічні відгуки + rating recalc
**Status:** done (2026-07-02, log: `283-285_2026-07-02_supplier-self-service-backend_backend-developer.md`) · **Agent:** backend-developer · **Depends:** TASK-282
- `CreateReviewAsync`: guard — reviewer tenant ≠ `supplier.TenantId` та reviewer `business_type != "supplier"` (400); дубль уже дає 409.
- Після створення відгуку — синхронний перерахунок `SupplierMetrics.Rating` = AVG(rating) (створити metrics-рядок, якщо нема).
- Новий публічний `GET /api/marketplace/suppliers/{id}/reviews` (`[AllowAnonymous]`, paginated) — rating, comment, created_at, назва tenant-рецензента (denormalized display name, без id).
**Accept criteria:** self-review → 400; supplier-tenant review → 400; rating у публічному листингу оновлюється після нового відгуку; тести на guard + recalc.

---

## TASK-286 — Frontend: supplier cabinet (роль, sidebar, сторінки)
**Status:** done (2026-07-03, log: `286-287_2026-07-03_supplier-cabinet-marketplace-frontend_frontend-developer.md`) · **Agent:** frontend-developer · **Depends:** TASK-284
- `lib/roles.ts`: `SupplierAdmin` + set `SUPPLIER_ONLY`; supplier_admin виключити з tenant-staff sets.
- Sidebar: для supplier_admin — тільки група «Кабінет постачальника» (Профіль / Мої товари / Відгуки) + профіль користувача.
- Нова feature `features/supplier-cabinet/` (`types.ts`, `api/`, `hooks/`, `components/`), сторінки `(dashboard)/supplier/profile`, `/supplier/items`, `/supplier/reviews`. Реюз компонентів `features/marketplace/` (AddSupplierItemModal, форма профілю) де можливо.
- Admin onboarding UI: у формі створення tenant — опція business_type `supplier`.
**Accept criteria:** supplier_admin після логіну бачить лише кабінет; CRUD товарів і publish-toggle працюють; `tsc --noEmit` + `npm run build` green.

---

## TASK-287 — Frontend: marketplace enrichment — рейтинг і відгуки видимі клієнтам
**Status:** done (2026-07-03, log: `286-287_2026-07-03_supplier-cabinet-marketplace-frontend_frontend-developer.md`) · **Agent:** frontend-developer · **Depends:** TASK-285
- `/marketplace/[id]`: блок «Відгуки» (список з `GET /suppliers/{id}/reviews`, зірки, дата, ім'я рецензента) + існуюча форма «залишити відгук» показує 400/409 помилки guard-ів.
- `SupplierCard` у листингу: рейтинг (зірки + число) і кількість відгуків; фільтр за категорією вже є — переконатися, що категорії supplier-профілів відображаються.
**Accept criteria:** рейтинг/відгуки видно і анонімно, і клієнт-tenant-ам; свіжий відгук одразу оновлює рейтинг (invalidate query); `tsc --noEmit` + build green.

---

## TASK-288 — QA: supplier self-service regression
**Status:** done (2026-07-03, log: `.claude/logs/reviews/qa_282-288_2026-07-03.md`) · **Agent:** qa-tester · **Depends:** TASK-286, TASK-287
Усі 6 сценаріїв + регресія + `dotnet test` 494/494 + `tsc --noEmit` — PASS (локальний стек).
Знайдено 2 pre-existing баги (не блокують v4.1):
- **BUG-009 (high, deploy/env):** 8 hand-written міграцій без `[Migration]`/`[DbContext]` атрибутів
  (AddProviderRoles, AddNotificationIsRead, 2×ProviderBypassRls, AddItemPerishabilityClass,
  ForceRlsOnAllTenantTables, 2×FixRlsNullIf) — EF `MigrateAsync` їх НЕ бачить; свіжа БД отримує
  неповну схему (login 500: ProviderRoleId missing). Локальну dev-базу полагоджено вручну.
- **BUG-010 (medium):** `GET /api/marketplace/suppliers/{id}` віддає unpublished-профіль
  (IsPublic=false) навіть анонімно — detail не фільтрує is_public (листинг/search фільтрують).
Low-нотатки (див. QA-лог): review-guard-и 400 екрануються module gate 403 для supplier-tenant-ів;
supplier_admin має 200 на /api/notifications/history (свій tenant, порожньо).
Тест-план: (1) онбординг supplier-tenant провайдером; (2) ізоляція — supplier A не бачить дані supplier B і клієнтських tenant-ів (RLS); (3) supplier_admin 403 на всі tenant-staff ендпоінти; (4) publish-toggle → поява/зникнення в публічному листингу; (5) review-флоу: клієнт лишає відгук, дубль → 409, self-review → 400, рейтинг перерахований; (6) module gate: деактивація `marketplace_supplier` → 403 кабінету.
**Accept criteria:** усі 6 сценаріїв пройдені на dev; знайдені баги оформлені як BUG-задачі.

---

# Previous Sprint — v3.5 «Provider UX» (started 2026-06-21)

---

## TASK-281 — Dashboard і /stock: консистентний фільтр магазину
**Status:** done · **Agent:** frontend-developer · **Depends:** TASK-280 · Updated: 2026-07-02
Дашборд (stats, «Потребують уваги», карта зон) викликав `/api/stock*` без
`store_id` — показував дані всіх магазинів, тоді як `/stock` фільтрує за
`selectedStoreId` з header StoreSelector. Після «Переглянути всі» список міг
бути порожнім. Fix: `frontend/features/dashboard/api/dashboard.ts` — усі три
функції приймають `storeId` (helper `withStore` додає `store_id=` до URL);
`frontend/features/dashboard/hooks/useDashboard.ts` — хуки читають
`selectedStoreId` з `useStoreContext` і включають його в queryKey. Бекенд
(`StockController`) вже приймає `store_id?` на `/api/stock`, `/summary`,
`/zones-summary`. Коли магазин не вибрано (`null`) — параметр не додається,
обидві сторінки показують все. `tsc --noEmit` та `npm run build` — green.
Log: `281_2026-07-02_dashboard-store-consistency_frontend-developer.md`

---

## TASK-280 — Dashboard: блок «Потребують уваги» — 5 рядків + «Переглянути всі»
**Status:** done · **Agent:** frontend-developer · **Depends:** — · Updated: 2026-07-02
Блок `AttentionTable` не мав обмеження висоти — при багатьох товарах займав пів
сторінки. Fix (у `frontend/features/dashboard/components/AttentionTable.tsx`):
показуються перші 5 рядків поточного фільтра; нижче кнопка
«Переглянути всі (N)» (лише коли рядків > 5). Ціль навігації — `/stock`
(сторінки `/shelf` немає): таб «All» → `/stock`, таби Expired/Critical/Warning →
`/stock?status=<value>` — сторінка вже читає `status` з query params, значення
збігаються зі `StockFilters`, тож фільтр преселектнутий. Стилі — існуючий
inline dark-theme патерн блоку. `tsc --noEmit` та `npm run build` — green.
Log: `280_2026-07-02_dashboard-attention-view-all_frontend-developer.md`

---

## TASK-279 — Повідомлення про завершення сеансу при неактивності
**Status:** done · **Agent:** frontend-developer · **Depends:** — · Updated: 2026-07-02
Раніше при протуханні access token + невдалому refresh `frontend/lib/api.ts` робив
жорсткий redirect на `/login` без пояснення — користувача «викидало» мовчки.
Fix: redirect тепер на `/login?reason=session_expired`; на сторінці логіну новий
клієнтський компонент `SessionExpiredNotice` (features/auth/components) читає параметр
через `useSearchParams` (обгорнуто в `<Suspense>` у server-сторінці) і показує amber-банер
«Час сеансу сплив. Будь ласка, увійдіть знову.» над формою — той самий візуальний патерн,
що й error-блок у LoginForm, але warning-тон (#F59E0B), бо це очікувана подія.
`middleware.ts` без змін: він не може відрізнити «сеанс сплив» від «перший візит»
(в обох випадках cookie відсутні), тож reason ставить лише api.ts після фактичного
провалу refresh. `tsc --noEmit` та `npm run build` — green.
Log: `279_2026-07-02_session-expired-notice_frontend-developer.md`

---

## BUG-009 — 8 hand-written міграцій без [Migration]/[DbContext] атрибутів
**Status:** done · **Agent:** database-engineer (+ main session verification) · Updated: 2026-07-03
Found in QA v4.1: EF `MigrateAsync` ігнорував 8 ручних міграцій (AddProviderRoles,
AddNotificationIsRead, ServiceDesk/Team provider bypass RLS, ItemPerishabilityClass,
ForceRlsOnAllTenantTables, 2× NULLIF RLS-фікси) — свіжа БД розгорталась неповною.
Fix: додано атрибути `[DbContext(typeof(AppDbContext))]` + `[Migration("<id>")]`,
міграції переписані на ідемпотентний SQL (IF NOT EXISTS / OR REPLACE guards),
snapshot оновлено. На проді вони виконаються ПОВТОРНО при наступному деплої
(відсутні у __EFMigrationsHistory) — ідемпотентність перевірена: DELETE 8 рядків
історії на локальній БД з існуючими обʼєктами → повторний прогін чистий.
`dotnet ef migrations list` показує всі 9; build green; tests 500/500.
Log: `bug009_2026-07-03_orphan-migrations_database-engineer.md`

---

## BUG-010 — GET /api/marketplace/suppliers/{id} віддає unpublished профіль
**Status:** done · **Agent:** backend-developer · **Depends:** — · Updated: 2026-07-03
Found in QA v4.1 (`qa_282-288_2026-07-03.md`). Листинг/search фільтрують `IsPublic`,
але detail-ендпоінт — ні: неопублікований профіль був доступний будь-кому за id.
Fix: `MarketplaceService.GetSupplierProfileAsync` повертає `null` (→404) якщо
`profile.IsPublic == false` — для анонімних і автентифікованих. Legitimate доступи
не зачеплені: supplier cabinet читає свій профіль через `ISupplierCabinetService.
GetOwnerManagedProfileAsync` (окремий шлях), MarketplaceAdminController використовує
лише Admin*-методи — інших call sites у `GetSupplierProfileAsync` нема.
Tests: +2 unit (unpublished→null для anon/auth, published→dto). `dotnet build` 0 warn.
Follow-up (main session, 2026-07-03): той самий guard додано в `GetSupplierItemsAsync`
і `GetSupplierReviewsAsync` (приватний `IsPublishedAsync`) → `/items` і `/reviews`
unpublished-постачальника тепер теж 404. +4 unit tests. `dotnet test` 500/500 green.
Log: `bug010_2026-07-03_unpublished-supplier-leak_backend-developer.md`

---

## BUG-011 — банер «Час сеансу сплив» після ручного «Вийти»
**Status:** done · **Agent:** frontend-developer · **Depends:** — · Updated: 2026-07-03
Repro: клік «Вийти» → /login з банером session_expired (TASK-279), хоча вихід ручний.
Cause: in-flight polling (SupportChatWidget 3с, notifications badge) ловив 401 після
відкликання refresh cookie → `apiFetch` робив hard redirect `/login?reason=session_expired`,
перебиваючи чистий `router.push("/login")` з `useLogout`.
Fix (`frontend/lib/api.ts` + `useAuth.ts`): module-level прапорець `markLoggedOut()`,
який `useLogout.mutationFn` ставить ПЕРЕД `authApi.logout()`; у 401-гілці `apiFetch`
при прапорці — тихий `ApiError` без refresh/redirect (перевірка і до, і після tryRefresh
для гонки). Прапорець скидається в `setToken()` (login/refresh). Додатково: 401 без
токена на момент запиту → редірект на `/login` БЕЗ reason (не «сеанс сплив»).
TASK-279 сценарій не зачеплено: протухла сесія з токеном далі дає reason=session_expired.
`npx tsc --noEmit` + `npm run build` green.
Log: `bug011_2026-07-03_logout-expired-banner_frontend-developer.md`

---

## BUG-013 — майстер «Новий клієнт» (provider): нема типу «Постачальник» + кирилична назва блокує «Далі»
**Status:** done · **Agent:** frontend-developer · **Depends:** — · Updated: 2026-07-03
Repro: CreateTenantWizard (панель провайдера) не мав business type «Постачальник»
(supplier додано лише в admin у TASK-286); кирилична назва → slugify відкидав усі
не-ASCII символи → slug порожній → кнопка «Далі» disabled.
Fix: (1) `features/provider/types.ts` — `supplier` у BusinessType, labels («Постачальник»,
🚚), ALL_BUSINESS_TYPES, preset `["marketplace_supplier"]`; `marketplace_supplier` у
TenantModule + MODULE_LABELS/DESCRIPTIONS/ALL_MODULES (звірено з Tenant.cs, TASK-282).
(2) Спільна util `lib/slug.ts` — транслітерація укр→лат (щ→shch, ї→yi, х→kh тощо) +
санітизація; використана в CreateTenantWizard і admin/CreateTenantModal (там була та сама
вада). Назва компанії зберігається як введена — транслітерується тільки slug.
tsc + next build green.
Log: `bug013_2026-07-03_provider-wizard-supplier-slug_frontend-developer.md`

---

## BUG-012 — POST /api/admin/marketplace/suppliers 500 (FK violation) на prod
**Status:** done · **Agent:** backend-developer · **Depends:** — · Updated: 2026-07-03
Root cause: `MarketplaceService.AdminCreateSupplierAsync` хардкодив `TenantId = Guid.Empty`
→ INSERT у `suppliers` порушував FK `FK_suppliers_tenants_TenantId` (тенант 00000000-… не
існує). Флоу TASK-275 «+ Створити постачальника» падав 500 завжди — рядків з Guid.Empty
у prod немає.
Fix: get-or-create системний tenant «Platform Marketplace» (slug `platform-marketplace`,
business_type=supplier, inactive, без users) — `MarketplaceRepository.
GetOrCreatePlatformTenantIdAsync` (ліниво, race-safe по unique slug + detach на програші);
`AdminCreateSupplierAsync` використовує його id. Supplier cabinet не зачеплено: профілі
admin-флоу мають `IsOwnerManaged = false`, кабінет фільтрує `IsOwnerManaged = true` —
покрито тестом. Чому TASK-275-тести не зловили: NSubstitute-моки репо не перевіряють FK;
додано 4 repo-тести на EF InMemory (перший виклик створює tenant, другий/крос-контекст
реюзає; cabinet-лукап не бачить platform-suppliers) + 2 service-тести. ADR-016 amendment
у `decisions.md`. Build green, 506/506 тестів.
Log: `bug012_2026-07-03_admin-supplier-fk_backend-developer.md`
**Next:** deploy to prod; re-check «+ Створити постачальника» на /marketplace.

---

## BUG-007 — /api/movements 500: паралельні запити на одному DbContext
**Status:** done · **Agent:** backend-developer · **Depends:** — · Updated: 2026-07-02
Found during store_manager role QA (follow-up to BUG-006). На prod `/api/movements`
повертав 500 на кожен виклик (5/5 запитів fail).
Root cause: `MovementService.GetAsync` запускав `_repo.GetAsync` і `_repo.CountAsync`
паралельно через `Task.WhenAll` на одному scoped `AppDbContext`. DbContext не
thread-safe → «A second operation was started on this context instance…» → 500.
Fix: обидва запити виконуються послідовно через `await` у
`ShelfGuard.Application/Features/Movements/MovementService.cs`. Grep по всьому
Application + Infrastructure: інших `Task.WhenAll` над одним DbContext немає.
Build green, 459/459 тестів.
Log: `bug007-008_2026-07-02_movements-concurrency-topproducts-jsonb_backend-developer.md`
**Next:** deploy to prod; re-run store_manager QA pass.

---

## BUG-008 — /api/analytics/pos/top-products 500: jsonb Barcodes у SQL-проєкції
**Status:** done · **Agent:** backend-developer · **Depends:** — · Updated: 2026-07-02
Found during store_manager role QA (follow-up to BUG-006). Ендпоінт падав 500 навіть
після фіксу DateTime Kind (BUG-006).
Root cause: `AnalyticsRepository.GetPosTopProductsAsync` проєктував
`i.Product!.Barcodes.Count > 0 ? i.Product.Barcodes[0] : null` всередині SQL-запиту.
`Barcodes` — `List<string>` mapped to `jsonb`; Npgsql не транслює `.Count` / індексер
`[0]` над jsonb-списком → runtime translation exception → 500.
Fix: у проєкції вибирається весь список (`Barcodes = i.Product!.Barcodes`), перший
штрихкод береться client-side (`FirstOrDefault()`) після `ToListAsync` — той самий
патерн, що в `DailySalesRepository.cs:50-54`. Інші `Barcodes.Count/[0]` у кодовій базі —
в Application-сервісах над матеріалізованими entity, не в IQueryable — не зачеплені.
Build green, 459/459 тестів.
Log: `bug007-008_2026-07-02_movements-concurrency-topproducts-jsonb_backend-developer.md`
**Next:** deploy to prod; re-run store_manager QA pass on POS analytics.

---

## BUG-006 — Analytics 500: DateTimeKind.Unspecified vs timestamptz
**Status:** done · **Agent:** backend-developer · **Depends:** — · Updated: 2026-07-02
Found during QA of store_manager role. On prod усі 4 POS analytics ендпоінти
(`/api/analytics/pos/summary`, `revenue-trend`, `top-products`, `cashiers`) повертали 500,
а `/api/analytics/write-offs` та `/api/movements` — 500 тільки з `from=&to=` фільтрами.
Root cause: `DateOnly.ToDateTime(TimeOnly.MinValue/MaxValue)` в `AnalyticsRepository.cs`
дає `DateTime` з `Kind=Unspecified`; Npgsql відхиляє такі параметри для `timestamptz`
колонок (`pos_transactions.CreatedAt` тощо) → runtime exception → 500. Тести не ловили,
бо використовують fake-репозиторії.
Fix: приватні хелпери `ToUtcStart(DateOnly)` / `ToUtcEnd(DateOnly)` через
`ToDateTime(..., DateTimeKind.Utc)`; замінено всі 14 конверсій. `MovementRepository` вже
використовував правильний overload — без змін. Build green, 459/459 тестів.
Log: `bug006_2026-07-02_analytics-datetime-kind-500_backend-developer.md`
**Next:** deploy to prod; re-run store_manager QA pass on analytics endpoints.

---

## TASK-278 — Live Chat: живий чат провайдер ↔ клієнт
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** — · Updated: 2026-06-21
Різниця між тікетом і чатом: тікет — для довгострокових задач (налаштування компанії), чат — миттєве спілкування.
**DB (міграція AddChatFeature):**
- `chat_sessions` (id, tenant_id, created_by_user_id, subject TEXT, status open/closed, created_at, updated_at; RLS на tenant_id)
- `chat_messages` (id, session_id, sender_user_id, sender_name TEXT, body TEXT, is_read, created_at; RLS через session → tenant_id)
**Backend:**
- `POST /api/chat/sessions` — клієнт відкриває нову сесію (перший повідомлення)
- `GET /api/chat/sessions` — клієнт бачить свої сесії (свій tenant)
- `GET /api/chat/sessions/{id}/messages` — список повідомлень сесії
- `POST /api/chat/sessions/{id}/messages` — надіслати повідомлення (клієнт або провайдер)
- `POST /api/chat/sessions/{id}/close` — закрити сесію
- `GET /api/admin/chat/sessions` (ProviderOnly) — всі сесії cross-tenant
- `GET /api/admin/chat/sessions/{id}/messages` (ProviderOnly) — повідомлення клієнта
- `POST /api/admin/chat/sessions/{id}/messages` (ProviderOnly) — відповідь провайдера
- `POST /api/admin/chat/sessions/{id}/close` (ProviderOnly) — закрити сесію
**Frontend (клієнт) — `SupportChatWidget.tsx`:**
- Повністю переробити: замість тікету показати список чат-сесій + кнопку "Новий чат"
- Активна сесія: вигляд як у месенджері (бульки повідомлень), input внизу, відправка через Enter/кнопку
- Polling кожні 3 секунди через `refetchInterval` React Query (без WebSocket)
**Frontend (провайдер) — нова вкладка в `/service-desk`:**
- Панель "Живий чат" поруч із існуючим Service Desk
- Список чат-сесій усіх клієнтів (ім'я, тенант, остання активність, кількість непрочитаних)
- При натисканні — повна переписка + input для відповіді
- Нові повідомлення підсвічуються, polling кожні 3с
Accept: dotnet build green; міграція green; клієнт може надіслати повідомлення, провайдер його бачить і відповідає; tsc + next build green.

## TASK-277 — Команда: створення користувача з логіном/паролем та правами
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** — · Updated: 2026-06-21
**Backend:**
- Розширити `InviteProviderMemberRequest` полем `Password?: string` (необов'язкове)
- В `ProviderTeamService.InviteMemberAsync`: якщо `Password` передано → хешувати його замість `tempPassword`
- Якщо `Password` не передано — поведінка залишається як є (tempPassword)
**Frontend — `InviteProviderMemberModal.tsx`:**
- Додати поля: «Пароль» (type=password) + «Підтвердження паролю»
- Валідація: обидва поля повинні збігатися, мінімум 6 символів
- Додати секцію «Права доступу» — readonly список того, що може робити обрана роль:
  - provider_admin: управління командою, всі клієнти, Service Desk, Чат
  - provider_agent: Service Desk, Чат, перегляд клієнтів
- Кнопка тепер «Створити користувача» (а не «Запросити»)
Accept: backend build green; фронтенд: tsc green; можна створити провайдер-агента з власним паролем, він може увійти в систему з цим паролем.

## TASK-276 — Розклад: множинний вибір днів при додаванні зміни
**Status:** done · **Agent:** frontend-developer · **Depends:** — · Updated: 2026-06-21
Поточний `AddSlotModal` у `ScheduleTab.tsx` дозволяє вибрати лише один день.
**Зміни:**
- Замінити `<select>` для дня тижня на 7 чекбоксів (Пн–Нд) у горизонтальній сітці
- Форма дозволяє виділити будь-яку кількість днів (мінімум 1)
- При сабміті — послідовно викликати `create.mutateAsync` для кожного вибраного дня з однаковими `userId`, `startTime`, `endTime`, `notes`
- Стан форми: `dayOfWeek` → `daysOfWeek: number[]`
- Якщо будь-який з викликів повертає помилку — показати її й зупинитись
- Після успіху — закрити модалку (одиночний `onClose()`)
Accept: tsc green; можна обрати 3 дні → backend отримує 3 POST-запити → 3 слоти з'являються у grid.

## TASK-275 — Маркетплейс: Full-width + Створення постачальника + Додавання товарів
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** — · Updated: 2026-06-21
**Frontend (швидке виправлення):**
- У `frontend/app/(dashboard)/marketplace/page.tsx` рядок 80: видалити `maxWidth: 1200` зі стилів обгортки
**Backend — нові провайдер-ендпоінти (`MarketplaceAdminController`):**
- `POST /api/admin/marketplace/suppliers` (ProviderOnly) — створити нового постачальника:
  Body: `{ companyName, region, categories[], website?, deliveryRegions[], workingHours?, paymentTerms?, isPublic, plan }`
  Дія: CREATE `Supplier` (tenantId = provider tenant_id) + CREATE `SupplierProfile` для нього
- `POST /api/admin/marketplace/suppliers/{id}/items` (ProviderOnly) — додати товар:
  Body: `{ customName, price?, minQty?, unit?, isAvailable }`
  Дія: CREATE `SupplierItem` (supplierId = id)
- `DELETE /api/admin/marketplace/suppliers/{id}/items/{itemId}` (ProviderOnly) — видалити товар
**Frontend — сторінка `/marketplace`:**
- Додати кнопку «+ Створити постачальника» (видима лише для PROVIDER_TEAM ролей) поруч із пошуковим рядком
- `CreateSupplierModal.tsx` (`features/marketplace/components/`): форма з полями companyName, region, categories (textarea через кому), isPublic toggle, plan select (free/premium)
- На `SupplierCard.tsx` або `marketplace/[id]/page.tsx` — кнопка «+ Додати товар» (видима для PROVIDER_TEAM):
  `AddSupplierItemModal.tsx`: customName, price, minQty, unit, isAvailable toggle
- Hooks: `useCreateSupplier`, `useAddSupplierItem`, `useDeleteSupplierItem` у `features/marketplace/hooks/`
Accept: backend build green; tsc + next build green; провайдер може створити постачальника → він з'являється у списку; можна додати/видалити товар; сторінка на всю ширину.

---

## v3.4 carry-over

## TASK-274 — Provider Schedule (розклад команди)
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** TASK-272 · Updated: 2026-06-20
Тижневий розклад доступності для агентів: recurring slots (DayOfWeek 0-6 + time range).
Backend: entity `ProviderScheduleSlot` + migration `AddProviderScheduleSlots` + `ProviderScheduleController`
(GET ?userId=, POST, DELETE/{id}; ProviderTeamMember/ProviderCanInvite policies).
Frontend: `ScheduleTab.tsx` — 7-колонковий weekly grid + AddSlotModal.
Build green, migration green, tsc green.
Log: `274_2026-06-20_provider-schedule_backend-developer.md`

## TASK-273 — Provider Employee Statistics
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** TASK-272 · Updated: 2026-06-20
Статистика продуктивності команди (без нової схеми): assigned/resolved tickets, created-by-provider, comments, avg resolution time.
Backend: `IProviderStatsRepository` + `ProviderStatsRepository` (cross-tenant) + `ProviderStatsService` + `GET /api/provider/team/stats`.
Frontend: `StatsTab.tsx` — таблиця з прогрес-баром resolve rate + кольоровими метриками.
Build green, tsc green.
Log: `273_2026-06-20_provider-employee-stats_backend-developer.md`

## TASK-272 — Provider HR: управління власним персоналом
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** — · Updated: 2026-06-20
Розширення команди провайдера: редагування учасника + реактивація.
Backend: `PUT /api/provider/team/{id}` + `POST /api/provider/team/{id}/reactivate` ([ProviderCanInvite]).
Frontend: `EditMemberModal.tsx` (нова) + оновлений `TeamTab.tsx` з кнопками Edit/Відновити.
Guard: роль власника (`provider`) не може бути змінена через API.
Build green, tsc green.
Log: `272_2026-06-20_provider-hr-staff-management_backend-developer.md`
**Next:** TASK-273 (employee performance stats), TASK-274 (schedule/calendar UI).

---

## TASK-271 — Backend: Provider cross-tenant Service Desk
**Status:** done · **Agent:** backend-developer · **Depends:** TASK-251 · Updated: 2026-06-20
Provider може бачити тікети з усіх тенантів та створювати тікети від імені клієнта.
Нові ендпоінти (ProviderOnly policy):
- `GET  /api/admin/service-desk?status=&tenantId=` — всі тікети cross-tenant
- `POST /api/admin/service-desk` — створити тікет для клієнтського тенанту
Нові файли: `IProviderTicketRepository`, `ProviderTicketRepository`, `IProviderTicketService`,
`ProviderTicketService`, `ProviderServiceDeskDtos`, `AdminServiceDeskController`.
Migration `AddTicketCreatedByProvider` — `CreatedByProvider bool DEFAULT false` на `support_tickets`.
Тікет зберігається з `TenantId = client tenant` + `CreatedByProvider = true` → клієнт бачить у
своєму Service Desk, Провайдер бачить у cross-tenant запиті.
Build green, 459/459 тестів.
Log: `271_2026-06-20_provider-service-desk-backend_backend-developer.md`
**Next:** TASK-272 Provider HR (власний персонал), TASK-270 chat button in header.

---

## BUG-005 — pos_transactions.RetryCount missing on production
**Status:** done · **Agent:** database-engineer · **Depends:** — · Updated: 2026-06-16
Flagged in TASK-204 log: prod threw `column p.RetryCount does not exist` in
`PosService.GetPendingFiscalizationAsync`. Root cause: migration
`20260613000000_AddPosTransactionRetryCount` (TASK-069, committed 2026-06-13) was never
actually deployed to prod. Fix: regenerated as `20260616151654_AddPosTransactionRetryCount`
(same single AddColumn, fresh timestamp so it lands after the v4 rename migrations on next
deploy). Build green, Pos tests 76/76 green.
Log: `bug005_2026-06-16_pos-retrycount-missing-column_database-engineer.md`
**Next:** verify on next prod deploy that the migration applies and fiscalization retry
worker stops erroring.

---

## TASK-078 — Mobile: Write-offs screen
**Status:** done · **Agent:** mobile-developer · **Depends:** — · Updated: 2026-06-15
Екран списання для мобільного працівника:
- Список власних списань (GET /api/write-offs)
- Кнопка «+ Списання» → scan штрихкод (expo-camera) → підтягнути назву товару → вибір причини (expired/damaged/theft/other) → кількість → коментар → підтвердження
- Detail екран окремого списання
- Тільки для ролей: storekeeper, store_manager і вище
Accept: tsc green; flow проти API (create + list); scan штрихкоду відкриває форму з назвою товару.

## TASK-079 — Mobile: Transfers screen
**Status:** done · **Agent:** mobile-developer · **Depends:** — · Updated: 2026-06-15
Екран переміщень між магазинами/зонами:
- Список переміщень (GET /api/transfers)
- Кнопка «+ Переміщення» → scan штрихкод → кількість → вибір destination store → підтвердження
- Статуси: pending / in_transit / completed
- Тільки для ролей: storekeeper, store_manager і вище
Accept: tsc green; create + list flow проти API.

## TASK-080 — Mobile: Notifications screen
**Status:** done · **Agent:** mobile-developer · **Depends:** — · Updated: 2026-06-15
Сповіщення на мобільному:
- Bell icon у (app)/_layout.tsx header з badge кількості непрочитаних
- Екран /notifications: список (GET /api/notifications/history), тип іконкою (expiry/stock/system), read/unread стилі
- Tap → mark as read
Accept: tsc green; список підвантажується з API; badge оновлюється.

## TASK-081 — Mobile: Dashboard з реальними даними
**Status:** done · **Agent:** mobile-developer · **Depends:** — · Updated: 2026-06-15
Підключити index.tsx до реальних API:
- Картки Safe/Warning/Critical/Expired → GET /api/stock/summary
- Секція «AI замовлення» → GET /api/ai-orders (pending suggestions, count)
- Секція «Останні події» → GET /api/stock/events?limit=5 (або /api/activity-logs)
- Pull-to-refresh
Accept: tsc green; реальні числа замість заглушок; pull-to-refresh працює.

---

## v3.3 carry-over

## TASK-075 — Architect: Menu groups + Role matrix
**Status:** done · **Agent:** project-manager · **Depends:** — · Updated: 2026-06-14
Визначити логічні групи навігації та матрицю доступу ролей до меню.
Нова роль: Касир (cashier) — тільки /pos.
Уточнено: StoreManager → менеджмент магазину; NetworkManager → мережева картина.
Accept: задокументована матриця, TASK-076 + TASK-077 готові до виконання.

## TASK-076 — Backend: Cashier role + оновлені AppPolicies
**Status:** done · **Agent:** backend-developer · **Depends:** 075 · Updated: 2026-06-14
Додати роль `cashier` до AppRoles enum (C#), оновити AppPolicies:
- CanAccessPos: cashier + storekeeper + store_manager + network_manager + enterprise_admin
- CanManageStore: store_manager + network_manager + enterprise_admin (без cashier/storekeeper/merchandiser)
- CanViewNetworkAnalytics: network_manager + enterprise_admin
Оновити UserInviteDto/UserUpdateDto валідацію нових ролей.
Accept: dotnet build green; тести авторизації з cashier роллю проходять.

## TASK-077 — Frontend: Згрупований Sidebar + RBAC видимість
**Status:** done · **Agent:** frontend-developer · **Depends:** 075, 076 · Updated: 2026-06-14
Переробити Sidebar.tsx: групи зі стрілкою expand/collapse, роль-based видимість.

**Групи та доступ:**
1. Головна: Дашборд — TENANT_ROLES
2. Каса (expand): Каса (/pos), POS Аналітика — CAN_ACCESS_POS (cashier + managers)
3. Склад (expand): Каталог, Залишки, Прийомка, Переміщення, Списання — CAN_RECEIVE_STOCK + TENANT_ROLES
4. Продажі (expand): Продажі, Замовлення, AI Замовлення, Події — AT_LEAST_STORE_MANAGER
5. Аналітика (expand): Аналітика загальна, POS Аналітика — CAN_VIEW_ANALYTICS
6. Управління (expand): Персонал, План магазину, IoT пристрої — AT_LEAST_STORE_MANAGER
7. Адмін: Провайдер, Адмін — PROVIDER_ONLY
8. Налаштування — all

**Нові role sets у frontend/lib/roles.ts:**
- CAN_ACCESS_POS: cashier + CAN_RECEIVE_STOCK
- CAN_MANAGE_STORE: AT_LEAST_STORE_MANAGER (без cashier/storekeeper)
- CAN_VIEW_NETWORK: network_manager + enterprise_admin

**Правила видимості по ролях:**
- cashier: тільки Каса (група Каса), Налаштування
- storekeeper: Склад, Каса (без POS Аналітики), Налаштування
- merchandiser: Склад (Каталог + Залишки, без Прийомки/Переміщень), Налаштування
- store_manager: Каса, Склад, Продажі, Аналітика, Управління, Налаштування
- network_manager: Каса (POS Аналітика), Продажі, Аналітика, Управління, Налаштування
- enterprise_admin: все крім Provider/Admin
Accept: tsc + next build green; кожна роль бачить тільки свої групи; collapse/expand працює.

---

# Carry-over from v3.2 «ПРРО Каса» (started 2026-06-12)

Scope: v3-spec §3 + §6 Фаза 4. ADR-012: Checkbox (SaaS ПРРО) as fiscal provider behind
IFiscalService, offline-first (ADR-011 flow stays). Test cash register registered in
Checkbox cabinet (фіскальний номер TEST582378; license key + cashier creds in
.claude/private/access.md — blocker resolved 2026-06-12).

## TASK-066 — DB: pos_shifts, pos_transactions, pos_transaction_items
**Status:** done · **Agent:** database-engineer · **Depends:** — · Updated: 2026-06-12
v3-spec §5 + Status/'pending_fiscalization', OfflineNumber; RLS (TenantId direct);
FK product_stock SET NULL (яка партія списана). Accept: migration + RLS verified, build green.
Committed as 6d7a5082 «feat(pos): v3.2 POS schema».

## TASK-067 — Infrastructure: Checkbox fiscal client (IFiscalService)
**Status:** done · **Agent:** backend-developer · **Depends:** — · Updated: 2026-06-12
Done: IFiscalService + DTOs (Application/Features/Pos/Fiscal), CheckboxFiscalClient +
PrroOptions + token store (Infrastructure/Integrations/Prro), Noop fallback, DI switch,
unit tests 292/292 green. Live: license key valid on api.checkbox.in.ua
(⚠️ dev-api host from docs does NOT resolve — docs corrected). Cashier creds received →
**full live e2e GREEN** (CheckboxLiveE2ETests, gated by PRRO_LIVE_E2E=1): PIN signin →
shift CREATED→OPENED → sell receipt DONE (fiscal_code TEST-KcEsEF + tax_url) → Z-report
CLOSED, ~6s total. Added IFiscalService.GetShiftStatusAsync (shift opening is async —
needed for polling; TASK-068 must poll after open/close).
Log: 067_2026-06-12_checkbox-fiscal-client_backend-developer.md
ADR-012. Integrations/Prro: CheckboxFiscalClient implementing IFiscalService —
cashier signin (login/password or PIN → bearer token), shift open/close, sell receipt,
receipt status; DTOs; config binding PRRO__* (PROVIDER/BASEURL/LICENSEKEY/CASHIER__*,
secrets in .env only); error mapping + timeouts; unit tests with fake HTTP handler.
Accept: unit tests green (fake handler); live: dev-api.checkbox.in.ua reachability green
+ license-key flow as far as possible without cashier creds (blocker: cashier login/PIN
pending from user).

## TASK-068 — API: POS endpoints (shifts, sales → FEFO + stock_events)
**Status:** done · **Agent:** backend-developer · **Depends:** 066, 067 · Updated: 2026-06-13
⚠️ ADR-013: must resolve fiscalization through the per-tenant IFiscalServiceFactory
(TASK-071), not the startup-time IFiscalService DI registration.
POST /api/pos/shifts/open|close, POST /api/pos/sales (items by barcode; critical → auto
discount price, expired → 423 block per spec §3), GET /api/pos/shifts/current, sales list.
Sale = one DB tx: pos_transaction + items + FEFO write-down + stock_events('pos_sale');
fiscalization async (Status). Accept: service tests (FEFO, expired block, totals), build green.

## TASK-069 — Worker: fiscalization retry job
**Status:** done · **Agent:** backend-developer (worker) · **Depends:** 067, 068 · Updated: 2026-06-13
Cron */5 min: pending_fiscalization docs → submit/poll receipt status via Checkbox
(through API endpoint backed by IFiscalService); update FiscalNumber/Status on DONE.
Offline numbering handled by Checkbox itself (ADR-012). Accept: tsc green;
retry/backoff covered.

## TASK-071 — Settings: ПРРО провайдер (Checkbox) у Налаштування → Інтеграції
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** 067 · Updated: 2026-06-13
ADR-013. Per-tenant fiscal provider config, same mechanism as the Claude key
(integration_configs service='claude' → ClaudeOrderAdvisor.ResolveAsync; web UI
features/integrations + IntegrationsTab).
**Backend:** storage in integration_configs (service='prro', JSONB: provider
[checkbox|disabled, extensible], base_url [test/prod], license_key, cashier_login,
cashier_password, cashier_pin_code; RLS already on table — verify tenant isolation).
Endpoints: GET/PUT /api/settings/prro (GET masks secrets: ••••+last 4; PUT with
masked/unchanged secret keeps stored value — secrets are write-only),
POST /api/settings/prro/test (ping cash-registers/info via X-License-Key + cashier
signin, no shift side effects). Per-tenant IFiscalServiceFactory
(Infrastructure/Integrations/Prro): tenant DB config → PRRO__* env fallback →
NoopFiscalService; replaces startup DI switch; CheckboxTokenStore keyed per
tenant+license key. TASK-068/069 consume the factory.
**Frontend:** rework SERVICE_META.prro (features/integrations/types.ts — current
fields are stale placeholders) → provider select («Checkbox» / «вимкнено»),
credential form (license key, login/password or PIN, base URL test/prod toggle),
«Перевірити з'єднання» button calling /test, status badge (connected/error/disabled)
in IntegrationsTab card.
**Accept:** backend unit tests (resolution order DB→env→noop, masking, keep-on-masked
PUT, factory per-tenant); test endpoint green against live Checkbox test register;
cross-tenant isolation verified; tsc + next build green; full UI flow: select provider
→ enter creds → test → save → re-open shows masked secrets.

## TASK-070 — Mobile: POS screens (tablet) in Expo app
**Status:** done · **Agent:** mobile-developer · **Depends:** 068 · Updated: 2026-06-13
Зміна (open/close + PIN), продаж: скан штрихкоду (expo-camera) → кошик → ціна з акцією,
critical/expired badge, оплата cash/card (терминал SDK / принтер — Phase 4.1, поза скоупом),
чек зі статусом фіскалізації. Accept: tsc green; flow проти прод-API.

## TASK-072 — Web: POS dashboard (зміни, транзакції, Z-звіти)
**Status:** done · **Agent:** frontend-developer · **Depends:** 068 · Updated: 2026-06-14

## TASK-074 — SaaS Admin Panel: tenant onboarding + управління
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** — · Updated: 2026-06-15
Provider-only панель: список тенантів, створення (назва+slug+план+перший адмін),
статус active/inactive, зміна плану (basic/standard/enterprise/trial), модулі,
usage stats (users/stores/products/sales). Route /admin, policy ProviderOnly.
Backend: GET|POST /api/admin/tenants, GET|PATCH|POST /api/admin/tenants/{id}/...
Frontend: /admin сторінка з таблицею тенантів + create modal + detail drawer.
Accept: dotnet build+test green; tsc green; CRUD flow проти API.

## TASK-073 — POS Аналітика: API + Web дашборд
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** 068 · Updated: 2026-06-15
Нові ендпоінти GET /api/analytics/pos/* + веб-дашборд /analytics/pos.
Метрики: виручка за період, динаміка по днях, топ товарів, ефективність касирів,
середній чек, розбивка cash/card. Дані з pos_transactions + pos_transaction_items.
Accept: backend тести зелені; tsc + next build green; графіки відображають реальні дані.
Веб-інтерфейс для десктоп касира/менеджера — аналог TASK-070 (mobile) але для Next.js.
Route `/pos`. Функціонал: поточна зміна (відкрити/закрити + статус фіскалізації),
список продажів зміни (чек-деталі), Z-звіт після закриття, sidebar «Каса» (CanReceiveStock).
Використовує існуючі ендпоінти TASK-068:
  GET  /api/pos/shifts/current
  POST /api/pos/shifts/open  (body: { storeId, openingCash? })
  POST /api/pos/shifts/close
  GET  /api/pos/sales?shiftId=
Не включає: продаж через сканер (мобільна функція), оплата терміналом — Phase 4.1.
Accept: tsc + next build green; shift open/close/list-sales flow проти API.

---
# Previous sprint — v3.1 «IoT Foundation» (started 2026-06-12)

Scope: v3-spec §6 Фаза 1. ADR-010: MQTT ingestion in worker. pos_* tables → Phase 4.
**✅ COMPLETE 2026-06-12** — log: 061-065_2026-06-12_iot-foundation_multi-agent.md
Builds/tests green (backend 15/15 IoT tests, worker tsc, next build).
Live e2e PASSED on local stack: migration+RLS ✓, mosquitto pub/sub ✓,
temp alert → notification rows ✓, weight −490г → FEFO −2 units ✓.
2 bugs caught & fixed in e2e (jsonb config parsing; $6 type cast in notification log).
**DEPLOYED to production 2026-06-12** (93.127.143.98): mosquitto healthy (port 1884),
V3IotFoundation migration applied (auto on API start), RLS 6 policies verified,
worker «[mqtt] connected, subscribed to shelfguard/#», /iot and /floor-plan → 200.
Deploy bug fixed on the way: deploy.sh sourced unquoted .env → truncated DB
connection string overrode --env-file → API crash loop (fix: 95f5586d + quoted .env).

## TASK-061 — DB: IoT schema (iot_devices, temperature_readings, weight_readings)
**Status:** done · **Agent:** database-engineer · **Depends:** — · Updated: 2026-06-12
v3-spec §5: 3 tables + RLS (tenant via iot_devices.tenant_id; readings join device),
FKs to stores/store_zones, idx_temp_readings_device_time + device_id unique.
Accept: migration applies cleanly; RLS verified cross-tenant; dotnet build green.

## TASK-062 — DevOps: Mosquitto MQTT broker in docker-compose
**Status:** done · **Agent:** devops-engineer · **Depends:** — · Updated: 2026-06-12
Service `mosquitto` (eclipse-mosquitto:2), port 1883, allow_anonymous for dev,
persistent volume, MQTT_URL env wired to worker. Accept: `docker compose up` →
pub/sub smoke test on shelfguard/# passes.

## TASK-063 — API: iot_devices CRUD + readings endpoints
**Status:** done · **Agent:** backend-developer · **Depends:** 061 · Updated: 2026-06-12
GET/POST /api/iot/devices, GET/PUT/DELETE(soft) /api/iot/devices/:id,
GET /api/iot/devices/:id/readings (temp, paged), GET /api/iot/temperature?store_id=
(latest per device). Thin controllers, service in Application/Features/IoT.
Accept: tests for service rules (device_id unique per tenant, soft delete); build+tests green.

## TASK-064 — Worker: MQTT listener → readings + stock_events + temp alerts
**Status:** done · **Agent:** backend-developer (worker) · **Depends:** 061, 062 · Updated: 2026-06-12
Subscribe shelfguard/#; resolve device by device_id; update last_seen_at/battery.
temp payload → temperature_readings + threshold check (fridge >+8°C, freezer >-12°C
from device config) → is_alert + notification queue (critical → manager/director).
weight payload → weight_readings + confidence calc (95/85/60, <70 = log only) →
stock_events (type sensor) + FEFO write-down for confident deltas.
Offline cron: last_seen_at > 30 min → alert. Accept: tsc green; unit-testable pure
funcs for confidence/thresholds; e2e via mosquitto_pub on local stack.

## TASK-065 — Web: IoT devices dashboard (/iot)
**Status:** done · **Agent:** frontend-developer · **Depends:** 063 (+064 for live data) · Updated: 2026-06-12
Devices table: type icon, zone, online/offline (last_seen_at), battery, firmware;
register/edit/deactivate dialogs; temperature tab: recharts line per device,
alert badges. Sidebar «IoT пристрої» (AT_LEAST_STORE_MANAGER).
Accept: tsc + next build green; CRUD flow works against API.

---
# Previous sprint — v2.5 «AI Agent» ✅ COMPLETE (2026-06-12) — v2 DONE

## TASK-060 — Web: AI orders dashboard ✅ done (2026-06-12)
Log: `.claude/logs/tasks/060_2026-06-12_ai-orders-dashboard_frontend-developer.md`
/ai-orders per spec §7 mockup: base/AI/final + reasoning, inline edit, accept/reject.
Claude key manageable via Налаштування → Інтеграції. Live e2e pending Anthropic credits.

## TASK-058 + TASK-059 — Claude advisor + AI orders API + daily job ✅ done (2026-06-11)
Log: `.claude/logs/tasks/058-059_2026-06-11_ai-order-agent_backend-developer.md`
ClaudeOrderAdvisor (Infrastructure/AI, official SDK, structured outputs), 6 endpoints,
worker cron 05:00 + Telegram notify. Awaiting CLAUDE_API_KEY for live e2e.

---
# Previous sprint — v2.4 «Cannibalization» ✅ COMPLETE (2026-06-11)

## TASK-057 — Promo cannibalization ✅ done (2026-06-11)
Log: `.claude/logs/tasks/057_2026-06-11_cannibalization_backend-developer.md`
Auto-suggestions (promo ×2.0, siblings ×0.7), apply flow, promo coefficient in formula.
E2e: Вода k_event 2.0 × k_promo 2.0 → ORDER 304. Next: v2.5 AI Agent (TASK-058..060).

---
# Previous sprint — v2.3 «Events & Weather» ✅ COMPLETE (2026-06-11)

## TASK-056 — Web: events calendar ✅ done (2026-06-11)
Log: `.claude/logs/tasks/056_2026-06-11_events-calendar_frontend-developer.md`
/events: month grid, recurring projection, CRUD + coefficient editor, seed button. 200 OK.
Next: v2.4 Cannibalization (TASK-057) → v2.5 AI Agent (TASK-058..060).

## TASK-054 — Demand events calendar ✅ done (2026-06-11)
Log: `.claude/logs/tasks/054_2026-06-11_demand-events_backend-developer.md`
4 tables + RLS, full CRUD, 5 seeded holidays, event coefficient wired into order
formula (most-specific scope wins, events multiply). E2e: Вода ×2 → ORDER 152.

## TASK-055 — Open-Meteo integration ✅ done (2026-06-11)
Log: `.claude/logs/tasks/055_2026-06-11_open-meteo-weather_backend-developer.md`
Client + 6 endpoints + worker cron 06:00 + weather coefficient in formula.
E2e on real Kyiv forecast: k_event 2.0 × k_weather 1.5 → ORDER 228.

---
# Previous sprint — v2.2 «Buffer & Formula» ✅ COMPLETE (2026-06-11)

## TASK-053 — Web: orders page + buffer funnel ✅ done (2026-06-11)
Log: `.claude/logs/tasks/053_2026-06-11_orders-page-buffer-funnel_frontend-developer.md`
/orders: one-click chain ADU→buffers→order, funnel viz, MOQ/USQ tags. Deployed, 200 OK.
Next sprint: v2.3 «Events & Weather» (TASK-054..056).

## TASK-051 — CDA buffer engine ✅ done (2026-06-11)
Log: `.claude/logs/tasks/051_2026-06-11_cda-buffer-engine_backend-developer.md`
product_buffer table + RLS, pure CdaBufferCalculator (9 tests), GET/recalculate endpoints.
Verified on production: Total 51.97 = G 36.03 + Y 5.02 + R 10.92 (hand-checked).

## TASK-052 — Order formula ✅ done (2026-06-11)
Log: `.claude/logs/tasks/052_2026-06-11_order-formula_backend-developer.md`
POST /api/orders/calculate. Full chain verified on production:
Вода Моршинська 51.97+24−0−0 → ORDER 76. Tests 9/9.

---
# Previous sprint — v2.1 «Data Foundation» ✅ COMPLETE (2026-06-11)

## TASK-046 — v2 schema: daily_sales, product_adu, supply_schedules ✅ done (2026-06-11)
Log: `.claude/logs/tasks/046_2026-06-11_v2-data-foundation-schema_database-engineer.md`
Migration V2DataFoundation applied to production. RLS verified (6 policies).

## TASK-047 — Daily Sales API ✅ done (2026-06-11)
Log: `.claude/logs/tasks/047_2026-06-11_daily-sales-api_backend-developer.md`
GET/POST /daily-sales (upsert), POST /import (CSV by barcode), PUT /:id/mark-anomaly.
Verified on production. Tests 5/5.

## TASK-048 — ADU calculation engine ✅ done (2026-06-11)
Log: `.claude/logs/tasks/048_2026-06-11_adu-engine_backend-developer.md`
Pure AduCalculator (9 unit tests) + eligibility query + upsert. Verified on production:
recalculate → 2 products with adu_effective 10.9167 (group 3, 30 valid days).

## TASK-049 — Supply schedules CRUD ✅ done (2026-06-11)
Log: `.claude/logs/tasks/049_2026-06-11_supply-schedules-crud_backend-developer.md`
Full CRUD + one-active-per-pair rule (409), ISO day validation, soft delete.
Verified on production (6/6 e2e checks). Tests 11/11.

## TASK-050 — Web: sales entry page ✅ done (2026-06-11)
Log: `.claude/logs/tasks/050_2026-06-11_sales-entry-page_frontend-developer.md`
/sales: filters + manual entry form + CSV import dialog + anomaly toggle. Deployed, 200 OK.

---
# v1 maintenance (parallel)
TASK-045 (mobile profile+receipt wiring) · TASK-034 (auth tests) · TASK-035 (bin/obj)
TASK-038 (impersonation verify) · TASK-039 (bot /start) — see backlog.md

---
# Done

## TASK-033 — Notifications e2e ✅ done (2026-06-11)
Log: `.claude/logs/tasks/033_2026-06-11_notifications-e2e_devops-engineer.md`
Fixed 5 pipeline breaks (pg URL format, PascalCase SQL, Redis collision with another
project, DATE→NaN statuses, duplicate scheduler). Verified live: statuses recompute
hourly, 23 notifications queued. Delivery needs TELEGRAM_BOT_TOKEN / RESEND_API_KEY (user).


## TASK-018 — Mobile App Scaffolding ✅ done (2026-06-07)
Log: `.claude/logs/tasks/018_2026-06-07_mobile-scaffolding_mobile-developer.md`

## TASK-025 — DB Fix: RLS + FK Constraints ✅ done (2026-06-04)
Log: `.claude/logs/tasks/025_2026-06-04_fix-rls-fk_database-engineer.md`

## TASK-019 — Analytics API ✅ done (2026-06-04)
Log: `.claude/logs/tasks/019_2026-06-04_analytics_backend-developer.md`


## TASK-016 — Write-offs ✅ done (2026-06-04)
Log: `.claude/logs/tasks/016_2026-06-04_write-offs_backend-developer.md`

## TASK-015 — Stock Transfers ✅ done (2026-06-04)
Log: `.claude/logs/tasks/015_2026-06-04_transfers_backend-developer.md`

## TASK-014 — Stock Receipts ✅ done (2026-06-04)
Log: `.claude/logs/tasks/014_2026-06-04_receipts_backend-developer.md`

## TASK-013 — Suppliers CRUD ✅ done (2026-06-04)
Log: `.claude/logs/tasks/013_2026-06-04_suppliers-crud_backend-developer.md`

## TASK-012 — Stores/Zones CRUD ✅ done (2026-06-04)
Log: `.claude/logs/tasks/012_2026-06-04_stores-zones_backend-developer.md`

## TASK-007 — ProductStock API + FEFO ✅ done (2026-06-04)
Log: `.claude/logs/tasks/007_2026-06-04_product-stock-api_backend-developer.md`

## TASK-006 — Products API ✅ done (2026-06-04)
Log: `.claude/logs/tasks/006_2026-06-04_products-api_backend-developer.md`

## TASK-002 — Full DB Schema ✅ done (2026-06-04)
Log: `.claude/logs/tasks/002_2026-06-04_full-db-schema_database-engineer.md`

## TASK-010 — Web dashboard ✅ done (2026-06-03)
Log: `.claude/logs/tasks/010_2026-06-03_web-dashboard_frontend-developer.md`

---

## TASK-027..031 — Frontend Pages ✅ done (2026-06-04)
Log: `.claude/logs/tasks/027_2026-06-04_frontend-pages_frontend-developer.md`
Pages: /stock, /receipts, /receipts/:id, /transfers, /write-offs, /analytics

---

## TASK-011b — Web products page (/inventory) ✅ done (2026-06-10)
Log: `.claude/logs/tasks/011b_2026-06-10_products-page_frontend-developer.md`
Route: /inventory — Catalog CRUD (list + create + edit + delete + detail drawer)

---

## TASK-024 — Notifications Settings API ✅ done (2026-06-10)
Log: `.claude/logs/tasks/024_2026-06-10_notifications-api_backend-developer.md`
Endpoints: GET /notifications/settings, PUT /notifications/settings, GET /notifications/history, POST /notifications/test

---

## TASK-023 — Users API (HR module) ✅ done (2026-06-10)
Log: `.claude/logs/tasks/023_2026-06-10_users-api_backend-developer.md`
Endpoints: GET /users, GET /users/:id, POST /users/invite, PUT /users/:id, PUT /users/:id/permissions, DELETE /users/:id, GET /users/:id/activity

---

## TASK-022 — Discounts API ✅ done (2026-06-10)
Log: `.claude/logs/tasks/022_2026-06-10_discounts-api_backend-developer.md`
Endpoints: GET /discounts, GET /discounts/:id, POST /discounts, PUT /discounts/:id/approve, PUT /discounts/:id/cancel

---

## BUG-004 — Inconsistent 404 error format ✅ fixed (2026-06-11)
Log: `.claude/logs/tasks/bug004_2026-06-11_error-format-standardization_backend-developer.md`
Central fix: custom IClientErrorFactory + InvalidModelStateResponseFactory in ShelfGuard.Api.
All error bodies now follow `{error: "..."}`. Verified on production. All 4 smoke-test bugs closed.

---

## BUG-003 — GET /api/analytics/summary ✅ closed: not a bug (2026-06-11)
Log: `.claude/logs/reviews/bug003-resolution_2026-06-11.md`
Route never existed — smoke test probed a guessed name. Real endpoint is
`/api/analytics/expiry-summary`; all 6 analytics routes verified 200 on production.
Stale `/api/analytics/dashboard` row in api-contracts.md corrected.

---

## BUG-002 — GET /api/stock/summary ✅ fixed (2026-06-11)
Log: `.claude/logs/tasks/bug002_2026-06-11_stock-summary-endpoint_backend-developer.md`
Response: `{safe, warning, critical, expired, needsVerification, total}`. Optional `?store_id` filter.
Verified on production: 25 total batches (11 safe / 7 warning / 5 critical / 2 expired).

---

## BUG-001 — RLS Tenant Leakage ✅ fixed (2026-06-10)
Log: `.claude/logs/tasks/bug001_2026-06-10_rls-tenant-leakage_security-reviewer.md`
Fix: `TenantConnectionInterceptor.BuildSetSql()` now always SETs `app.tenant_id`.
Provider users get null UUID → RLS returns `[]` instead of leaking tenant data.
Tests: 13/13 pass.

---

## Next candidates

- **TASK-007** — ProductStock (batches) API + FEFO logic — **найвищий пріоритет**, блокує dashboard реальні дані
- **TASK-011** — `/api/stock` backend endpoint + `/stock` frontend page
  - Requires: product_stock table ✅, catalog_products ✅
  - Blocks: real dashboard stats (Safe/Warning/Critical/Expired from actual batches)

- **TASK-012** — Extend DbSeeder with store, zones, catalog_products, stock batches
  - Makes dashboard show real FEFO data instead of POC products proxy

- **TASK-003b** — Migrate catalog API from POC `Products` → `catalog_products`
  - Low priority until stock API is built
