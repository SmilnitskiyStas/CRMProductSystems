# Backlog

Tasks waiting to be picked up. Ordered by priority.
Rewritten 2026-06-15: v4 platform transformation decomposition added (TASK-200+).

---

## v4 — Business Platform Transformation

**ADR:** ADR-014 (entity rename), ADR-015 (module activation)
**Goal:** Retail Inventory System → Multi-Industry Business Operations Platform
**Migration phases:** 1 Entity Rename → 2 Module Activation → 3 New Nav → 4 Supplier Marketplace → 5 Auto Service → 6 Production → 7 AI Assistant

---

### Phase 1 — Foundation: Entity Rename (блокує всі подальші фази)

#### TASK-200 — DB: stores → locations + location_type
**Status:** done · **Agent:** database-engineer · **Priority:** critical · **Updated:** 2026-06-15
EF Core migration:
- Rename table `stores` → `locations`; rename `store_zones` → `location_zones`
- Add PostgreSQL enum `location_type`: `retail_store | warehouse | auto_service | office | production | restaurant` (default `retail_store`)
- Add column `location_type` до `locations`
- Rename `store_id` FK-колонки → `location_id` у всіх залежних таблицях: `product_stock`, `stock_transfers`, `stock_receipts`, `write_offs`, `discounts`, `iot_devices`, `notification_settings`, `pos_shifts`, `pos_transactions`, `daily_sales`
- Оновити всі RLS policies (посилання на `store_id` → `location_id`)
- Add `business_type` enum до `tenants`: `retail | auto_service | warehouse | restaurant | production | distribution` (default `retail`)
Accept: migration applies cleanly; dotnet build green; RLS verified cross-tenant.

#### TASK-201 — Backend: Store → Location entity + API rename
**Status:** done · **Agent:** backend-developer · **Depends:** TASK-200 · **Priority:** critical · **Updated:** 2026-06-15
- Rename Domain entity `Store` → `Location`; add `LocationType` enum
- Rename `StoreZone` → `LocationZone`
- Видалити POC `Product` entity + `Products` table (ADR-006 нарешті!)
- Update всі repositories, services, DTOs (Store→Location у назвах)
- Update API controllers: `StoresController` → `LocationsController`
- Routes: `/api/stores` → `/api/locations`; тимчасові 301-redirect зі старих маршрутів
- Add `BusinessType` до Tenant entity + Tenant DTO
Accept: dotnet build green; тести оновлені; /api/locations → 200; legacy /api/stores → 301.

#### TASK-202 — Frontend: stores → locations
**Status:** done · **Agent:** frontend-developer · **Depends:** TASK-201 · **Priority:** high · **Updated:** 2026-06-16
- Update всі API calls (stores → locations)
- Rename UI labels: «Магазин» → «Локація», «Тип» selector з `location_type` values
- Форми create/edit: додати вибір типу локації
- Update `features/stores/` → `features/locations/` (або alias)
Accept: tsc + next build green; CRUD локацій працює.

#### TASK-203 — Mobile: stores → locations
**Status:** done · **Agent:** mobile-developer · **Depends:** TASK-201 · **Priority:** medium · **Updated:** 2026-06-16
- Update API client (всі store endpoints → location)
- Update UI references (labels, selectors)
Accept: tsc green; мобільний додаток компілюється і підключається до нового API.

#### TASK-204 — DB: catalog_products → items + item_type
**Status:** done · **Agent:** database-engineer · **Depends:** TASK-200 · **Priority:** critical · **Updated:** 2026-06-16
EF Core migration:
- Rename table `catalog_products` → `items`
- Add PostgreSQL enum `item_type`: `product | service | spare_part | consumable | raw_material | kit` (default `product`)
- Add column `item_type` до `items`
- Drop legacy `Products` table (POC; залежить від TASK-201)
- Rename FK-посилання на `catalog_products` → `items` у: `product_stock`, `product_supplier_settings`, `write_off_items`, `stock_transfer_items`, `stock_receipt_items`, `pos_transaction_items`, `daily_sales`, `product_buffers`
- Оновити RLS policies
Accept: migration green; dotnet build green.

