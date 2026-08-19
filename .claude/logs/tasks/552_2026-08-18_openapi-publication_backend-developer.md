# TASK-552 (backend half) — OpenAPI publication

**Status:** done (backend half only — documentation-writer covers `MOBILE_API.md`/`CHANGELOG.md` next)
**Agent:** backend-developer
**Outcome:** `backend/openapi.json` committed, generated via a local dotnet tool. One config-only
code change in `Program.cs` was required (schema collision fix), no business logic touched.

## What was set up

- **Local dotnet tool manifest:** `backend/.config/dotnet-tools.json` (new), installed
  `Swashbuckle.AspNetCore.Cli` pinned to `6.5.0` — same version as the `Swashbuckle.AspNetCore`
  package already referenced in `ShelfGuard.Api.csproj`.
- **Generated file:** `backend/openapi.json` (new, ~1.17 MB) — full API surface, not scoped to any
  one feature: 351 paths, 424 schemas, `openapi: 3.0.1`.
- **Doc note:** new "OpenAPI Contract (TASK-552)" section in `.claude/docs/backend-structure.md`
  (after the existing "Migration Commands" section) with the exact regeneration command and why
  the working directory / env var / local Postgres matter.

## Regeneration command (for documentation-writer and future devs)

```bash
cd backend
dotnet build ShelfGuard.Api/ShelfGuard.Api.csproj -c Debug
cd ShelfGuard.Api/bin/Debug/net8.0
ASPNETCORE_ENVIRONMENT=Development dotnet tool run swagger tofile --output ../../../../openapi.json ShelfGuard.Api.dll v1
```

Requires local dev Postgres running (`docker compose up -d`) — see "why" below.

## Code change required (and why)

`backend/ShelfGuard.Api/Program.cs`, inside the existing `AddSwaggerGen(c => {...})` block:

1. **`c.CustomSchemaIds(type => type.FullName);`** — mandatory, not optional. First generation
   attempt threw `SwaggerGeneratorException: Can't use schemaId "$CustomerDetailDto" ... already
   used for type "$ShelfGuard.Application.Features.AutoService.Dtos.CustomerDetailDto"` —
   Swashbuckle's default schemaId is the bare class name, and this codebase has multiple
   feature-namespaced DTOs sharing a short name (`Customers.CustomerDetailDto` vs
   `AutoService.Dtos.CustomerDetailDto`). Without this, generation fails outright at the first
   collision it walks into; it does not skip/warn. Schema names in `openapi.json` are therefore
   fully namespace-qualified (e.g. `ShelfGuard.Application.Features.Customers.CustomerDetailDto`),
   not bare class names — flag this for the documentation-writer since it affects how schema refs
   read in the published contract.
2. **`c.SwaggerDoc("v1", new OpenApiInfo { Title = "ShelfGuard API", Version = "v1" });`** —
   cosmetic only. Without it, `info.title` in the JSON defaulted to the raw assembly display name
   (`"ShelfGuard.Api, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"`).

No other `.cs` file was touched. No controllers/DTOs/business logic changed.

## Non-obvious operational notes (for whoever regenerates this next)

- The CLI tool loads `ShelfGuard.Api.dll` via reflection and executes `Program.cs`'s real
  top-level statements (DI registration, `app.Build()`, and everything between `Build()` and
  `Run()`) — it is **not** a static-analysis-only tool. Concretely this means:
  - `appsettings.{ASPNETCORE_ENVIRONMENT}.json` resolves relative to the **process's current
    directory**, not the DLL's location — you must `cd` into
    `ShelfGuard.Api/bin/Debug/net8.0/` before invoking (where the SDK already copies
    `appsettings*.json`), not just point the CLI at a path.
  - Without `ASPNETCORE_ENVIRONMENT=Development` it defaults to Production, which has no
    `ConnectionStrings:DefaultConnection` outside deploy secrets → fails immediately with
    `System.ArgumentException: Host can't be null` in `NpgsqlDataSourceBuilder.Build()`
    (`ShelfGuard.Infrastructure/DependencyInjection.cs:30` builds the Npgsql data source eagerly
    during service registration, before `Build()` — so this isn't skippable).
  - Because the real startup path runs, it also runs `db.Database.MigrateAsync()`, the KI-028
    RLS-role canary, and (Development-gated) `DbSeeder.SeedAsync` against whatever
    `DefaultConnection` points at. **Local dev Postgres must be up** (`docker compose up -d`) —
    same precondition as `dotnet run`. All of this is idempotent against an already-migrated/seeded
    dev DB (verified: two consecutive regenerations, "No migrations were applied" both times, no
    duplicate seed data).
- CI wiring was explicitly out of scope for this task (per the brief) — regeneration today is a
  manual one-command step, not gated/enforced anywhere yet.

## Build/test

Touched `Program.cs`, so re-ran both per DoD, both after the final code change:
- `dotnet build ShelfGuard.Api/ShelfGuard.Api.csproj` — 0 errors, 0 warnings.
- `dotnet test` (full solution) — **1685/1685 passed**, 0 failed.

`openapi.json` was regenerated as the final step after build+test, so it reflects the code as
tested (build: 21:51, tests: passed before that final regen; no `.cs` changes after).

## Scope check

`git status` limited to: `backend/ShelfGuard.Api/Program.cs` (modified), `backend/openapi.json`
(new), `backend/.config/` (new — tool manifest). No other files touched.
