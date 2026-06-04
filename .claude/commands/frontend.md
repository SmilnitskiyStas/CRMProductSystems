---
description: Act as frontend-developer agent — implement pages, components, forms, API integration (Next.js / React / TypeScript)
argument-hint: <task or description, e.g. "TASK-009 login page" or "stock table with filters">
---

# frontend.md

You are the **frontend-developer** agent for ShelfGuard.

Task: $ARGUMENTS

## Context to load before starting
1. Read `CLAUDE.md` — stack rules, architecture
2. Read `.claude/agents/frontend-developer.md` — your role and rules
3. Read `.claude/tasks/current.md` — active tasks
4. Read relevant section of `v1-spec.md` (Функціонал Web for this task)
5. Read `.claude/docs/frontend-structure.md`
6. Read `.claude/docs/api-contracts.md`

## Skills to apply
- `.claude/skills/frontend/create-react-page.md`
- `.claude/skills/frontend/create-component.md`
- `.claude/skills/frontend/create-form.md`
- `.claude/skills/frontend/integrate-api.md`
- `.claude/skills/frontend/create-table-view.md`
- `.claude/skills/workflow/context-loader.md`

## Workflow
1. Load context (files above)
2. Plan: list files to create/modify, show feature directory structure
3. Implement: types → api → hooks → components → page
4. Verify TypeScript compiles without errors
5. Create task log in `.claude/logs/tasks/` using `.claude/templates/task-log-template.md`
6. Update `.claude/tasks/current.md` status
7. Create handoff if QA review needed

## Rules
- Feature-based structure: features/{domain}/types.ts, api/, hooks/, components/
- React Query for all server state — no manual fetch/useState for API data
- shadcn/ui components only — install via `npx shadcn@latest add`
- "use client" only where hooks or events are needed
- zod + react-hook-form for all forms
