# Backlog

Tasks waiting to be picked up. Ordered by priority.
Re-audited against v1-spec.md on 2026-06-11 (PM): TASK-020 was stale — ProviderController
has all 8 spec endpoints and /provider page exists; replaced with verification task.

---

## v1 — remaining before release

## TASK-032: Mobile — device smoke test ✅ done (2026-06-11)
**Status:** done
**Result (user-verified on device):** login ✓, dashboard ✓, scanner camera opens and
scans products ✓ (cssInterop fix confirmed). Findings → TASK-045.

## TASK-045: Mobile polish — profile actions + receipt screen wiring
**Status:** planned
**Priority:** medium
**Agent:** mobile-developer
**Notes:** From device smoke (2026-06-11):
1. Profile screen shows auth info but no action works (logout? settings? — wire buttons).
2. Receipt screen shows nothing — production DB has 4 seeded receipts, so either the
   screen isn't wired to GET /receipts or it filters to a status the seeds don't have.
   Diagnose and wire.

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

# v2 — Auto Order + AI Forecasting (per v2-spec.md)

Decomposed 2026-06-11 (PM). Sprint v2.1 (Phase 1 — Data Foundation) moved to current.md.

## Phase 2 — Buffer & Formula
- **TASK-051** (backend): CDA buffer engine — green/yellow/red zones, dynamic lead_time/order_cycle, product_buffer table+migration, GET/POST /buffer endpoints (v2-spec §2)
- **TASK-052** (backend): order formula — Buffer + SafetyBuffer − Stock − InTransit, USQ/MOQ rounding from product_supplier_settings (v2-spec §3)
- **TASK-053** (frontend): buffer funnel indicator per product + basic orders page

## Phase 3 — Events & Weather
- **TASK-054** (backend+db): demand_events + coefficients tables, CRUD API, pre-seeded holidays (v2-spec §4)
- **TASK-055** (backend+devops): Open-Meteo client in Infrastructure/Integrations, weather_data + weather_coefficients, daily fetch cron in worker (v2-spec §6)
- **TASK-056** (frontend): events calendar (week/month view)

## Phase 4 — Promotions & Cannibalization
- **TASK-057** (backend+frontend): promo_cannibalization, auto-generation on discount create, confirm/edit UI, formula impact (v2-spec §5)

## Phase 5 — AI Agent
- **TASK-058** (backend): Claude API client — isolated in ShelfGuard.Infrastructure/AI, prompt template from v2-spec §7, context snapshot builder
- **TASK-059** (backend+devops): ai_order_suggestions(+items) tables, /ai-orders API, BullMQ daily job 05:00, "order ready" notification
- **TASK-060** (frontend): AI order dashboard — review/edit/accept flow, was_edited tracking

Email channel (Resend) — deferred by user, revisit during v2.
