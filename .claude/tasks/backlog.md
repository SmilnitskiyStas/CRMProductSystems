# Backlog

Tasks waiting to be picked up. Ordered by priority.

---

## TASK-001: Rename projects from CRM.* to ShelfGuard.*
**Status:** planned
**Priority:** high
**Agent:** backend-developer
**Dependencies:** none
**Notes:** Current test projects use CRM.* naming. Need ShelfGuard.* for real implementation.

---

## TASK-002: Implement full v1 database schema
**Status:** planned
**Priority:** critical
**Agent:** database-engineer
**Dependencies:** TASK-001
**Notes:** Full SQL schema from v1-spec.md section 4.2. All tables + RLS policies + indexes.

---

## TASK-003: Implement JWT authentication
**Status:** planned
**Priority:** critical
**Agent:** backend-developer
**Dependencies:** TASK-002
**Notes:** POST /auth/login, POST /auth/refresh, GET /auth/me. JWT with tenantId + role. HttpOnly cookie for refresh token.

---

## TASK-004: Implement TenantInterceptor (RLS middleware)
**Status:** planned
**Priority:** critical
**Agent:** backend-developer
**Dependencies:** TASK-003
**Notes:** Middleware reads JWT, sets app.tenant_id PostgreSQL session variable for RLS.

---

## TASK-005: Implement RoleGuard
**Status:** planned
**Priority:** critical
**Agent:** backend-developer
**Dependencies:** TASK-003
**Notes:** Role-based authorization from v1-spec.md section 3.2 matrix.

---

## TASK-006: Products API (real schema)
**Status:** planned
**Priority:** high
**Agent:** backend-developer
**Dependencies:** TASK-002, TASK-004
**Notes:** Full products CRUD with barcode, categories, segments, supplier settings (MOQ/USQ). See v1-spec.md /products endpoints.

---

## TASK-007: ProductStock (batches) API
**Status:** planned
**Priority:** high
**Agent:** backend-developer
**Dependencies:** TASK-006
**Notes:** FEFO logic, batch CRUD, expiry statuses, suggestions. See v1-spec.md /stock endpoints.

---

## TASK-008: Expiry status cron job (BullMQ)
**Status:** planned
**Priority:** high
**Agent:** backend-developer + devops-engineer
**Dependencies:** TASK-007
**Notes:** expiry-check.job — hourly, updates batch statuses, sends notifications queue.

---

## TASK-009: Web auth pages
**Status:** planned
**Priority:** high
**Agent:** frontend-developer
**Dependencies:** TASK-003
**Notes:** Login page, JWT storage, auth redirect.

---

## TASK-010: Web dashboard (store overview)
**Status:** planned
**Priority:** high
**Agent:** frontend-developer
**Dependencies:** TASK-007, TASK-009
**Notes:** 4 metric cards (safe/warning/critical/expired) + attention table + quick actions.

---

## TASK-011: Web stock page (/stock)
**Status:** planned
**Priority:** medium
**Agent:** frontend-developer
**Dependencies:** TASK-007, TASK-009
**Notes:** Dense table with filters, multi-select, batch actions.

---

## TASK-012: Stores + Zones CRUD
**Status:** planned
**Priority:** medium
**Agent:** backend-developer
**Dependencies:** TASK-002

---

## TASK-013: Suppliers CRUD
**Status:** planned
**Priority:** medium
**Agent:** backend-developer
**Dependencies:** TASK-002

---

## TASK-014: Stock receipts (прийомка)
**Status:** planned
**Priority:** medium
**Agent:** backend-developer
**Dependencies:** TASK-007, TASK-013

---

## TASK-015: Stock transfers
**Status:** planned
**Priority:** medium
**Agent:** backend-developer
**Dependencies:** TASK-007, TASK-012

---

## TASK-016: Write-offs
**Status:** planned
**Priority:** medium
**Agent:** backend-developer
**Dependencies:** TASK-007

---

## TASK-017: Notifications (Telegram + Push + Email)
**Status:** planned
**Priority:** medium
**Agent:** backend-developer + devops-engineer
**Dependencies:** TASK-008
**Notes:** BullMQ notification.job, Telegraf.js bot, Resend email.

---

## TASK-018: Expo mobile app scaffolding
**Status:** planned
**Priority:** medium
**Agent:** frontend-developer
**Dependencies:** TASK-003
**Notes:** Expo SDK 56, Expo Router, NativeWind v4, auth flow, bottom tabs.

---

