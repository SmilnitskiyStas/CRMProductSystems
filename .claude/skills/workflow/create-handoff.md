# Skill: Create Handoff

## Purpose
Стандартизує передачу роботи між агентами.

## File Location
```
.claude/logs/handoffs/
```

## File Naming
```
TASK-ID_YYYY-MM-DD_from-agent_to-agent.md
```
Приклад: `001_2026-06-03_backend-developer_qa-tester.md`

## Template
```markdown
# Handoff: TASK-XXX

**Date:** YYYY-MM-DD
**From:** [agent]
**To:** [agent]
**Task:** [title]

## What was completed
[Summary of work done]

## What needs to be done next
1. [Specific action item]
2. [Specific action item]

## Important context
[Anything the next agent needs to know]

## Risks / Blockers
[Known risks or potential blockers]

## Files to review
- `path/to/file` — [why important]

## Definition of done for next agent
- [ ] [Criteria 1]
- [ ] [Criteria 2]
```

## Rules
- Handoff обов'язковий якщо задача переходить до іншого агента
- Next agent читає handoff перед початком роботи
- Якщо є блокер — handoff до `project-manager`, не до наступного агента
