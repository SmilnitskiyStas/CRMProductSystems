# TASK-282-arch — Supplier Self-Service: архітектурний дизайн

**Date:** 2026-07-02 · **Agent:** project-architect · **Status:** done
**Deliverables:** ADR-016 (`.claude/docs/decisions.md`), спринт v4.1 у `.claude/tasks/current.md` (TASK-282..288)

## Задача
Спроєктувати роль «Постачальник» — self-service наповнення маркетплейсу (профіль, товари),
видимість рейтингу/відгуків для клієнтів.

## Ключові рішення (деталі — ADR-016)
1. **Supplier = окремий tenant** (`business_type = "supplier"`, default-модуль `marketplace_supplier`),
   роль `supplier_admin`. RLS `tenant_isolation` дає ізоляцію «тільки свої дані» безкоштовно;
   публічний cross-tenant read маркетплейсу вже працює (provider DB context + `is_public`).
2. **Онбординг — провайдер запрошує** через існуючий Admin tenant onboarding; hook авто-створює
   `Supplier` + `SupplierProfile(IsOwnerManaged = true, IsPublic = false)`. User ↔ Supplier через TenantId
   (нова колонка `supplier_profiles.IsOwnerManaged` + partial unique index по TenantId).
3. **Кабінет** — `SupplierCabinetController` (`/api/supplier-cabinet/*`): profile GET/PUT + publish,
   items CRUD (реюз Admin*-методів MarketplaceService), reviews/metrics read-only.
4. **Відгуки** — тільки клієнтські tenant-и; unique (supplier_id, tenant_id) вже існує; guard від
   self-review; `SupplierMetrics.Rating` = AVG, перерахунок синхронно в CreateReviewAsync;
   новий публічний `GET /suppliers/{id}/reviews`.
5. Existing provider-created suppliers (`TenantId = Guid.Empty`, TASK-275) — без змін.

## Декомпозиція
- TASK-282 (database-engineer): міграція — IsOwnerManaged + index, default modules для supplier
- TASK-283 (backend): роль supplier_admin + онбординг-hook
- TASK-284 (backend): SupplierCabinetController
- TASK-285 (backend): reviews guard + public GET reviews + rating recalc
- TASK-286 (frontend): supplier cabinet UI (`features/supplier-cabinet/`, `/supplier/*`)
- TASK-287 (frontend): marketplace enrichment (рейтинг/відгуки на `/marketplace/[id]` та SupplierCard)
- TASK-288 (qa): regression (ізоляція, gating, review-флоу)

## Handoff
Наступний агент: database-engineer (TASK-282). Залежності лінійні по шарах:
282 → 283 → 284 → 286; 282 → 285 → 287; 286+287 → 288.
