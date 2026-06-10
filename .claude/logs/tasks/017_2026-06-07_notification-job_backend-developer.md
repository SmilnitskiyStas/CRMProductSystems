# TASK-017: Notifications — Telegram + Email (BullMQ worker)

**Agent:** backend-developer
**Date:** 2026-06-07
**Status:** done

## What was done

Implemented full `notifications` BullMQ queue worker that sends Telegram and Email alerts
for expiry threshold crossings. Push (Expo) left as placeholder — requires Expo push SDK (v2 scope).

### New files
- `worker/src/services/telegram.ts` — sends messages via Telegram Bot HTTP API (no SDK dep, uses fetch)
- `worker/src/services/email.ts` — sends via Resend SDK; lazy singleton client
- (updated) `worker/src/jobs/notification.job.ts` — full worker implementation

### notification.job.ts logic
1. Receives jobs from `notifications` queue (currently type: `expiry_alert`)
2. Fetches product name + store name from DB for readable messages
3. Queries `users` where: tenant_id match, role in subscription matrix, is_active=true
4. Checks `notification_settings` per user — respects opt-in/opt-out; falls back to role defaults if no settings row exists
5. Sends in parallel: Telegram (if chat_id present) + Email (via Resend)
6. Logs each send to `notification_queue` table (status='sent')
7. Per-channel send errors are caught and logged without failing the whole job

### Subscription matrix (v1-spec 8.2)
| Status   | Roles                                          | Channels               |
|----------|------------------------------------------------|------------------------|
| warning  | merchandiser, store_manager, network_manager, enterprise_admin | telegram, push |
| critical | same                                           | telegram, push, email  |
| expired  | store_manager, network_manager, enterprise_admin | telegram, email      |

### Env vars required
- `TELEGRAM_BOT_TOKEN` — Telegram Bot API token
- `RESEND_API_KEY` — Resend API key
- `FROM_EMAIL` — sender address (default: noreply@shelfguard.app)
- `DATABASE_URL` — PostgreSQL connection string

### Dependencies added
- `resend` npm package

## Build
`npx tsc --noEmit` — 0 errors