#### TASK-205 — Backend: CatalogProduct → Item entity + API rename
**Status:** done · **Agent:** backend-developer · **Depends:** TASK-204 · **Priority:** critical · **Updated:** 2026-06-16
- Rename `CatalogProduct` → `Item`; add `ItemType` enum
- Update services/repositories/DTOs (Catalog → Items naming)
- Routes: `/api/catalog` → `/api/items`; тимчасові 301-redirect
- Item тепер підтримує всі типи (product, service, spare_part…)
Accept: dotnet build green; /api/items → 200; FEFO через item_id стабільне.

#### TASK-206 — Frontend: catalog/products → items
**Status:** planned · **Agent:** frontend-developer · **Depends:** TASK-205 · **Priority:** high
- Update API calls (catalog → items)
- Rename UI: «Каталог продуктів» → «Каталог»; додати колонку «Тип товару»
- Форма create/edit: selector `item_type`
Accept: tsc + next build green.

#### TASK-207 — Mobile: products → items
**Status:** planned · **Agent:** mobile-developer · **Depends:** TASK-205 · **Priority:** medium
- Update API client
- Update сканування штрихкодів (barcode → item lookup)
Accept: tsc green.

#### TASK-208 — Backend: Tenant business_type + Module activation API
**Status:** planned · **Agent:** backend-developer · **Depends:** TASK-200 · **Priority:** high
ADR-015 Module activation pattern:
- `[RequireModule("key")]` attribute + IAsyncActionFilter
- `GET /api/settings/modules` (enterprise_admin — власний тенант)
- `GET/PATCH /api/admin/tenants/{id}/modules` (ProviderOnly)
- Default modules при створенні тенанта залежать від `business_type`:
  - retail → `{inventory, procurement, pos}`
  - auto_service → `{auto_service, procurement}`
  - restaurant → `{inventory, pos, production}`
  - warehouse → `{inventory, procurement}`
Accept: dotnet build green; тест: [RequireModule] → 403 якщо модуль вимкнений.

#### TASK-209 — Frontend: Module activation settings UI
**Status:** planned · **Agent:** frontend-developer · **Depends:** TASK-208 · **Priority:** medium
- Нова вкладка «Модулі» у Налаштуваннях (enterprise_admin)
- Toggle-список активних модулів з описами
- `useModules()` hook
- Sidebar-групи ховаються якщо модуль вимкнений (комбінація роль + модуль)
Accept: tsc green; toggle → зміна видимості sidebar-груп.

---

### Phase 2 — New Navigation & Menu Structure

#### TASK-210 — Frontend: New v4 menu structure
**Status:** planned · **Agent:** frontend-developer · **Depends:** TASK-209 · **Priority:** medium
Переробити sidebar відповідно до v4-spec:
- **Dashboard** — всі ролі
- **Operations** (expand): Каталог, Залишки, Переміщення, Списання — модуль `inventory`
- **Sales** (expand): Каса /pos, Замовлення, Клієнти — модуль `pos`
- **Procurement** (expand): Постачальники, Замовлення постачання, AI Procurement — модуль `procurement`
- **Analytics** (expand): Продажі, Залишки, Фінанси, Прогнозування
- **Workforce** (expand): Персонал, Графіки, Ролі — AT_LEAST_STORE_MANAGER
- **Marketplace** — модуль `marketplace`
- **Service Desk** (expand): Тікети, Запити
- **Settings** — всі
Accept: tsc + next build green; кожна роль бачить правильні розділи.

---

### Phase 3 — Supplier Marketplace

#### TASK-220 — DB: Supplier Marketplace schema
**Status:** planned · **Agent:** database-engineer · **Depends:** Phase 1 · **Priority:** medium
Нові таблиці:
- `supplier_profiles` (розширений профіль: region, categories JSONB, website, delivery_regions JSONB, working_hours, payment_terms, is_public, plan `free|premium`)
- `supplier_items` (каталог постачальника: supplier_id, item_id або custom_name, price, min_qty, unit, is_available)
- `supplier_metrics` (avg_delivery_days, order_accuracy, quality_score, rating, cancellation_rate, response_time_hours — оновлюється background job)
- `supplier_reviews` (tenant_id, supplier_id, rating 1-5, comment, created_at — RLS)
Accept: migration green; RLS на supplier_reviews.

