# Done

Completed tasks.

---

## TASK-009: Web auth pages
**Status:** done
**Agent:** frontend-developer
**Completed:** 2026-06-03
**Notes:** Login page (pixel-match to prototype dark theme), JWT storage in lib/api.ts, useLogin/useLogout/useMe hooks, dashboard auth guard layout, edge middleware for cookie-based redirect. tsc: 0 errors.

---

## TASK-005: RoleGuard
**Status:** done
**Agent:** backend-developer
**Completed:** 2026-06-03
**Notes:** 6 named authorization policies (ProviderOnly, AtLeastEnterpriseAdmin, AtLeastNetworkManager, AtLeastStoreManager, CanReceiveStock, CanViewStock). AppRoles constants in Domain. AppPolicies.Configure() in Infrastructure. ProviderController stub. 42 new tests, 68/68 total.

---

## TASK-004: TenantInterceptor (RLS middleware)
**Status:** done
**Agent:** backend-developer
**Completed:** 2026-06-03
**Notes:** DbConnectionInterceptor fires on every pool checkout, sets app.tenant_id + app.role. Role whitelist + UUID validation prevent injection. 13 new tests, 26/26 total.

---

## TASK-003: JWT authentication with refresh tokens
**Status:** done
**Agent:** backend-developer
**Completed:** 2026-06-03
**Notes:** POST /auth/login, POST /auth/refresh, POST /auth/logout, GET /auth/me. BCrypt passwords, 7-day rotating refresh token (HttpOnly cookie, SHA256 hash stored in DB), 15-min JWT access token with tenantId/role/storeId claims. RLS on users + refresh_tokens. 7 new tests, 13/13 total.

---

## TASK-000: Initial project setup and multi-agent infrastructure
**Status:** done
**Agent:** project-manager (lead) + devops-engineer + documentation-writer
**Completed:** 2026-06-03
**Notes:** Full .claude/ structure, CLAUDE.md, agents, skills, docs, templates, memory, tasks backlog (TASK-001–020), ADR-001–005, docker-compose with Redis, /worker BullMQ scaffold.

---

## TASK-001: Rename CRM.* → ShelfGuard.*
**Status:** done
**Agent:** backend-developer
**Completed:** 2026-06-03
**Notes:** All 5 project dirs renamed, all 19 source files updated, old CRM.sln removed, ShelfGuard.sln created. dotnet build: 0 errors. dotnet test: 6/6 passed.

---

## TEST-001: Test inventory product catalog (proof of concept)
**Status:** done
**Agent:** backend-developer + frontend-developer
**Completed:** 2026-06-03
**Notes:** Proof-of-concept CRUD for products. Used CRM.* project naming. Backend builds and all 6 unit tests pass. Frontend feature structure established. Will be superseded by TASK-001 through TASK-007 with real ShelfGuard schema.
