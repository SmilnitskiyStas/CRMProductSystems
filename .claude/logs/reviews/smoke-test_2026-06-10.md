# Smoke Test — ShelfGuard Production
**QA Agent:** qa-tester
**Date:** 2026-06-10
**Environment:** http://93.127.143.98:10053 (Docker, PostgreSQL seed data)
**Tested as:** ea@demo.local (enterprise_admin)

---

## Test Results Summary

| # | Endpoint | Expected | Result | Status |
|---|---|---|---|---|
| 1 | POST /api/auth/login | 200 + JWT | 200 ✅ | PASS |
| 2 | GET /api/auth/me | 200 + user | 200 ✅ | PASS |
| 3 | GET /api/products | 200, 15 items | 200, 15 ✅ | PASS |
| 4 | GET /api/stock | 200, 25 batches | 200, 25 ✅ | PASS |
| 5 | GET /api/stock/summary | 200 + stats | **404** ❌ | FAIL |
| 6 | GET /api/receipts | 200, items | 200, 17 ✅ | PASS |
| 7 | GET /api/transfers | 200, items | 200, 6 ✅ | PASS |
| 8 | GET /api/write-offs | 200, items | 200, 10 ✅ | PASS |
| 9 | GET /api/suppliers | 200, 3 items | 200, 3 ✅ | PASS |
| 10 | GET /api/stores | 200, 2 stores | 200, 2 ✅ | PASS |
| 11 | GET /api/analytics/write-offs | 200 + data | 200 ✅ | PASS |
| 12 | GET /api/analytics/summary | 200 + data | **404** ❌ | FAIL |
| 13 | GET /api/movements | 200 | 200 ✅ | PASS |
| 14 | GET /api/discounts | 200, [] | 200, [] ✅ | PASS |
| 15 | GET /api/notifications/settings | 200, [] | 200, [] ✅ | PASS |
| 16 | GET /api/notifications/history | 200 | 200 ✅ | PASS |
| 17 | GET /api/users | 200, 6 users | 200, 6 ✅ | PASS |
| 18 | GET /api/provider/health | 200 | 200 ✅ | PASS |
| 19 | No token → 401 | 401 | 401 ✅ | PASS |
| 20 | keeper → POST /api/users/invite → 403 | 403 | 403 ✅ | PASS |
| 21 | POST /api/discounts (% > 100) → 400 | 400 + error | 400 ✅ | PASS |
| 22 | GET /api/products/{wrong-id} → 404 | 404 | 404 ✅ | PASS |
| 23 | FEFO: stock expiryDate ordering | oldest first | dates ascending ✅ | PASS |
| **24** | **provider GET /api/products** | **403 or []** | **200 + tenant data** ❌ | **FAIL (SEC)** |

---

## Bugs Found

### BUG-001 — SECURITY: Provider reads tenant catalog without impersonation
**Severity:** HIGH
**Endpoint:** `GET /api/products` with provider JWT (`admin@shelfguard.local`)
**Expected:** 403 Forbidden OR empty `[]` (RLS blocks cross-tenant reads)
**Actual:** 200 + full tenant catalog (15 products returned)
**Root cause:** `TenantConnectionInterceptor` likely sets `app.tenant_id = ''` when JWT has no tenant_id claim. PostgreSQL RLS policy `USING (tenant_id = current_setting('app.tenant_id')::uuid)` fails silently when cast is invalid → returns all rows instead of 0.
**Fix:** In interceptor — if tenant_id is absent, set `app.tenant_id = '00000000-0000-0000-0000-000000000000'` (never-matching UUID) instead of empty string.

---

### BUG-002 — GET /api/stock/summary → 404
**Severity:** MEDIUM
**Expected:** Dashboard stats endpoint (safe/warning/critical/expired counts)
**Actual:** 404 Not Found
**Impact:** Dashboard frontend stats cards likely show 0 or use fallback data
**Fix:** Implement `GET /api/stock/summary` endpoint in StockController

---

### BUG-003 — GET /api/analytics/summary → 404
**Severity:** LOW
**Expected:** Analytics overview endpoint
**Actual:** 404 (only `/api/analytics/write-offs` works)
**Impact:** Analytics page may be partially broken
**Fix:** Implement or map correct analytics summary route

---

### BUG-004 — Inconsistent 404 error format
**Severity:** LOW
**Endpoint:** `GET /api/products/{nonexistent-id}`
**Expected:** `{ "error": "Product not found." }` (per api-contracts.md)
**Actual:** ASP.NET ProblemDetails `{ "type": "...", "title": "Not Found", "status": 404, "traceId": "..." }`
**Impact:** Frontend error handling may break (expects `error` field)
**Fix:** Add global exception middleware or return `NotFound(new { error = "..." })` consistently

---

## Overall Status

| Layer | Score | Notes |
|---|---|---|
| Auth & Security | 🟡 8/10 | BUG-001 is serious — provider bypasses RLS |
| Core API (stock/receipts/transfers) | ✅ 10/10 | All working, data correct |
| Role enforcement | ✅ 10/10 | 401/403 correct |
| Data integrity | ✅ 10/10 | 15 products, 25 batches, seed data intact |
| FEFO ordering | ✅ 10/10 | expiryDate ascending in stock list |
| Analytics | 🟡 7/10 | Only write-offs endpoint works |
| Error format consistency | 🟡 7/10 | Mixed ProblemDetails vs {error} |

**Production readiness: 🟡 CONDITIONAL PASS**
Core functionality works. BUG-001 must be fixed before real clients use provider role.
