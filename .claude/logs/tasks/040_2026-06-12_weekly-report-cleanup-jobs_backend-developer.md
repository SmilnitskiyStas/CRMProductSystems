---
task_id: TASK-040
date: 2026-06-12
agent: backend-developer
status: done
---

# TASK-040 — weekly-report.job + cleanup.job implementations

Both worker jobs were placeholders (console.log only). Cron schedules already
existed in `worker/src/index.ts` (Sun 08:00 / daily 03:00) — only handlers added.

## cleanup.job (`worker/src/jobs/cleanup.job.ts`)
Per v1-spec §8.3 + backlog note. Runs as privileged role (`SET app.role='worker'`):
1. `product_stock`: sold_out → archived after 30 days (by `LastCheckedAt`)
2. `notification_queue`: delete non-pending rows older than 90 days
3. `stock_events`: delete rows older than 180 days
4. `activity_logs`: delete rows older than 180 days

`stock_movements` intentionally never purged (financial audit trail).
Retention overridable via env: `CLEANUP_SOLD_OUT_DAYS`, `CLEANUP_NOTIFICATION_DAYS`,
`CLEANUP_STOCK_EVENTS_DAYS`, `CLEANUP_ACTIVITY_LOGS_DAYS`.

## weekly-report.job (`worker/src/jobs/weekly-report.job.ts`)
Per v1-spec §8.2 (recipients: store_manager, network_manager, enterprise_admin).
Per tenant with active recipients:
- Week stats: new batches (7d), write-offs count + total loss (7d)
- Current shelf state: safe / warning / critical / expired counts (Quantity > 0)
- Respects `notification_settings` (EventType `weekly_report`); no rows → defaults
  to email + telegram
- Delivery: Telegram (HTML) works now; email path uses `sendEmail` which skips
  gracefully while RESEND_API_KEY is unset (deferred by user)
- Logs one `notification_queue` row per recipient (EventType `weekly_report`)

## Verification
- `npx tsc --noEmit` in /worker — clean
- Live cron e2e not run: local Docker down, prod deploy out of scope here.
  Manual trigger when deployed: `weeklyReportQueue.add("weekly-report", {})` /
  `cleanupQueue.add("cleanup", {})` from `worker/src/queues/index.ts`.

## Follow-ups
- When RESEND_API_KEY arrives: email channel works without code changes
- TASK-042 (per-channel notification_queue rows) also applies to weekly_report rows