#### TASK-221 — Backend: Supplier Marketplace API
**Status:** planned · **Agent:** backend-developer · **Depends:** TASK-220 · **Priority:** medium
- `GET /api/marketplace/suppliers` — публічний listing (без auth) з пагінацією + фільтрами (region, category, plan)
- `GET /api/marketplace/suppliers/{id}` — профіль (premium поля тільки при plan=premium або auth)
- `GET /api/marketplace/suppliers/{id}/items` — каталог постачальника
- `POST /api/marketplace/suppliers/{id}/reviews` — залишити оцінку (auth, tenant)
- `GET/PUT /api/settings/supplier-profile` — постачальник керує своїм профілем
- `POST /api/marketplace/search` — пошук за item_name + region
Модуль: `[RequireModule("marketplace")]`
Accept: dotnet build green; публічний listing → 200 без auth.

#### TASK-222 — Frontend: Supplier Marketplace UI
**Status:** planned · **Agent:** frontend-developer · **Depends:** TASK-221 · **Priority:** medium
- `/marketplace` — grid постачальників, фільтри (регіон, категорія), search
- `/marketplace/{id}` — профіль постачальника: метрики, каталог, відгуки
- Premium badge + gating для premium-полів
- `features/marketplace/` folder
Accept: tsc + next build green.

#### TASK-223 — Backend: AI Supplier Recommendation
**Status:** planned · **Agent:** backend-developer · **Depends:** TASK-221 · **Priority:** low
- `POST /api/marketplace/ai-recommend` — Claude API: передати список постачальників + item_name → отримати рекомендацію з поясненням
- Ізольовано в `Infrastructure/AI/SupplierAdvisor`
Accept: dotnet build green; Claude API відповідає рекомендацією.

---

### Phase 4 — Auto Service Module

#### TASK-230 — DB: Auto Service schema
**Status:** planned · **Agent:** database-engineer · **Depends:** Phase 1 · **Priority:** low
Нові таблиці (всі мають `tenant_id` + RLS):
- `as_customers` (name, phone, email, notes)
- `as_vehicles` (customer_id, brand, model, year, vin, license_plate, mileage, notes)
- `as_service_catalog` (name, description, item_id FK → items service type, default_price, duration_hours)
- `as_work_orders` (vehicle_id, mechanic_user_id, status: `new|in_progress|waiting_parts|done|invoiced`, created_at, completed_at, notes)
- `as_work_order_lines` (work_order_id, type: `service|part`, service_catalog_id nullable, item_id nullable, qty, price, discount)
Accept: migration green; RLS cross-tenant verified.

#### TASK-231 — Backend: Auto Service Module API
**Status:** planned · **Agent:** backend-developer · **Depends:** TASK-230 · **Priority:** low
- CRUD: customers, vehicles, service catalog, work orders
- `POST /api/auto-service/work-orders/{id}/complete` — списує parts через FEFO (item_type=spare_part)
- `GET /api/auto-service/work-orders` — kanban view (by status)
- Модуль: `[RequireModule("auto_service")]`
Accept: dotnet build green; FEFO write-down на spare_part при complete.

#### TASK-232 — Frontend: Auto Service Module
**Status:** planned · **Agent:** frontend-developer · **Depends:** TASK-231 · **Priority:** low
- `/auto-service` — kanban board (New / In Progress / Waiting Parts / Done)
- Клієнти та автомобілі
- Форма Work Order (клієнт + авто + послуги + запчастини)
- `features/auto-service/`
Accept: tsc + next build green.

#### TASK-233 — Mobile: Auto Service screens
**Status:** planned · **Agent:** mobile-developer · **Depends:** TASK-231 · **Priority:** low
- Work Order list + detail
- Сканування VIN або штрихкод запчастини
Accept: tsc green.

---

### Phase 5 — Production Module

#### TASK-240 — DB: Production schema
**Status:** planned · **Agent:** database-engineer · **Depends:** Phase 1 · **Priority:** low
Нові таблиці:
- `recipes` (name, output_item_id, output_qty, unit, notes, tenant_id + RLS)
- `recipe_ingredients` (recipe_id, item_id, qty, unit)
- `production_orders` (recipe_id, location_id, planned_qty, status: `planned|in_progress|done|cancelled`, started_at, completed_at, created_by, tenant_id + RLS)
- `production_order_consumptions` (production_order_id, item_id, product_stock_id, qty_consumed, consumed_at)
Accept: migration green; RLS verified.

