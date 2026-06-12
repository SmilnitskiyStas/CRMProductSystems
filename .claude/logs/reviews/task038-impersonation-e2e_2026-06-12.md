# QA Review — TASK-038: Provider Impersonation E2E
**Agent:** qa-tester
**Date:** 2026-06-12
**Environment:** production API (manager/provider demo accounts)
**Verdict:** ✅ PASS — 12/12 checks, no bugs

## Results

| # | Check | Result |
|---|---|---|
| 1 | Provider login (admin@shelfguard.local) | ✅ |
| 2 | GET /provider/tenants — list with counts | ✅ Свіжий Кут, plan/counters present |
| 3 | GET /provider/tenants/{id} — detail | ✅ users=6, stores=2, modules |
| 4 | POST impersonate → JWT | ✅ claims verified: role=enterprise_admin, tenant_id=8abfbbb5…, **TTL 60 min** |
| 5 | Impersonation token reads tenant data | ✅ GET /products → 16 items |
| 6 | **SECURITY: impersonation token on /provider/*** | ✅ **403** — scoped token carries no provider rights |
| 7 | Audit trail | ✅ activity_logs: `provider.impersonate` — "Provider admin@shelfguard.local started impersonation of tenant 'Свіжий Кут'" |
| 8 | PUT plan persists | ✅ 204, detail reflects change (restored to basic after test) |
| 9 | PUT modules persists | ✅ 204 with valid names; whitelist: shelf_manager, crm, notifications, auto_order, iot, cv_camera |
| 10 | Invalid module → 400 | ✅ `{"error":"Unknown modules: bogus_module."}` |
| 11 | DELETE impersonate | ✅ 204 (client-side signal per design) |
| 12 | Negatives | ✅ nonexistent tenant → 404; store_manager on /provider → 403 |

## Notes (not bugs)
- First PUT modules attempt 400'd because the test used short names ("shelf") —
  validation working as designed; module whitelist documented above.
- DELETE impersonate is stateless by design (comment in controller): the 60-min JWT
  cannot be revoked server-side. Acceptable for v1 given the short TTL; a token
  denylist would be the v1.1 hardening if needed.
- Products count 16 vs seed 15 — a product was added during earlier demos; not an issue.

## Conclusion
TASK-038 closed. Provider panel impersonation is production-ready:
scoped tokens, correct privilege drop, full audit trail, validated plan/module management.
