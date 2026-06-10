# Current Sprint

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
