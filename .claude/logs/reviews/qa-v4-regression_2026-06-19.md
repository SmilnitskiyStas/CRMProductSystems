# QA Regression Review — v4 Modules
**Date:** 2026-06-19  
**Agent:** qa-tester  
**Scope:** Phase 1–6 (TASK-200..TASK-250)

---

## 1. Automated Test Suite

```
dotnet test (no build)
Passed!  Failed: 0, Passed: 459, Skipped: 0  Duration: 1s
```

All 459 unit tests green. New v4 coverage:
- `RequireModuleFilterTests` — 5 tests (403 on missing tenant, 403 on disabled module, provider bypass)
- `MarketplaceServiceTests` — 15 tests (listing, search, premium gating, review dedup)
- `SupplierAdvisorTests` — AI recommendation tests
- `AutoServiceServiceTests` — 6 tests (delete guards, FEFO complete, insufficient stock 422)
- `ProductionServiceTests` — 9 tests (FEFO multi-batch, insufficient stock, cancel/done guards, empty ingredients 400)

---

## 2. Static Code Review — Bugs Found

### BUG-006 (critical) — Marketplace public endpoints blocked by RequireModuleFilter

**File:** `backend/ShelfGuard.Api/Controllers/MarketplaceController.cs`  
**Lines:** 37, 60, 84, 93

All four public marketplace endpoints have both `[AllowAnonymous]` AND `[RequireModule("marketplace")]`:

```csharp
[HttpGet("suppliers")]
[AllowAnonymous]
[RequireModule("marketplace")]   // ← blocks anonymous!
```

`RequireModuleFilter.OnActionExecutionAsync` logic:
1. Reads `role` from JWT claims — anonymous request → null
2. Checks `role == "provider"` → false
3. Reads `tenant_id` from JWT claims → null
4. `Guid.TryParse(null, ...)` → false → **returns 403 "Module not activated"**

`[AllowAnonymous]` only bypasses ASP.NET Core auth middleware (401/403 auth checks). The action filter runs independently and ignores it.

**Result:** Anonymous GET to `/api/marketplace/suppliers` → **403**, not 200.  
**Contradicts:** TASK-221 spec ("публічний listing (без auth)"), controller docstring ("unauthenticated callers can browse the marketplace").

**Fix:** Remove `[RequireModule("marketplace")]` from the four `[AllowAnonymous]` endpoints. Public discovery has no tenant context → module gating makes no sense.

Affected endpoints:
- `GET /api/marketplace/suppliers`
- `GET /api/marketplace/suppliers/{id}`
- `GET /api/marketplace/suppliers/{id}/items`
- `POST /api/marketplace/search`

`POST /api/marketplace/suppliers/{id}/reviews` and `POST /api/marketplace/ai-recommend` are `[Authorize]` → fine to keep `[RequireModule]`.

---

## 3. Module Activation — Phase 1 (TASK-208) ✅

RequireModuleFilter unit tests verify all critical paths:
- Module disabled → 403 ✅
- Module enabled → passes ✅
- Missing tenant claim → 403 ✅
- Provider role → bypass (no DB lookup) ✅
- Tenant not found in DB → 403 ✅

Default modules per business_type verified in `TenantTests.cs` and `TenantAdminServiceTests.cs`.

---

## 4. Auto Service Module — Phase 4 (TASK-230..233) ✅ (with notes)

Unit tests cover:
- Delete customer with vehicles → 409 ✅
- Delete customer without vehicles → 204 ✅
- Add line to Done order → 409 ✅
- FEFO complete — pre-validation atomic (insufficient stock → 422, no partial writes) ✅
- FEFO complete — happy path, `stock_events` type `auto_service_consumption` ✅

**Note (non-blocking):** `GET /api/auto-service/work-orders` response not tested against kanban filter (status param). Verify manually that `?status=in_progress` filters correctly.

---

## 5. Production Module — Phase 5 (TASK-240..242) ✅

Unit tests cover:
- Complete from Planned → 200, FEFO multi-batch consumed in expiry order ✅
- Complete from InProgress → 200 ✅
- Complete from Done → 409 ✅
- Insufficient stock → 422, no partial DB writes ✅
- Output batch created with `PROD-` prefix, correct qty ✅
- `production_consumption` + `production_output` stock events ✅
- Cancel Done order → 409 ✅
- Cancel Planned order → Cancelled ✅
- Create recipe with empty ingredients → 400 ✅
- Deactivate recipe with active orders → 409 ✅
- Deactivate recipe without active orders → 204 ✅

All 9 tests green. FEFO and atomicity logic verified.

---

## 6. Supplier Marketplace — Phase 3 (TASK-220..223)

**BUG-006 (see above)** — public endpoints blocked for anonymous.

Service layer tests (authenticated path):
- Public listing returns only is_public=true ✅
- Premium field gating (unauthenticated + free plan → hidden) ✅
- Search by item name ✅
- Duplicate review → 409 ✅
- Rating 1-5 validation — tested via service, not controller ⚠️ (controller doesn't validate rating separately from service)
- AI recommendation — tested with fake advisor ✅

---

## 7. AI Business Assistant — Phase 6 (TASK-250) ✅ (limited)

`POST /api/ai/assistant` — `[Authorize]` + `[RequireModule("inventory")]`.
No unit tests for `AiAssistantService` (mock-hostile — aggregates DB + Claude API).
Acceptance: build green, pattern mirrors tested `ClaudeOrderAdvisor`.

**Note:** Requires live Anthropic credits for e2e. Verify manually once credits confirmed active.

---

## 8. Entity Rename (Phase 1 — TASK-200..207) ✅

- `StoreServiceTests` renamed → still present and green (tests operate on `Location` entity)
- `ItemServiceTests` covers catalog → items rename
- Legacy `/api/stores` → 301 redirect: verified in `201_2026-06-15_location-entity-rename_backend-developer.md`
- Legacy `/api/catalog` → 301 redirect: verified in `205_2026-06-16_item-entity-rename_backend-developer.md`

---

## Summary

| Module | Status | Notes |
|---|---|---|
| Phase 1 Entity Rename | ✅ pass | |
| Phase 2 Module Activation | ✅ pass | |
| Phase 3 Marketplace | ⚠️ BUG-006 | Public endpoints blocked (critical) |
| Phase 4 Auto Service | ✅ pass | Minor: kanban filter manual verify |
| Phase 5 Production | ✅ pass | |
| Phase 6 AI Assistant | ⚠️ pending | Requires live Anthropic credits |

---

## Required Actions

1. **backend-developer** → fix BUG-006 (remove `[RequireModule]` from 4 public endpoints)
2. **manual** → re-test `GET /api/marketplace/suppliers` anonymously after fix
3. **manual** → verify `GET /api/auto-service/work-orders?status=in_progress` filters
4. **manual** → verify AI Assistant with live Anthropic key
