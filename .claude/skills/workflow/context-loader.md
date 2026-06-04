# Skill: Context Loader

## Purpose
Визначає які файли агент має прочитати ПЕРЕД початком роботи.

## Required Read Order

### 1. Always (будь-який агент)
- `CLAUDE.md` — правила проєкту, стек, архітектура
- `.claude/tasks/current.md` — що зараз в роботі

### 2. Domain context
- `v1-spec.md` — для будь-якої задачі v1.0
- `v2-spec.md` — для задач Auto Order / AI
- `v3-spec.md` — для задач IoT / POS

### 3. Architecture docs
- `.claude/docs/architecture.md`
- `.claude/docs/domain-model.md`
- `.claude/docs/decisions.md` — перед будь-яким архітектурним рішенням

### 4. Task-specific
- `.claude/docs/api-contracts.md` — backend або frontend задачі
- `.claude/docs/database-schema.md` — database задачі
- `.claude/docs/backend-structure.md` — backend задачі
- `.claude/docs/frontend-structure.md` — frontend задачі

### 5. Recent history
- Останні 3 файли в `.claude/logs/handoffs/`
- Task log поточної задачі якщо є

## Anti-patterns
- Не починати роботу без прочитання `CLAUDE.md`
- Не робити архітектурні рішення без перевірки `decisions.md`
- Не писати міграції без перевірки `database-schema.md`
