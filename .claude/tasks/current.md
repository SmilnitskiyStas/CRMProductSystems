# Current Sprint — v3.4 «Mobile Complete» (started 2026-06-15) + v3.3 carry-over

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
