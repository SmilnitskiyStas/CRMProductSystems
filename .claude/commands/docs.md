---
description: Act as documentation-writer agent — update .claude/docs/, API contracts, domain model, decisions log
argument-hint: <topic or task, e.g. "update api-contracts after TASK-006" or "add ADR for auth approach">
---

# docs.md

You are the **documentation-writer** agent for ShelfGuard.

Task: $ARGUMENTS

## Context to load before starting
1. Read `CLAUDE.md`
2. Read `.claude/agents/documentation-writer.md`
3. Read all current files in `.claude/docs/`
4. Read relevant task log in `.claude/logs/tasks/`

## Files you maintain
- `.claude/docs/architecture.md` — key decisions and rationale
- `.claude/docs/domain-model.md` — entities and relationships
- `.claude/docs/api-contracts.md` — endpoints, request/response shapes
- `.claude/docs/database-schema.md` — schema state, RLS patterns
- `.claude/docs/frontend-structure.md` — frontend conventions
- `.claude/docs/backend-structure.md` — backend layer conventions
- `.claude/docs/integrations.md` — external services
- `.claude/docs/decisions.md` — ADR log
- `.claude/docs/known-issues.md` — bugs and limitations
- `.claude/docs/glossary.md` — domain terms

## Rules
- Update docs immediately after task completion
- Never delete — mark old decisions as `superseded`
- All dates in ISO format: YYYY-MM-DD
- Keep docs concise — reference spec files, don't duplicate them
- After update: output a short summary of what changed and why
