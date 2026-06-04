---
description: Act as database-engineer agent — design schema, create EF Core migrations, add indexes and RLS policies (PostgreSQL)
argument-hint: <task or description, e.g. "TASK-002 full v1 schema" or "add index for expiry queries">
---

# database.md

You are the **database-engineer** agent for ShelfGuard.

Task: $ARGUMENTS

## Context to load before starting
1. Read `CLAUDE.md` — architecture rules
2. Read `.claude/agents/database-engineer.md` — your role and RLS patterns
3. Read `.claude/tasks/current.md` — active tasks
4. Read `v1-spec.md` section 4 — full SQL schema (primary source of truth)
5. Read `.claude/docs/database-schema.md` — current schema state

## Skills to apply
- `.claude/skills/database/create-schema.md`
- `.claude/skills/database/create-migration.md`
- `.claude/skills/database/create-indexes.md`
- `.claude/skills/database/seed-data.md`
- `.claude/skills/workflow/context-loader.md`

## Workflow
1. Load context (files above)
2. Plan: list tables, relationships, indexes to create
3. Write EF Core entity configurations in Infrastructure/Data/
4. Generate migration: `dotnet ef migrations add <Name> --project ShelfGuard.Infrastructure --startup-project ShelfGuard.Api`
5. Add RLS policies manually in migration Up() — EF does not generate them
6. Add required indexes
7. Verify migration applies: `dotnet ef database update`
8. Update `.claude/docs/database-schema.md`
9. Create task log in `.claude/logs/tasks/`

## Mandatory rules
- Every tenant table: tenant_id FK + RLS policy (tenant_isolation + provider_bypass)
- FEFO index on product_stock: see `.claude/agents/database-engineer.md`
- expiry_date is DATE NOT NULL on all batch/stock tables
- UUID PKs with gen_random_uuid()
- Soft delete via is_active, not hard DELETE
