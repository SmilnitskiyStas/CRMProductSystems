# Backlog

Tasks waiting to be picked up. Ordered by priority.
Cleaned 2026-06-11: removed TASK-001..007, 009..016, 018, 019, 021..025, 026, 011b, 027..031 — all done (see current.md and .claude/logs/tasks/).

---

## TASK-032: Mobile — verify EAS build + device smoke test
**Status:** planned
**Priority:** critical
**Agent:** mobile-developer + qa-tester
**Dependencies:** none (CNG fix deployed 2026-06-11, commit 491e3e10)
**Notes:** Re-run `eas build -p android --profile preview` after android/ folder removal.
Install APK on device, smoke test: login → dashboard stats → barcode scan → stock list.
API layer exists (auth/dashboard/receipt/stock) — verify screens are wired to production API
(EXPO_PUBLIC_API_URL) and fix anything not connected.

---

## TASK-033: Notifications end-to-end verification (TASK-017 closure)
**Status:** planned
**Priority:** high
**Agent:** devops-engineer + qa-tester
**Dependencies:** none
**Notes:** Worker jobs exist (expiry-check, notification, weekly-report, cleanup) and
shelfguard_worker container runs in production. Verify: (1) expiry-check actually updates
batch statuses hourly against prod DB; (2) notification.job delivers — needs
TELEGRAM_BOT_TOKEN / Resend key in production env; (3) POST /api/notifications/test works
end-to-end. Configure missing env vars.

---

## TASK-034: Fix 2 failing AuthServiceTests
**Status:** planned
**Priority:** medium
**Agent:** backend-developer
**Dependencies:** none
**Notes:** Pre-existing failures (mock token generator returns "" — setup signature
mismatch). LoginAsync_returns_tokens_when_credentials_are_valid +
RefreshAsync_returns_new_tokens_for_valid_refresh_token. Suite should be 189/189 green.

---

## TASK-035: Untrack build artifacts (bin/obj) from git
**Status:** planned
**Priority:** medium
**Agent:** devops-engineer
**Dependencies:** none
**Notes:** backend */bin and */obj directories are tracked in git — every build pollutes
git status with ~70 modified files and already caused a stash conflict (2026-06-11).
Add to .gitignore + `git rm -r --cached`. Pure hygiene, zero runtime impact.

---

## TASK-020: Super Admin provider panel
**Status:** planned
**Priority:** low
**Agent:** backend-developer + frontend-developer
**Dependencies:** none
**Notes:** ProviderController has /health only. v1-spec.md: tenant list, impersonation,
usage stats. Provider RLS bypass policies already in place.

---

# v2 (after v1 release)

Per v2-spec.md — Auto Order + AI Forecasting:
- ADU/CDA calculation engine
- Claude API forecasting client (ShelfGuard.Infrastructure/AI)
- Auto-order suggestions + supplier portal flow
- Open-Meteo weather correlation
