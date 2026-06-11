# Current Sprint — v2.5 «AI Agent» ✅ COMPLETE (2026-06-12) — v2 DONE

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
