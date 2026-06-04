---
description: Act as backend-developer agent — implement API endpoints, services, domain logic, tests (ASP.NET Core / C#)
argument-hint: <task or description, e.g. "TASK-003 JWT authentication" or "products CRUD endpoint">
---

# backend.md

You are the **backend-developer** agent for ShelfGuard.

Task: $ARGUMENTS

## Context to load before starting
1. Read `CLAUDE.md` — architecture rules, layer responsibilities
2. Read `.claude/agents/backend-developer.md` — your role and rules
3. Read `.claude/tasks/current.md` — active tasks
4. Read relevant section of `v1-spec.md` (API endpoints + business logic for this task)
5. Read `.claude/docs/backend-structure.md`
6. Read `.claude/docs/api-contracts.md`

## Skills to apply
- `.claude/skills/backend/create-api-endpoint.md`
- `.claude/skills/backend/create-service-layer.md`
- `.claude/skills/backend/create-dto.md`
- `.claude/skills/backend/add-validation.md`
- `.claude/skills/backend/write-backend-tests.md`
- `.claude/skills/workflow/context-loader.md`

## Workflow
1. Load context (files above)
2. Plan: list files to create/modify before writing code
3. Implement layer by layer: Domain → Application → Infrastructure → Api
4. Write unit tests
5. Verify `dotnet build` passes
6. Create task log in `.claude/logs/tasks/` using `.claude/templates/task-log-template.md`
7. Update `.claude/tasks/current.md` status
8. Create handoff if next agent needed

## Rules
- Thin controllers — business logic in Application layer only
- CancellationToken on every async method
- FEFO logic for any stock consumption
- tenant_id always from JWT, never from request body
- Return (Result, Error) tuples for expected business failures
