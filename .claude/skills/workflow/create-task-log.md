# Skill: Create Task Log

## Purpose
Стандартизує створення task log файлу після завершення роботи.

## File Location
```
.claude/logs/tasks/
```

## File Naming
```
TASK-ID_YYYY-MM-DD_short-description_agent.md
```
Приклад: `001_2026-06-03_products-api_backend-developer.md`

## Template
```markdown
# TASK-XXX: [Title]

**Date:** YYYY-MM-DD
**Agent:** [agent name]
**Status:** done
**Duration:** [estimate]

## What was done
[Short description of completed work]

## Files changed
- `path/to/file.cs` — [what changed]
- `path/to/file.ts` — [what changed]

## Decisions made
[Any decisions taken during implementation]

## Tests
- [ ] Unit tests written
- [ ] Build passes
- [ ] Manual test passed

## Notes
[Anything important for the next agent]
```

## When to Create
- Після завершення кожної задачі (status → done)
- Навіть якщо задача маленька — лог обов'язковий
