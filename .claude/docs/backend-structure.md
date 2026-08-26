# Backend Structure

**Owner:** backend-developer
**Updated:** 2026-08-26
**Last reviewed:** 2026-07-16 (pre-launch audit) — startup sequence, migration history and feature-status tables below refreshed to reality. Migration Commands section expanded 2026-08-26 after a hand-written-migration incident (see section for detail).

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

**`ITenantContext` (TASK-528) — required convention for all new controllers/services.**
`ShelfGuard.Application/Services/ITenantContext.cs` (impl: `ShelfGuard.Infrastructure/Services/TenantContext.cs`,
registered `AddScoped` in `Infrastructure/DependencyInjection.cs`) centrally resolves the current
staff request's tenant id from the `tenant_id` JWT claim — `Guid? TenantId { get; }`, `null` when
absent/invalid (unauthenticated, provider/consumer session, malformed claim). **New
controllers/services must inject `ITenantContext` and read `.TenantId` instead of writing a new
`ResolveTenantId()`/`GetTenantId()`-style helper that touches `ClaimsPrincipal`/`User.FindFirst("tenant_id")`
directly.**

Migrated so far (TASK-528, Stage A of the consumer-app-builder initiative — see
`docs/architecture/CURRENT_STATE.md` §1): `BannersController`, `LoyaltyController`,
`LoyaltySettingsController`. **The remaining ~40 pre-existing controllers under
`backend/ShelfGuard.Api/Controllers/` still use their own per-controller
`ResolveTenantId()`/`GetTenantId()` helper** (same `User.FindFirst("tenant_id")` logic, just not yet
centralized) — this is a deliberate, incremental migration, not an oversight. Migrate a controller
opportunistically whenever it's next touched for an unrelated change; do not batch-migrate the rest
as a separate task.

**Not the same thing as `ITenantSessionOverride`** (`ShelfGuard.Application/Services/ITenantSessionOverride.cs`).
`ITenantContext` answers "whose own tenant does this staff request belong to" (read-only, from the
JWT). `ITenantSessionOverride` instead lets an already-trusted operation temporarily assume a
*different*, explicitly-chosen tenant's RLS context — the consumer/cross-tenant case
(`ConsumerContentController`, parts of `LoyaltyService`), where the session structurally carries no
`tenant_id` claim at all. Do not use one in place of the other, and do not route
`ConsumerContentController`/`ConsumerLoyaltyController`-style cross-tenant reads through
`ITenantContext` — they correctly stay on `ITenantSessionOverride`.

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

**Never hand-write a migration `.cs` file.** Always generate via `dotnet ef migrations add`. A
2026-08-26 incident: a migration was hand-written (Up/Down only, a suspiciously round timestamp)
instead of tool-generated — it was missing its paired `.Designer.cs` and had silently drifted from
`AppDbContextModelSnapshot.cs`. Regenerating it properly via the real command produced near-identical
`Up`/`Down` content, confirming the schema design itself was correct — the problem was purely
skipping the tool. Hand-writing skips the model-diff-against-current-model safety net and risks
snapshot drift that nothing catches until a later real `migrations add` breaks unexpectedly.

**`dotnet ef` commands do not read `appsettings.Development.json`** — they resolve the connection
string entirely through `ShelfGuard.Infrastructure/Data/AppDbContextFactory.cs`
(`IDesignTimeDbContextFactory<AppDbContext>`), which reads only the `ConnectionStrings__DefaultConnection`
env var, falling back to a hardcoded default that does **not** match this project's local dev
Postgres. Export it first, matching `appsettings.Development.json`'s `DefaultConnection` value:
```bash
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5435;Database=crm;Username=shelfguard_app_dev;Password=<see appsettings.Development.json DefaultConnection>"
```
Skipping this makes `dotnet ef database update`/`migrations list`/`migrations add` fail with a
**misleading** `password authentication failed for user postgres` error — unrelated to the real dev
DB's actual credentials, easy to misdiagnose as a Postgres problem.

**Before pushing:** verify with `dotnet ef database update` against local dev Postgres, then run at
least `dotnet test --filter "FullyQualifiedName~<AffectedFeature>"`. A full-suite local `dotnet test`
run is unreliable — see KI-035 (`known-issues.md`), a pre-existing Postgres connection-pool issue.
If `backend-ci`'s Test step fails after push with `53300: too many clients already` scattered across
unrelated feature test classes (not your own feature's), that's very likely KI-035, not your change —
retrigger with an empty commit rather than debugging already-correct code.

## OpenAPI Contract (TASK-552)
`backend/openapi.json` is a **committed** snapshot of the full Swashbuckle-generated API surface
(all controllers, all feature modules — not scoped to any one area), regenerated via the
`Swashbuckle.AspNetCore.Cli` local dotnet tool (manifest: `backend/.config/dotnet-tools.json`,
pinned to the same `6.5.0` version as the `Swashbuckle.AspNetCore` package reference in
`ShelfGuard.Api.csproj`). It is the source doc-writer agents read from when producing
`docs/integration/MOBILE_API.md`.

**Regenerate after any endpoint/DTO change:**
```bash
cd backend
dotnet build ShelfGuard.Api/ShelfGuard.Api.csproj -c Debug
cd ShelfGuard.Api/bin/Debug/net8.0
ASPNETCORE_ENVIRONMENT=Development dotnet tool run swagger tofile --output ../../../../openapi.json ShelfGuard.Api.dll v1
```
(PowerShell: replace the `ASPNETCORE_ENVIRONMENT=...` prefix with `$env:ASPNETCORE_ENVIRONMENT =
"Development";`.)

Two things make the working directory and env var non-optional, not just style:
- The CLI loads `ShelfGuard.Api.dll` via reflection and runs `Program.cs`'s real top-level
  statements (not just DI registration) — `WebApplication.CreateBuilder` resolves
  `appsettings.{Environment}.json` relative to the **process's current directory**, so you must
  `cd` into the build output folder (where the SDK already copies `appsettings*.json`) rather than
  pass a path to the DLL from elsewhere; `ASPNETCORE_ENVIRONMENT=Development` selects
  `appsettings.Development.json` (has a real `DefaultConnection`) instead of Production (no
  connection string configured outside `.env`/deploy secrets → the run fails with `Host can't be
  null`).
- Because it runs the real startup path, this also runs `db.Database.MigrateAsync()`, the
  KI-028 RLS-role canary, and (Development-gated) `DbSeeder` against whatever `DefaultConnection`
  points at — **the local dev Postgres must be up** (`docker compose up -d`), same precondition as
  `dotnet run`. All idempotent against an already-migrated/seeded dev DB.

`Program.cs`'s `AddSwaggerGen` also sets `c.CustomSchemaIds(type => type.FullName)` — several
feature modules declare same-named DTOs in different namespaces (e.g. `Customers.CustomerDetailDto`
vs `AutoService.Dtos.CustomerDetailDto`); without this the generator throws on the first collision
it walks into instead of producing a document. Schema names in `openapi.json` are therefore fully
namespace-qualified, not bare class names.

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
