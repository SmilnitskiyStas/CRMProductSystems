# TASK-008: Expiry cron job (BullMQ, hourly)

**Agent:** backend-developer
**Date:** 2026-06-07
**Status:** done

## What was done

Implemented `worker/src/jobs/expiry-check.job.ts` — hourly BullMQ job that scans all active
product_stock batches, updates expiry statuses, and enqueues notification jobs for threshold crossings.

### Logic
- Runs every hour via cron scheduler already configured in `index.ts` (`0 * * * *`)
- Queries all `product_stock` rows where `quantity > 0`
- Computes `days_left = expiry_date - today` (UTC midnight comparison)
- Status rules:
  - `days_left < 0`  → `expired`
  - `days_left <= 1` → `critical`
  - `days_left <= 3` → `warning`
  - otherwise        → `safe`
- Updates `status` and `last_checked_at` for changed rows
- Sets `notified_warning_at` / `notified_critical_at` once per batch (idempotent re-run)
- Enqueues `expiry-alert` job to `notifications` queue with full context (tenantId, storeId, productId, batchNumber, status, daysLeft, quantity)

### Infrastructure fix
- Replaced standalone `ioredis` package with BullMQ's bundled ioredis (connection type conflict)
- `redis.ts` now exports `ConnectionOptions` plain object parsed from `REDIS_URL`

### New file
- `worker/src/db.ts` — PostgreSQL Pool using `pg` library, reads `DATABASE_URL` env var

## Files changed
- `worker/src/jobs/expiry-check.job.ts` — full implementation (was stub)
- `worker/src/db.ts` — new PostgreSQL client module
- `worker/src/redis.ts` — refactored to use BullMQ ConnectionOptions
- `worker/package.json` — added `pg`, `@types/pg`; removed standalone `ioredis`

## Build
`npx tsc --noEmit` — 0 errors, 0 warnings