## TASK-019: Analytics API
**Status:** planned
**Priority:** low
**Agent:** backend-developer
**Dependencies:** TASK-007, TASK-016

---

## TASK-020: Super Admin provider panel
**Status:** planned
**Priority:** low
**Agent:** backend-developer + frontend-developer
**Dependencies:** TASK-003, TASK-005

---

## TASK-021: Movements API (GET /movements)
**Status:** planned
**Priority:** high
**Agent:** backend-developer
**Dependencies:** TASK-007
**Notes:** Read-only endpoint. Filter stock_movements by product_id, store_id, type, from, to (DateOnly). Needs IMovementRepository + MovementsController. Required for Analytics frontend + audit log.

---

## TASK-022: Discounts API (GET|POST /discounts, approve, cancel)
**Status:** planned
**Priority:** medium
**Agent:** backend-developer
**Dependencies:** TASK-007
**Notes:** discounts table exists, no service/controller. Endpoints: GET /discounts (?store_id, ?status), POST /discounts, PUT /discounts/:id/approve, PUT /discounts/:id/cancel. Webhook to POS on approve (placeholder for v1).

---

## TASK-023: Users API (HR module)
**Status:** planned
**Priority:** high
**Agent:** backend-developer
**Dependencies:** TASK-003, TASK-005
**Notes:** GET /users (store_manager sees only own store), POST /users/invite, GET /users/:id, PUT /users/:id, DELETE /users/:id (soft, is_active=false), GET /users/:id/activity. Required for HR/Settings frontend page.

---

## TASK-024: Notifications Settings API
**Status:** planned
**Priority:** medium
**Agent:** backend-developer
**Dependencies:** TASK-003
**Notes:** GET /notifications/settings, PUT /notifications/settings, GET /notifications/history, POST /notifications/test. Uses notification_settings + notification_queue tables already in schema.

---

## TASK-025: Fix DB — notification_settings RLS + stock_movements FK
**Status:** planned
**Priority:** high
**Agent:** database-engineer
**Dependencies:** none (additive migration)
**Notes:**
1. Add RLS to notification_settings (via users.TenantId join — user has no direct TenantId on this table, need sub-select through users)
2. Add FK constraints on stock_movements: product_id → catalog_products, from_store_id/to_store_id → stores
3. Also add FK on write_offs: store_id → stores (currently missing)

---

## TASK-026: Seeder — v1 catalog/stock/stores test data
**Status:** planned
**Priority:** high
**Agent:** backend-developer
**Dependencies:** TASK-012, TASK-013
**Notes:** DbSeeder currently only fills legacy Products table. Add seed data for:
- 1 Store ("Магазин №1"), 3 StoreZones (shelf/fridge/freezer)
- 1 Supplier, 5+ CatalogProducts with categories
- 10+ ProductStock batches (mix of safe/warning/critical/expired statuses)
This is required for frontend development with real data.

---

## TASK-011b: Web products page (/products)
**Status:** planned
**Priority:** medium
**Agent:** frontend-developer
**Dependencies:** TASK-006 ✅, TASK-026
**Notes:** Catalog products CRUD using GET /catalog. List + create form + edit.

---

## TASK-027: Web stock page (/stock) — frontend
**Status:** planned
**Priority:** high
**Agent:** frontend-developer
**Dependencies:** TASK-007 ✅, TASK-026
**Notes:** Dense table: product name / barcode / zone / batch / qty / expiry / days left / status badge / actions. Filters: store/zone/status/category. Multi-select for bulk actions.

---

## TASK-028: Web receipts pages (/receipts, /receipts/:id) — frontend
**Status:** planned
**Priority:** medium
**Agent:** frontend-developer
**Dependencies:** TASK-014 ✅, TASK-026

---

## TASK-029: Web transfers page (/transfers) — frontend
**Status:** planned
**Priority:** medium
**Agent:** frontend-developer
**Dependencies:** TASK-015 ✅, TASK-026

---

## TASK-030: Web write-offs page (/write-offs) — frontend
**Status:** planned
**Priority:** medium
**Agent:** frontend-developer
**Dependencies:** TASK-016 ✅, TASK-026

---

## TASK-031: Web analytics page (/analytics) — frontend
**Status:** planned
**Priority:** medium
**Agent:** frontend-developer
**Dependencies:** TASK-019 ✅, TASK-026
**Notes:** Recharts graphs. Expiry summary donut, write-off losses bar chart, movements timeline, by-zone heatmap, by-category table.
