# Skill: Review Completed Task

## Purpose
Визначає процес рев'ю після завершення задачі.

## Review Checklist

### Code Quality
- Code відповідає архітектурним правилам з CLAUDE.md
- Немає бізнес-логіки в контролерах
- Типи правильні, немає any в TypeScript
- Немає закоментованого коду

### Tests
- Unit тести написані для нової бізнес-логіки
- Тести покривають не тільки happy path
- dotnet test або npm test проходить

### Security
- Tenant ID береться з JWT, не з body
- Авторизація на всіх ендпоінтах
- Вхідні дані валідуються

### Domain Rules
- FEFO логіка дотримана (де застосовно)
- expiry_date не змінюється при переміщенні
- RLS застосовано на нових таблицях

### Documentation
- .claude/docs/ оновлено якщо змінилась архітектура або доменна модель
- Task log створено в .claude/logs/tasks/

## Review Log Location
.claude/logs/reviews/TASK-ID_YYYY-MM-DD_review.md
