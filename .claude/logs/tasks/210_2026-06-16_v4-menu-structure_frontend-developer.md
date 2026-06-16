# TASK-210 — Frontend: New v4 menu structure

**Agent:** frontend-developer · **Date:** 2026-06-16 · **Status:** done (with documented scope adjustments)

## What was done

### Two prerequisite backend fixes (required to make module-gating actually work, not just compile)

1. **Widened `GET /api/settings/modules` authorization** (`ModulesSettingsController.cs`) from `AppPolicies.AtLeastEnterpriseAdmin` to plain `[Authorize]`. The Sidebar needs every tenant role (cashier, storekeeper, merchandiser, etc.) to read their tenant's active modules to decide which nav groups to show — the TASK-208 policy only allowed enterprise_admin/provider, so every other role would have 403'd trying to load the sidebar's module data. **Confirmed with the user before making this change** (it's a real authorization loosening, flagged explicitly rather than done silently). Verified: `storekeeper` demo user now gets 200 from this endpoint (previously would have been 403).

2. **`V4ModulesBackfill` migration** (resolves KI-012). Existing tenants (created before TASK-208) still had legacy module keys (`shelf_manager`, `crm`, `notifications`) in `Modules`, not the v4 vocabulary. Since this task gates real nav groups (Operations/Sales/Procurement) on v4 module keys, deploying this Sidebar change without backfilling would have hidden those groups for every existing tenant, including production. The migration is a one-time, idempotent `UPDATE tenants SET "Modules" = ... WHERE NOT (already has any v4 key)`, mirroring `Tenant.DefaultModulesForBusinessType` exactly. Verified locally: demo tenant "Свіжий Кут" went from `["shelf_manager","crm","notifications"]` → `["inventory","procurement","pos"]`. `Down()` is intentionally a no-op (data-only migration, no destructive consequence either direction).

### Sidebar restructure (`frontend/components/layout/Sidebar.tsx`)
Regrouped nav items per the v4-spec structure, using existing pages only (no new pages/routes created):

| New group | Module gate | Items (existing routes) |
|---|---|---|
| Операції (Operations) | `inventory` | Каталог, Залишки, Прийомка, Переміщення, Списання, Локації, IoT пристрої |
| Продажі (Sales) | `pos` | Каса, Продажі, Події |
| Постачання (Procurement) | `procurement` | Замовлення постачання, AI Постачання |
| Аналітика (Analytics) | — (role-only, unchanged) | Аналітика, POS Аналітика |
| Персонал (Workforce) | — (role-only, unchanged) | Персонал |
| Адмін | — (role-only, unchanged) | Провайдер, Адмін |

`NavGroup` gained an optional `moduleKey?: ModuleKey`. New helper `isModuleActive()` hides a group only when module data has loaded AND the key is absent — while loading, groups default to visible (avoids a flash-hide-then-show; this is a UX concern only, actual route access is still enforced server-side regardless of what the sidebar shows). `Sidebar` now calls `useModules(enabled: userRole !== "provider")` — provider is excluded from the call since it has no tenant_id outside impersonation and would 403.

## Scope adjustments from the literal backlog spec (documented, not silently dropped)

- **"Клієнти" (Sales), "Постачальники" (Procurement), "Фінанси"/"Прогнозування" (Analytics), "Графіки"/"Ролі" (Workforce) are not in the sidebar** — none of these have a page implemented yet. Adding nav links to non-existent routes would create dead links; adding placeholder pages would be scope creep into other future tasks (Customers/Suppliers/Forecasting are separate features, not part of this nav-restructuring ticket).
- **Marketplace and Service Desk groups are omitted entirely** — Marketplace has zero implementation (Phase 3, not started). Service Desk only exists as the "Підтримка" tab inside Settings, not a standalone route; no dedicated page to link to. Both are called out in a code comment in `Sidebar.tsx` so whoever builds those pages knows exactly where to wire them in.
- **`/orders` mapped to Procurement, not Sales** — despite being in the old "Продажі" group, its actual function (AI order-quantity calculation for supply orders) matches "Замовлення постачання" in the v4 spec's Procurement bullet, not Sales' "Замовлення" (which the spec pairs with Каса/Клієнти — more like a customer order/sales-order concept that doesn't exist here).
- **Локації and IoT folded into Operations** (previously their own "Управління" group with Персонал). The v4 spec's bullet list doesn't mention them at all; since they're inventory/stock-adjacent (locations are where stock lives, IoT feeds stock monitoring), Operations was the more defensible home than inventing an ungated catch-all group.

## Verification
- `dotnet build` → 0 errors; `dotnet test` → 420/420 passed
- `npx tsc --noEmit` → 0 errors; `npm run build` → 27/27 pages
- Live-tested against local API: `ea@demo.local` (enterprise_admin) and `keeper@demo.local` (storekeeper) both get 200 + the backfilled v4 module list from `/api/settings/modules`

## Files changed
- `backend/ShelfGuard.Api/Controllers/ModulesSettingsController.cs` (policy widened)
- `backend/ShelfGuard.Infrastructure/Migrations/20260616200319_V4ModulesBackfill.cs` (+Designer.cs) (new)
- `backend/ShelfGuard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`
- `frontend/components/layout/Sidebar.tsx`
- `.claude/docs/known-issues.md` (KI-012 → resolved)

## Next
Phase 3+ (Supplier Marketplace, Auto Service, Production, AI Assistant) — Marketplace nav entry should be added when TASK-220/222 ships a real page.
