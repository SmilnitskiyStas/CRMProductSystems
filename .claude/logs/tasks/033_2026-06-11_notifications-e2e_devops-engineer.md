---
task_id: TASK-033
date: 2026-06-11
agent: devops-engineer + qa-tester
status: done (delivery tokens pending — user action)
---

# TASK-033 — Notifications end-to-end

## Found: the pipeline was broken in 5 places

1. **DATABASE_URL format** — server `.env` had the .NET connection string
   (`Host=...;Database=...`), shared with the API. node-pg can't parse it
   (resolved host "base" out of "Data**base**") → every job failed with
   `getaddrinfo EAI_AGAIN base`.
   **Fix:** separate `WORKER_DATABASE_URL` (postgres:// format, localhost:5434),
   built server-side from existing env components; compose passes it as the
   worker's `DATABASE_URL`.

2. **snake_case SQL vs PascalCase columns** — all worker queries used
   `tenant_id`-style names; EF Core created `"TenantId"`-style.
   **Fix:** rewrote all SQL in `expiry-check.job.ts` and `notification.job.ts`
   with quoted PascalCase + `AS snake_case` aliases. `notification_queue`
   INSERT also adds required `"RetryCount"`.

3. **Shared Redis = BullMQ queue-name collision** — ShelfGuard worker used
   `localhost:6379`, which is another project's Redis (trading), also consumed
   by `workmate-worker` (PM2). That worker stole ShelfGuard's `expiry-check`
   jobs (2 workers attached to the queue).
   **Fix:** dedicated `shelfguard_redis` container on host port **6380** +
   `REDIS_URL=redis://localhost:6380`.

4. **DATE handling** — pg returns `DATE` as a JS `Date`; code did
   `row.expiry_date + "T00:00:00Z"` → `Invalid Date` → `NaN` daysLeft →
   **all 25 batches marked "safe"** on first successful run.
   **Fix:** `"ExpiryDate"::text` in SQL. Next run restored real statuses.

5. **Duplicate scheduler** — legacy `expiry-check-hourly` scheduler id in Redis
   fired the job twice hourly. **Fix:** `removeJobScheduler` on startup
   (moot after the Redis switch, kept as guard).

## Verified on production
```
[expiry-check] processed 25 batches — updated: 14/25
statuses recomputed live: 1 critical / 6 expired / 18 safe
notification_queue: 23 rows (product.critical ×5, product.expired ×18), status=sent
[notifications] jobs completed without errors
```

## Remaining — user actions for live delivery
- `TELEGRAM_BOT_TOKEN` (create bot via @BotFather) → server `.env`; users need
  `TelegramChatId` filled.
- `RESEND_API_KEY` + verified sender domain → server `.env`.
- Until then sends are skipped with a console warn (graceful).

## Follow-up (minor)
- notification_queue rows are logged `status='sent'` even when channel send was
  skipped for missing token — should be `skipped`/`failed` per channel.
- Worker thresholds (warning ≤3d, critical ≤1d) now own the Status column;
  seed statuses were hardcoded and are overwritten hourly — expected.
