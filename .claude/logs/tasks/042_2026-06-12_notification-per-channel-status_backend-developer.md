---
task_id: TASK-042
date: 2026-06-12
agent: backend-developer
status: done
---

# TASK-042 — notification_queue per-channel status accuracy

Before: worker wrote one notification_queue row per user with
Channel="telegram,email" and blanket Status='sent' even when a channel was
skipped (no chat id, RESEND_API_KEY unset) or failed (send threw).

## Changes
| File | What |
|---|---|
| `worker/src/services/telegram.ts` | sendTelegramMessage now returns "sent" \| "skipped" (skipped when no bot token); still throws on API errors |
| `worker/src/services/email.ts` | sendEmail likewise ("skipped" when no RESEND_API_KEY) |
| `worker/src/services/notification-log.ts` | new: `deliver()` maps result/exception → DeliveryOutcome; `logNotifications()` inserts one row per channel, SentAt only for 'sent', Error column on failure/skip-reason |
| `worker/src/jobs/notification.job.ts` | per-channel outcomes: telegram / email / push (push always 'skipped' — not implemented); missing contact → 'skipped' with reason |
| `worker/src/jobs/weekly-report.job.ts` | same per-channel logging; sentCount counts users with ≥1 actually-sent channel |
| `frontend/.../notifications/types.ts` + `NotificationHistoryList.tsx` | history UI: added 'skipped' status badge («Пропущено», gray) |

## Status semantics
- `sent` — provider accepted the message (SentAt set)
- `skipped` — channel not configured (no token/key) or user has no contact / channel not implemented (reason in Error)
- `failed` — send threw (Error = exception message)

## Verification
- `npx tsc --noEmit` clean in /worker and /frontend
- Live e2e pending deployment (same as TASK-040 — manual queue trigger)

## Notes
- sendTelegramMessage return-type change is additive; other callers
  (telegram-listener, ai-order.job) ignore the return value — unaffected.
