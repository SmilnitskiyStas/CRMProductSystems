# TASK-001: Rename CRM.* → ShelfGuard.*

**Date:** 2026-06-03
**Agent:** backend-developer
**Status:** done
**Duration:** 1 session

## What was done

Renamed all backend project directories, .csproj files, namespaces, and solution file from CRM.* to ShelfGuard.*.

## Files changed

- `backend/CRM.sln` → deleted
- `backend/ShelfGuard.sln` → created with updated project paths
- `backend/CRM.Api/` → renamed to `backend/ShelfGuard.Api/`
- `backend/CRM.Application/` → renamed to `backend/ShelfGuard.Application/`
- `backend/CRM.Domain/` → renamed to `backend/ShelfGuard.Domain/`
- `backend/CRM.Infrastructure/` → renamed to `backend/ShelfGuard.Infrastructure/`
- `backend/CRM.Tests/` → renamed to `backend/ShelfGuard.Tests/`
- All 5 `.csproj` files renamed from `CRM.*` to `ShelfGuard.*`
- 19 `.cs` and `.csproj` files: `CRM.` namespace prefix replaced with `ShelfGuard.` in content
- `.claude/docs/backend-structure.md` — updated project naming section

## Decisions made

- Migration files (20260601183332_InitialCreate.cs, Designer, Snapshot) also updated — namespace only, no schema changes
- Database name stays `crm` (not in scope for TASK-001)
- appsettings.json untouched — no CRM. references there

## Tests

- Unit tests written: no (rename only)
- Build passes: yes — `dotnet build` 0 errors, 0 warnings
- Manual test: `dotnet test` 6/6 passed

## Notes for next agent

TASK-002 (database-engineer): implement full v1 schema.
- Solution path: `backend/ShelfGuard.sln`
- EF Core migration command: `dotnet ef migrations add <Name> --project ShelfGuard.Infrastructure --startup-project ShelfGuard.Api`
- Connection string (local dev): `Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password`
- AppDbContext is in `ShelfGuard.Infrastructure/Data/AppDbContext.cs`
- Current migration (InitialCreate) only has the `products` table — full v1 schema from spec needed
