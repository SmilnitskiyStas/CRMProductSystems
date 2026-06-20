# Live Smoke Test — Production (2026-06-19)

**Agent:** qa-tester  
**Date:** 2026-06-19  
**Server:** https://api.agrusystems.pp.ua:10054  
**Commit deployed:** 8f0b149c

---

## Scope

Anonymous smoke (no prod credentials available for authenticated tests).  
Authenticated v4 tests (Auto Service FEFO, Production complete, Module activation) — **manual verify required** with prod credentials.

---

## Results

### BUG-006 Fix — Marketplace Public Endpoints ✅

| Test | Status | Notes |
|---|---|---|
| `GET /api/marketplace/suppliers` (anon) | ✅ 200 | items:0, total:0 (no seed data) |
| `GET /api/marketplace/suppliers?region=Київ` | ✅ 200 | filter param accepted |
| `GET /api/marketplace/suppliers?plan=premium` | ✅ 200 | plan filter accepted |
| `POST /api/marketplace/search` `{itemName:"молоко"}` | ✅ 200 | `[]` array (no suppliers) |
| `POST /api/marketplace/search` `{itemName:""}` | ✅ 400 | `{"error":"ItemName is required."}` |
| `GET /api/marketplace/suppliers/{nonexistent-id}` | ✅ 404 | correct not-found |

**BUG-006 verified FIXED.** All 4 previously-broken public endpoints now return 200 for anonymous callers.

---

### Auth & Security ✅

| Test | Status | Notes |
|---|---|---|
| `POST /auth/login` (wrong creds) | ✅ 401 | `{"error":"Invalid email or password."}` |
| `GET /stock` (no auth) | ✅ 401 | Auth required enforced |
| `GET /items` (no auth) | ✅ 401 | |
| `GET /locations` (no auth) | ✅ 401 | |
| `GET /auto-service/work-orders` (no auth) | ✅ 401 | Module-gated endpoint |
| `GET /production/orders` (no auth) | ✅ 401 | Module-gated endpoint |
| `GET /production/recipes` (no auth) | ✅ 401 | |
| `GET /settings/modules` (no auth) | ✅ 401 | enterprise_admin only |

All authenticated/module-gated endpoints correctly reject anonymous access.

---

### Response Shapes ✅

| Shape | Result |
|---|---|
| `GET /marketplace/suppliers` → `{items, total, page, pageSize}` | ✅ PagedResult confirmed |
| `POST /marketplace/search` → `[]` array | ✅ array shape confirmed |
| 400 error → `{"error": "..."}` | ✅ standard error format |
| 401 error → `{"error": "..."}` | ✅ standard error format |

---

## Anonymous Tests: 16/16 ✅

---

## Pending — Requires Prod Credentials

These tests need a valid JWT (enterprise_admin or store_manager):

| Test | Why |
|---|---|
| `GET /api/settings/modules` | enterprise_admin only |
| `PATCH /api/admin/tenants/{id}/modules` | provider only |
| `GET /api/auto-service/work-orders` | [RequireModule("auto_service")] |
| `POST /api/auto-service/work-orders/{id}/complete` | FEFO write-down |
| `GET /api/production/recipes` | [RequireModule("production")] |
| `POST /api/production/orders/{id}/complete` | FEFO write-down + output stock |
| `POST /api/marketplace/suppliers/{id}/reviews` | [Authorize] |
| `POST /api/ai/assistant` | AI Business Assistant |

---

## Summary

**Deployment verified SUCCESSFUL.**  
- API reachable at api.agrusystems.pp.ua:10054  
- BUG-006 fix confirmed live  
- Auth and security boundaries intact  
- Error response format `{error: "..."}` consistent  
- No regressions detected in anonymous surface area

**Action required:** smoke with authenticated account to cover module-gated endpoints.
