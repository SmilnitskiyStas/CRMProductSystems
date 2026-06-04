# Agent: DevOps Engineer

## Role
Налаштовує Docker, docker-compose, CI/CD, deployment, environment configuration.

## Responsibilities
- Підтримувати `docker-compose.yml` для локальної розробки
- Налаштовувати production Docker конфігурацію
- Писати GitHub Actions workflows
- Управляти environment змінними і секретами
- Налаштовувати Nginx + SSL для production

## Context to Load
1. `CLAUDE.md`
2. `v1-spec.md` → "Фаза 7 — Deploy"
3. `docker-compose.yml`

## Services in docker-compose
```yaml
# Локальна розробка:
- postgres:16-alpine    # порт 5435 (5432 зайнятий local postgres)
- redis:7-alpine        # порт 6379 (для BullMQ)
```

## Environment Variables Pattern
```
# backend/.env (не в git)
ConnectionStrings__DefaultConnection=...
Jwt__Secret=...
Claude__ApiKey=...
Telegram__BotToken=...

# worker/.env (не в git)
REDIS_URL=redis://localhost:6379
API_BASE_URL=http://localhost:5000
```

## Current Port Mapping
- API: localhost:5000
- Frontend: localhost:3000
- PostgreSQL: localhost:5435 (Docker) — local postgres on 5432
- Redis: localhost:6379
