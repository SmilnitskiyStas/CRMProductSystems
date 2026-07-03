# TASK-282 — DB: supplier business_type, IsOwnerManaged, дефолтні модулі

**Agent:** database-engineer · **Date:** 2026-07-02 · **Status:** done
**Sprint:** v4.1 «Supplier Self-Service» · **ADR:** ADR-016

## Зміни

### Migration `20260702192126_V41SupplierSelfService`
- `supplier_profiles.IsOwnerManaged boolean NOT NULL DEFAULT false`
- Partial unique index `UX_supplier_profiles_owner_tenant ON supplier_profiles ("TenantId") WHERE "IsOwnerManaged"`
- RLS hardening (raw SQL, ідемпотентно): NULLIF-guard + `FORCE ROW LEVEL SECURITY`
  для `suppliers`, `supplier_profiles`, `supplier_items`, `supplier_metrics`, `supplier_reviews`.
  Причина: на dev-базі `V4SupplierMarketplace` застосувався ПІСЛЯ
  `ForceRlsOnAllTenantTables` + `FixAllRlsPoliciesNullIfEmptyString` (out-of-order),
  тому supplier*-політики не мали guard-а і FORCE RLS. Прод у порядку, але фікс безпечний скрізь.
- Down: drop index + column; RLS-фікс не ревертиться (адитивний, як у FixAllRlsPolicies).

### Domain / EF
- `Tenant.DefaultModulesForBusinessType`: кейс `"supplier"` → `["marketplace_supplier"]`
- `Tenant.UpdateBusinessType`: `"supplier"` додано до valid business types
- `Tenant.UpdateModules`: `"marketplace_supplier"` додано до valid module keys
- `SupplierProfile.IsOwnerManaged` property + EF config (default false, партіальний unique index)
- Snapshot drift fix: `Item.PerishabilityClass` не був у snapshot (hand-written міграція
  `20260627120000_AddItemPerishabilityClass` без Designer). Додано явний конфіг
  `HasMaxLength(20).HasDefaultValue("standard")` в AppDbContext; scaffold-нутий зайвий
  AddColumn видалено з міграції вручну (колонка вже існує в БД).

### Tests
- `TenantTests`: новий InlineData `("supplier", ["marketplace_supplier"])`

## Верифікація
- `dotnet build` — 0 warnings/errors; `dotnet test` — 460/460 passed
- Dev-база (docker crmproductsystems-postgres-1): Up → Down → Up чисто
- Партіальний unique index: другий owner-managed профіль того ж tenant → duplicate key;
  non-owner-managed дублікати tenant — дозволені (перевірено в rolled-back транзакції)
- RLS: усі 5 supplier*-таблиць мають NULLIF-guard + relforcerowsecurity = t

## Handoff
→ TASK-283 (backend-developer): роль supplier_admin + онбординг supplier-tenant.
Домен уже приймає `business_type = "supplier"` і модуль `marketplace_supplier`.
Lookup «мій профіль»: `supplier_profiles WHERE "TenantId" = @tenant AND "IsOwnerManaged"` — гарантовано ≤1 рядок.
