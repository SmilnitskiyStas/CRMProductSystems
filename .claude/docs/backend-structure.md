# Backend Structure

**Owner:** backend-developer
**Updated:** 2026-06-04
**Last reviewed:** 2026-07-16 (pre-launch audit) — startup sequence, migration history and feature-status tables below refreshed to reality.

## Layer Responsibilities
```
ShelfGuard.Api          — HTTP routing, auth middleware, DI wiring — no business logic
ShelfGuard.Application  — Use cases, business rules, DTOs, service interfaces
ShelfGuard.Domain       — Entities, value objects, repository interfaces, domain rules
ShelfGuard.Infrastructure — EF Core, repositories, Claude API, Telegram, BullMQ producers
```

## Dependency Direction
```
Api → Application → Domain
Infrastructure → Application, Domain
(Infrastructure implements Domain interfaces)
```

## Service Pattern
- Interface defined in `Application`, implementation in `Application`
- Repository interface in `Domain`, implementation in `Infrastructure`
- Example: `IAuthService` (Application) ← `AuthService` (Application); `IUserRepository` (Domain) ← `UserRepository` (Infrastructure)

## Tenant Context
`TenantConnectionInterceptor` (EF Core `DbConnectionInterceptor`) fires on every connection open.
Reads JWT claims, validates role whitelist, sets `app.tenant_id` and `app.role` PostgreSQL session variables.
All DB queries automatically filtered by RLS — application layer never filters by tenant manually.

> **Critical (KI-027/KI-028):** RLS is the *sole* tenant-isolation layer for single-object reads
> (`GetByIdAsync` methods carry no `&& TenantId==` clause by design). It only works if the app's
> Postgres connection role is a **non-superuser with `NOBYPASSRLS`** — a superuser silently bypasses
> all `tenant_isolation`/FORCE-RLS policies. Use the dedicated `shelfguard_app`-family role, never the
> bootstrap `POSTGRES_USER`. The `tenant_isolation` policy is **fail-closed** (Block 2 fix): with
> `app.tenant_id` unset it returns zero rows via `NULLIF(current_setting(...),'')`.

## Startup Sequence (Program.cs)
1. `db.Database.MigrateAsync()` — auto-apply pending migrations (unconditional; deploy depends on it)
2. **RLS-role canary (KI-028)** — `RlsRoleGuard.Evaluate`: if the connected role can bypass RLS,
   fail-fast (throw, refuse to boot) outside Development; log CRITICAL but allow boot in Development.
3. `DbSeeder.SeedAsync(...)` — gated `IsDevelopment() || SEED_ON_START=="true"` (KI-006 resolved);
   never seeds in production by default. Hashes the seed password at runtime via `IPasswordHasher`
   (KI-005 resolved — no hardcoded hash).
4. Swagger only in Development
5. Middleware: CORS → Authentication → Authorization → Controllers

## Migration Commands
```bash
cd backend/
dotnet ef migrations add <Name> --project ShelfGuard.Infrastructure --startup-project ShelfGuard.Api
dotnet ef database update --project ShelfGuard.Infrastructure --startup-project ShelfGuard.Api
```
> Stop the running API process before running these — DLL locking will fail the build.

## Migration History
The 3-row table below is long superseded — the project now has **~75 EF migrations** through v1→v4
(auth, full v1 schema, v2 orders/buffer/AI, v3 POS/IoT/ПРРО, v4 Store→Location + Product→Item renames,
module activation, marketplace, custom roles). Check the actual `ShelfGuard.Infrastructure/Migrations/`
folder rather than this doc. **Pre-launch audit added these (dev-applied, not yet on prod — see
`prelaunch-readiness.md`):** `FixMissingRlsGuardsAndProviderBypass`, `FixFailOpenTenantIsolationOnReset`,
`AddStockReceiptsTransfersTenantIndexes`, `AddProductStockXminConcurrencyToken`,
`FixNotificationSettingsRlsFailOpen`, `AddChatAndSupportMessagesRls`,
`AddActivityLogsIndexesAndDropSupersededStockIndexes`, `AddChatSessionsAndSupplySchedulesTenantIndexes`
(+ `ExpandProviderBypassToProviderAdmin`, prepared but not applied — decision pending).

## Feature Implementation Status
All v1→v4 features are shipped (controller + service + repository + migration). The per-feature status
table that used to live here (marking Stock/Receipts/Transfers/Write-offs/Stores/Analytics/Notifications
as 🕐 pending) is obsolete — those are all implemented. Notable renames since 2026-06-04:
`ProductsController` → redirect shim to `ItemsController` (`items` table); `StoresController` retired
in favour of `LocationsController` (`locations` table); `SupportController` retired in favour of
ServiceDesk (TASK-365). For the current feature inventory see CLAUDE.md's backend layout and
`.claude/docs/api-contracts.md`.
