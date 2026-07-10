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

### KI-014: Per-IP rate limiting is ineffective in production (client IPs not preserved)
Severity: medium
Status: open (root cause outside our stack)
Description: The API's per-IP rate limiter (TASK-329) works locally (verified: 10×401 + 5×429
on 15 parallel wrong logins) but never triggers in production — 15 parallel wrong logins all
return 401. The deployed build is confirmed current (new headers + 2FA endpoints live).
Root cause (most probable): the hosting provider's port-mapping layer (external 10054 → nginx
8443) terminates TCP and does not preserve client source IPs — each connection reaches nginx
from a different internal address, so per-IP partitions (API RateLimiter, nginx limit_req on
$binary_remote_addr) never accumulate. Verify via `docker logs shelfguard_api | grep "unknown email"`
(failed logins log the perceived IP) — if IPs vary per request/connection, this is confirmed.
Impact: volumetric/distributed brute force is not rate-limited per IP. Mitigations already live
and IP-independent: per-account lockout (5 fails → 15 min), password policy (12+ chars, blocklist),
opt-in 2FA TOTP. Fail2ban caveat: if SSH source IPs are also masked/shared, sshd bans could hit
a shared egress IP (self-DoS) — check `journalctl -u ssh` / auth.log source IPs before enabling.
Resolution options: ask the provider whether real client IPs can be preserved (PROXY protocol /
X-Forwarded-For from their edge → then trust it in nginx `set_real_ip_from`), or move TLS/edge
to a layer that preserves IPs (e.g., free Cloudflare in front).

### KI-013: Npgsql 8.0+ requires EnableDynamicJson() for List<string>/JSONB fields
Severity: high (silent 500 in production)
Status: resolved (2026-06-27)
Description: Npgsql 8.0+ breaking change — `List<string>` та інші складні .NET типи більше не десеріалізуються з JSONB-колонок автоматично. Без `EnableDynamicJson()` API повертає `System.NotSupportedException` → 500 на всіх GET-ендпоінтах, що читають JSONB. Проявилось після деплою поля `Barcodes: List<string>`.
Resolution: У `backend/ShelfGuard.Infrastructure/DependencyInjection.cs` замінено `UseNpgsql(connectionString)` на `NpgsqlDataSourceBuilder(...).EnableDynamicJson().Build()`, результат передається у `UseNpgsql(dataSource)`.
Rule: **При кожному новому полі `List<T>` / JSONB** — перевіряти, що `EnableDynamicJson()` вже є у `DependencyInjection.cs`. Якщо у prod-логах з'явився `InvalidCastException` / `NotSupportedException` з текстом `jsonb` — перша підозра саме тут.

## Resolved Issues

### KI-012: Existing tenants have stale legacy module keys, not v4 module keys ✅ resolved (2026-06-16)
Resolution: TASK-210 added migration `V4ModulesBackfill` — a one-time, idempotent data migration that sets `Modules` to `Tenant.DefaultModulesForBusinessType(tenant.BusinessType)` for any tenant whose `Modules` doesn't already contain at least one v4 key. Applied locally; verified the demo tenant went from `["shelf_manager","crm","notifications"]` to `["inventory","procurement","pos"]`. Sidebar (TASK-210) now gates the Operations/Sales/Procurement groups on these keys via `useModules()`.

### KI-001: Backend uses CRM.* project names ✅ resolved (2026-06-03)
Resolution: All backend projects renamed to ShelfGuard.* as part of initial setup.

### KI-002: No authentication implemented ✅ resolved (2026-06-03)
Resolution: Full JWT auth with refresh tokens implemented in TASK-003 (AddAuth migration + AuthService + AuthController).

### KI-003: Full v1 schema not yet migrated ✅ resolved (2026-06-04)
Resolution: TASK-002 completed — 19 new tables, RLS on all tenant tables, FEFO index applied via FullSchema migration.
