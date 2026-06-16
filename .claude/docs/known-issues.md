# Known Issues

**Owner:** qa-tester
**Updated:** 2026-06-04

## Active Issues

### KI-004: Duplicate `apiFetch` in feature API modules
Severity: high
Status: open
Description: `features/inventory/api/products.ts` and `features/dashboard/api/dashboard.ts` define their own local `apiFetch` instead of using `lib/api.ts`. The inventory version sends no Authorization header; the dashboard version skips 401 retry. Product API calls will silently break once the endpoint requires auth.
Resolution: Delete local `apiFetch` in both files and use `import { api } from "@/lib/api"`.

### KI-005: Hardcoded bcrypt hash in DbSeeder.cs
Severity: high
Status: open
Description: `DbSeeder.cs` contains a hardcoded bcrypt hash (`$2a$12$eump...`) committed to source control. Anyone with repo access knows the demo password.
Resolution: Inject `IPasswordHasher` into `DbSeeder` and call `.Hash(config["Seed:DefaultPassword"])` at runtime. Or read from env/config.

### KI-006: Auto-migrate + seed runs in all environments
Severity: medium
Status: open
Description: `Program.cs` calls `MigrateAsync()` and `DbSeeder.SeedAsync()` unconditionally. In production this risks migration race conditions (multiple replicas) and seeds demo users.
Resolution: Guard with `if (app.Environment.IsDevelopment())` or a dedicated `--seed` CLI flag.

### KI-007: Dashboard stats derived from POC Products table (fake data)
Severity: medium
Status: open
Description: Dashboard Safe/Warning/Critical/Expired cards are computed from `stockQuantity` vs `reorderLevel` in the POC `Products` table — not from real `product_stock` batches with expiry dates. "Expired" = stockQuantity is 0, which is incorrect.
Resolution: Implement TASK-011 (`/api/stock` endpoint) and TASK-012 (seed real batches), then replace `dashboardApi` to call the real analytics endpoint.

### KI-008: No pagination on GET /api/products
Severity: medium
Status: open
Description: Returns all products in one response. Will degrade at 1000+ items.
Resolution: Add `?page=&pageSize=` query params before staging deploy.

### KI-009: `staleTime` missing on `useProducts` hook
Severity: low
Status: open
Description: Every component mount that uses `useProducts` triggers a refetch. Dashboard hooks have `staleTime: 60_000` but inventory hook does not.
Resolution: Add `staleTime: 60_000` to `useProducts` query options.

### KI-010: Store map zones are static placeholder data
Severity: low
Status: open
Description: `StoreMap` component on dashboard renders hardcoded zone data. Real zone data requires `/api/stores/:id/zones` endpoint (not yet implemented).
Resolution: Implement stores API (part of TASK-011 or separate task), then wire `StoreMap` to real data.

### KI-011: Sidebar links to unimplemented pages show "coming soon"
Severity: low
Status: open
Description: `/stock`, `/transfers`, `/write-offs`, `/analytics`, `/notifications`, `/settings` show a catch-all "in development" page. Not a bug — intended placeholder.
Resolution: Implement each page per sprint plan.

### KI-012: Existing tenants have stale legacy module keys, not v4 module keys
Severity: medium
Status: open
Description: TASK-208 added `Tenant.HasModule()` and `[RequireModule("key")]` gated on the v4 module vocabulary (`inventory`, `procurement`, `pos`, `auto_service`, `production`, `marketplace`). New tenants created via `POST /api/admin/tenants` now get a default set based on `business_type`. But existing tenants (e.g. the seeded/production tenant "Свіжий Кут") still have legacy module keys in their `Modules` JSONB (`["shelf_manager", "crm", "notifications"]`) from before this feature existed — none of the v4 keys. `[RequireModule]` is not yet attached to any live controller, so this is currently harmless, but the moment any future task (Phase 4 Auto Service, Phase 5 Production, etc.) puts `[RequireModule(...)]` on a real endpoint, every pre-existing tenant will get 403 on it until backfilled.
Resolution: Before attaching `[RequireModule]` to any live controller, run a one-time data migration/script that calls `tenant.UpdateModules(Tenant.DefaultModulesForBusinessType(tenant.BusinessType))` for every existing tenant (or merges the v4 defaults into their current list rather than overwriting, if legacy module flags still gate anything elsewhere).

## Resolved Issues

### KI-001: Backend uses CRM.* project names ✅ resolved (2026-06-03)
Resolution: All backend projects renamed to ShelfGuard.* as part of initial setup.

### KI-002: No authentication implemented ✅ resolved (2026-06-03)
Resolution: Full JWT auth with refresh tokens implemented in TASK-003 (AddAuth migration + AuthService + AuthController).

### KI-003: Full v1 schema not yet migrated ✅ resolved (2026-06-04)
Resolution: TASK-002 completed — 19 new tables, RLS on all tenant tables, FEFO index applied via FullSchema migration.
