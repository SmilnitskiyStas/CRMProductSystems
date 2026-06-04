# TASK-000: Initial project setup and multi-agent infrastructure

**Date:** 2026-06-03
**Agent:** project-manager (lead) + devops-engineer + documentation-writer
**Status:** done
**Duration:** 1 session

## What was done

Completed the remaining items of TASK-000 to bring the project infrastructure to a fully ready state for development:

**devops-engineer:**
- Added `redis:7-alpine` service to `docker-compose.yml` (port 6380:6379, volume, healthcheck)
- Added `worker` service to `docker-compose.yml` (builds from ./worker, depends on postgres + redis)
- Created `/worker` BullMQ scaffold:
  - `package.json` — bullmq, ioredis, tsx, typescript
  - `tsconfig.json`
  - `Dockerfile`
  - `src/redis.ts` — shared IORedis connection
  - `src/queues/index.ts` — exports all 4 queue instances
  - `src/index.ts` — main entry, cron schedulers, starts all workers
  - `src/jobs/expiry-check.job.ts` — placeholder, TODO TASK-008
  - `src/jobs/notification.job.ts` — placeholder, TODO TASK-017
  - `src/jobs/weekly-report.job.ts` — placeholder, TODO TASK-017
  - `src/jobs/cleanup.job.ts` — placeholder

**documentation-writer:**
- Updated `.claude/docs/architecture.md`: replaced `CRM.*` layer names with `ShelfGuard.*`, added Worker/BullMQ queue table
- Added ADR-005 to `.claude/docs/decisions.md`: worker scaffold in TASK-000 rationale
- Updated ADR-004: added Redis port mapping (6380)

**project-manager:**
- Moved TASK-000 to `done.md`
- Cleared `current.md`

## Files changed

- `docker-compose.yml` — added redis + worker services
- `worker/package.json` — new
- `worker/tsconfig.json` — new
- `worker/Dockerfile` — new
- `worker/src/redis.ts` — new
- `worker/src/queues/index.ts` — new
- `worker/src/index.ts` — new
- `worker/src/jobs/expiry-check.job.ts` — new (placeholder)
- `worker/src/jobs/notification.job.ts` — new (placeholder)
- `worker/src/jobs/weekly-report.job.ts` — new (placeholder)
- `worker/src/jobs/cleanup.job.ts` — new (placeholder)
- `.claude/docs/architecture.md` — ShelfGuard.* names, Worker section
- `.claude/docs/decisions.md` — ADR-004 updated, ADR-005 added
- `.claude/tasks/current.md` — cleared
- `.claude/tasks/done.md` — TASK-000 added

## Decisions made

- ADR-005: Worker scaffold in TASK-000 so docker-compose and backend know queue names before TASK-008
- DB name stays `crm` until TASK-001 renames backend projects
- Redis host port 6380 (avoids conflict with local Redis on 6379)

## Tests

- Unit tests written: no (scaffold only, no logic)
- Build passes: not verified (npm install not run — expected to pass)
- Manual test: n/a

## Notes for next agent

TASK-001 (backend-developer): rename all `CRM.*` → `ShelfGuard.*` projects and update connection strings.
Queue names for BullMQ producers in the backend: `expiry-check`, `notifications`, `weekly-report`, `cleanup`.
Redis connection from .NET: `StackExchange.Redis` pointing to `localhost:6380` (local) or `redis:6379` (Docker).
