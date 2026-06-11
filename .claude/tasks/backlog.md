# Backlog

Tasks waiting to be picked up. Ordered by priority.
Re-audited against v1-spec.md on 2026-06-11 (PM): TASK-020 was stale — ProviderController
has all 8 spec endpoints and /provider page exists; replaced with verification task.

---

## v1 — remaining before release

## TASK-032: Mobile — device smoke test (in progress)
**Status:** in_progress
**Priority:** high
**Agent:** qa-tester + user
**Notes:** Dev build installed, login works, Telegram delivery confirmed. Remaining:
verify on device — scanner (after cssInterop fix), stock list, receipt flow, profile.
User drives the phone; fixes hot-reload via `npx expo start`.

## TASK-038: Provider panel — verify impersonation e2e
**Status:** planned
**Priority:** medium
**Agent:** qa-tester
**Notes:** All 8 /provider endpoints implemented + /provider page exists (backlog was
stale). Verify: tenants list renders, impersonate issues tenant-scoped JWT, actions are
logged, DELETE impersonate restores provider context, modules/plan updates persist.

## TASK-039: Telegram bot account linking (/start flow)
**Status:** planned
**Priority:** medium
**Agent:** backend-developer
**Notes:** v1-spec §8.1. Bot exists (@shelfguard_bot), sending works. Missing: telegraf
service with /start <link-code> → binds users.TelegramChatId automatically (today bound
manually via SQL). Worker has no command listener at all.

## TASK-040: weekly-report.job + cleanup.job implementations
**Status:** planned
**Priority:** low
**Agent:** backend-developer
**Notes:** Both are placeholders. weekly-report: Sunday 08:00 digest per v1-spec §8.3
(email — blocked on Resend key, deferred). cleanup: archive old stock_events/activity_logs.

## TASK-034: Fix 2 failing AuthServiceTests
**Status:** planned
**Priority:** medium
**Agent:** backend-developer
**Notes:** Mock token generator returns "" (setup signature mismatch). Suite must be 189/189.

## TASK-035: Untrack bin/obj from git
**Status:** planned
**Priority:** medium
**Agent:** devops-engineer
**Notes:** ~70 phantom modified files per build; caused a stash conflict on 2026-06-11.

## TASK-041: Web floor-plan constructor (/stores/:id/floor-plan)
**Status:** planned
**Priority:** low (defer to v1.1 unless client demos need it)
**Agent:** frontend-developer
**Notes:** v1-spec §6.4 — dnd-kit canvas, zone drag&drop, zone color = worst batch status.
Only unimplemented web page from the spec.

## TASK-042: notification_queue per-channel status accuracy
**Status:** planned
**Priority:** low
**Agent:** backend-developer
**Notes:** Worker logs status='sent' even when a channel was skipped (no chat_id/token).
Write one row per channel with sent/skipped/failed.

---

## v1 — infrastructure polish (Phase 7, can run parallel to v2)

## TASK-043: Domain + HTTPS (Let's Encrypt) + drop cleartext from mobile
**Priority:** high before real clients
**Notes:** API and web are plain http on IP:ports. Mobile ships with
usesCleartextTraffic=true as a workaround — remove after HTTPS.

## TASK-044: CI (GitHub Actions: build + test on PR), DB backups
**Priority:** medium

---

# v2 (next phase) — per v2-spec.md

Auto Order + AI Forecasting:
- ADU/CDA calculation engine
- Claude API forecasting client (ShelfGuard.Infrastructure/AI)
- Auto-order suggestions + supplier flow
- Open-Meteo weather correlation

Email channel (Resend) — deferred by user, revisit during v2.
