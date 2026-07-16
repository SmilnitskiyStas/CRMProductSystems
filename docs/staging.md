# Staging environment

A fully containerized environment (`docker-compose.staging.yml`) — api, web,
postgres, redis, mosquitto, worker all run in Docker, mirroring production's
topology (unlike local dev, where only postgres/redis/mosquitto run in Docker
and api/frontend run locally on the host).

Purpose: give the pre-launch audit (functional, load, security tests) an
isolated environment to run against, without touching local dev or production.

| | dev (`docker-compose.yml`) | staging (`docker-compose.staging.yml`) | production (`docker-compose.production.yml`) |
|---|---|---|---|
| postgres | 5435 (container), api/web run on host | **5436** (own container) | not in this compose file (`external_links`, separate container) |
| redis | 6380 | **6381** | 6380 (loopback) |
| mosquitto | 1884 | **1885** | 1884 (loopback) |
| api | 5000 (`dotnet run` on host) | **5101** | 5100 |
| web | 3000 (`npm run dev` on host) | **3101** | 3100 |
| seeding | always (Development) | always (`SEED_ON_START=true`) | never by default |

## First-time setup

```bash
cp .env.staging.example .env.staging
# edit .env.staging: fill in POSTGRES_PASSWORD, JWT_SECRET, and matching
# DATABASE_URL / WORKER_DATABASE_URL passwords. Everything else has a
# working default or can stay blank (Claude/ПРРО/Telegram are optional).
```

## Start / stop

```bash
# build + start the full stack in the background
docker compose -f docker-compose.staging.yml --env-file .env.staging up -d --build

# tail logs
docker compose -f docker-compose.staging.yml --env-file .env.staging logs -f

# stop (keeps data)
docker compose -f docker-compose.staging.yml --env-file .env.staging down

# stop and wipe all data (clean slate for the next test run)
docker compose -f docker-compose.staging.yml --env-file .env.staging down -v
```

## Migrations and seed data

No manual migration step is needed. On every boot the `api` container runs
`Database.MigrateAsync()` unconditionally (same as dev/prod), applying any
pending EF Core migrations against the staging postgres container. Because
`SEED_ON_START=true` is set for the `api` service in
`docker-compose.staging.yml`, `DbSeeder.SeedAsync()` also runs on every fresh
boot, populating demo/test data (not a production dump, not real client data)
— see `.claude/docs/known-issues.md` KI-006 for the guard rule.

If you only need to apply migrations manually against staging postgres from
your host (e.g. for a new migration developed against prod-shaped schema):

```bash
cd backend
dotnet ef database update \
  --project ShelfGuard.Infrastructure --startup-project ShelfGuard.Api \
  --connection "Host=localhost;Port=5436;Database=shelfguard_staging;Username=shelfguard_staging;Password=<your .env.staging password>"
```

## Access

- API: http://localhost:5101 (Swagger only if `ASPNETCORE_ENVIRONMENT` is
  changed to `Development` — staging runs as `Staging` by default, matching
  production's config-via-env-vars behavior)
- Web: http://localhost:3101
- Postgres: `localhost:5436` (loopback-only, same as prod's redis/mosquitto)
- Redis: `localhost:6381` · Mosquitto: `localhost:1885`
