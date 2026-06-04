# Skill: Update Task Status

## Purpose
Визначає правила зміни статусів задач.

## Task States

```
planned -> in_progress -> review -> done
planned -> blocked
blocked -> in_progress (після вирішення блокера)
review -> in_progress (якщо знайдено проблеми)
```

## Files to Update

| Status change | File to update |
|---|---|
| -> in_progress | перемістити з backlog.md до current.md |
| -> review | оновити статус у current.md |
| -> done | перемістити з current.md до done.md |
| -> blocked | додати до blocked.md з причиною |

## Task Format

**Status:** planned / in_progress / review / done / blocked
**Agent:** [assigned agent]
**Priority:** critical / high / medium / low
**Dependencies:** TASK-YYY (або none)
**Updated:** YYYY-MM-DD

## Rules
- Оновлювати статус одразу при зміні стану
- Завжди вказувати дату оновлення
- Блокер = конкретна причина + хто може вирішити
