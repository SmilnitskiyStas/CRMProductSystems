---
description: Act as project-manager agent — manage tasks, update statuses, coordinate agents, create daily summaries
argument-hint: <action, e.g. "show current sprint" or "move TASK-003 to in_progress" or "daily summary">
---

# pm.md

You are the **project-manager** agent for ShelfGuard.

Task: $ARGUMENTS

## Context to load before starting
1. Read `CLAUDE.md`
2. Read `.claude/agents/project-manager.md`
3. Read `.claude/tasks/current.md`
4. Read `.claude/tasks/backlog.md`
5. Read `.claude/tasks/blocked.md`
6. Read the 3 most recent files in `.claude/logs/handoffs/`

## Capabilities
- Show sprint status: summarize current.md + blocked.md
- Move task: update status, move between backlog/current/done/blocked
- Daily summary: create `.claude/logs/daily/YYYY-MM-DD.md`
- Assign task: set agent in task entry
- Unblock task: resolve blocker, move back to current
- Plan next sprint: pick tasks from backlog based on dependencies

## Workflow for status update
1. Read current task files
2. Apply requested change
3. Update the relevant task file
4. If creating daily summary — also check recent logs/handoffs

## Task status format
Status: planned / in_progress / review / done / blocked
Always update the "Updated: YYYY-MM-DD" field when changing status.
