# Handoff: TASK-000 → TASK-001

**Date:** 2026-06-03
**From:** project-manager
**To:** backend-developer
**Task:** TASK-001 — Rename CRM.* → ShelfGuard.*

## What was completed

TASK-000 is done. Full project infrastructure is in place:
- `.claude/` multi-agent system, docs, tasks, memory
- `docker-compose.yml` with PostgreSQL (5435) + Redis (6380) + worker service
- `/worker` BullMQ scaffold with 4 job placeholders
- Architecture docs updated to ShelfGuard.* naming

## What to do next

1. Rename solution and all projects: `CRM.sln` → `ShelfGuard.sln`, `CRM.Api` → `ShelfGuard.Api`, `CRM.Application` → `ShelfGuard.Application`, `CRM.Domain` → `ShelfGuard.Domain`, `CRM.Infrastructure` → `ShelfGuard.Infrastructure`, `CRM.Tests` → `ShelfGuard.Tests`
2. Update all `namespace`, `using`, and `ProjectReference` entries throughout the solution
3. Update connection string in `appsettings.json` / `appsettings.Development.json` — DB stays `crm` for now, but host/port should reflect Docker or local setup
4. Verify `dotnet build` passes with zero errors after rename
5. Verify `dotnet test` passes (6 tests currently green)

## Important context

- The existing proof-of-concept products feature (TEST-001) must still compile and pass tests after rename
- Do NOT change database schema or auth — that is TASK-002 and TASK-003
- Docker Compose DB name stays `crm` until you explicitly decide to rename it (not required for TASK-001)
- BullMQ queue names (for future StackExchange.Redis producers): `expiry-check`, `notifications`, `weekly-report`, `cleanup`

## Risks / Blockers

- Global find-replace across .csproj, .cs namespaces is error-prone — verify with `dotnet build` before marking done
- `.sln` file references project paths by folder name — must update those too

## Files to review

- `backend/CRM.sln` — solution file with project references
- `backend/CRM.Api/CRM.Api.csproj` — and all other .csproj files
- `backend/CRM.Api/Program.cs` — namespace entry point
- `.claude/docs/backend-structure.md` — update layer names after rename

## Definition of done

- `dotnet build` exits 0 with ShelfGuard.* namespaces
- `dotnet test` exits 0, all existing tests pass
- No `CRM.` references remain in .cs or .csproj files
- `.claude/docs/backend-structure.md` updated to ShelfGuard.*