#### TASK-241 — Backend: Production Module API
**Status:** planned · **Agent:** backend-developer · **Depends:** TASK-240 · **Priority:** low
- CRUD: recipes, recipe ingredients
- `POST /api/production/orders` — запустити виробничий ордер
- `POST /api/production/orders/{id}/complete` — списати інгредієнти через FEFO + додати готовий продукт у stock
- `GET /api/production/orders` — список ордерів
- Модуль: `[RequireModule("production")]`
Accept: dotnet build green; FEFO списання raw_material при complete; finished product створюється як новий batch.

#### TASK-242 — Frontend: Production Module
**Status:** planned · **Agent:** frontend-developer · **Depends:** TASK-241 · **Priority:** low
- `/production/recipes` — список рецептів + CRUD
- `/production/orders` — список ордерів, запуск, завершення
- `features/production/`
Accept: tsc + next build green.

---

### Phase 6 — AI Business Assistant

#### TASK-250 — AI Business Assistant (cross-module)
**Status:** planned · **Agent:** backend-developer · **Depends:** Phase 3-5 · **Priority:** low
- `POST /api/ai/assistant` — природна мова → агрегація даних з кількох модулів → Claude API → рекомендація
- Контекст: поточні залишки + замовлення + продажі + постачальники
- Isolated в `Infrastructure/AI/BusinessAssistant`
- Frontend: chat widget на Dashboard
Accept: Claude API відповідає з контекстом бізнесу; build green.

---

## v1 — remaining (none block production demo)

(empty — TASK-040..042 done 2026-06-12)

---

## v3 — CV Camera (Computer Vision) — OUT OF SCOPE
**Status:** frozen · Updated: 2026-06-15
IoT Camera / CV Camera (v3-spec §2) заморожено — немає доступу до IP-камер для розробки і тестування.
Розморозити коли з'явиться обладнання.

## Infrastructure polish (Phase 7) — deferred by user 2026-06-12

## TASK-043: Domain + HTTPS (Let's Encrypt) + drop cleartext from mobile
**Status:** done · **Priority:** high before real clients · Updated: 2026-06-15
SSL/TLS повністю налаштовано: nginx на agrusystems.pp.ua + api.agrusystems.pp.ua, TLS 1.2/1.3,
HSTS 1 рік, HTTP→HTTPS redirect. Certbot Let's Encrypt auto-renew (cron 03:30 UTC).
Mobile: usesCleartextTraffic=false, EAS env EXPO_PUBLIC_API_URL=https://api.agrusystems.pp.ua.

## TASK-044: CI (GitHub Actions: build + test on PR), DB backups
**Status:** done · **Priority:** high · **Agent:** devops-engineer · Updated: 2026-06-15

---

## Done (recent)
- TASK-042 per-channel notification statuses ✅ (2026-06-12) — log: 042_2026-06-12_notification-per-channel-status_backend-developer.md
- TASK-041 floor-plan constructor ✅ (2026-06-12) — log: 041_2026-06-12_floor-plan-constructor_frontend-developer.md; QA e2e pending
- TASK-040 weekly-report + cleanup jobs ✅ (2026-06-12) — log: 040_2026-06-12_weekly-report-cleanup-jobs_backend-developer.md
- TASK-035 bin/obj untracked ✅ (2026-06-12) — 473 files, git status clean after builds
- TASK-034 auth tests fixed ✅ (2026-06-12) — suite 249/249 green
- TASK-039 Telegram /start linking ✅ (2026-06-12) — deep-link codes + worker listener
- TASK-038 impersonation e2e ✅ PASS 12/12 (2026-06-12)
- TASK-032 device smoke ✅ (2026-06-11) · TASK-045 mobile polish ✅ (2026-06-12)
- v2 complete: TASK-046..060 ✅ — logs in .claude/logs/tasks/
- Pending external: Anthropic credits for live AI e2e; Resend key for email channel.

## Process note (for all agents)
NEVER edit markdown/source files via PowerShell Get-Content/-replace/Set-Content —
it mojibakes UTF-8 (happened 3×). Use the Write/Edit tools.
