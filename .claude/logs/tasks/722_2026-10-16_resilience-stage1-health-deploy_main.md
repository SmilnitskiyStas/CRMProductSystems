# TASK-722 — Resilience stage 1: health checks, limits, deploy without long downtime

Status: review (not deployed)

## Що зроблено
- API: `/health/live` (без залежностей) і `/health/ready` (перевірка БД, `DbReadinessHealthCheck`), anonymous. Прапор `Migrations:RunOnStartup` (default true).
- HEALTHCHECK у `backend/Dockerfile` (curl), `frontend/Dockerfile` (wget), `worker/Dockerfile` (pgrep -x node).
- `docker-compose.production.yml`: redis healthcheck, worker чекає healthy redis/mosquitto, `mem_limit`/`cpus` (api 1536m/2, web 768m/1, worker 768m/1, redis 384m). Порти 5100/3100 не чіпали.
- `deploy.sh`: build ДО зупинки старих контейнерів, `up -d` без `down`, ghost-cleanup лише як fallback, health-gate замість `sleep 15` (exit 1 при нездоровому сервісі).

## Перевірка
- `dotnet build` OK, `dotnet test` 2467/2467, `bash -n deploy.sh` OK, `docker compose config` OK.
- НЕ перевірено: живий `/health/*` проти реальної БД, Docker-збірка образів, деплой на сервері.

## Відкрито
- Ліміти пам'яті підібрано «з запасом» — звірити з `docker stats` на сервері.
- Staging успадковує HEALTHCHECK з Dockerfile; compose staging не змінювали.
- Далі: етап 2 (Polly/circuit breaker для зовнішніх API).
