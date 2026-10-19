# TASK-728 — Worker: shelf.empty alerts

- New `worker/src/jobs/shelf-alert.job.ts`: cron `* * * * *`, `SET app.role='worker'`, in one transaction `UPDATE shelf_scans SET AlertPending=false ... FOR UPDATE SKIP LOCKED ... RETURNING` (LIMIT 200), enqueues `shelf_empty` on `notifications` (jobId `shelf-empty-<scanId>`, attempts 3). Enqueue failure -> ROLLBACK, flag stays, next run retries. Missing/low item names parsed from `ResultJson.items[]` (status missing|low).
- `notification.job.ts`: `ShelfEmptyPayload` + `handleShelfEmpty` — roles merchandiser/store_manager/network_manager/enterprise_admin; channels telegram+email (default when no settings row), per-user `notification_settings` for `shelf.empty` honored; logged via `logNotifications`. Location/zone names via locations + location_zones.
- `index.ts`: scheduler + worker registered.
- Build: `npm run build` (tsc) OK. No worker test setup -> no unit test. Not run against a live DB/Redis.
- Frontend `NotificationEventType` / i18n (`shelf.empty`) NOT added: needs messages/uk.json + en.json (shared high-churn files, out of scope).
