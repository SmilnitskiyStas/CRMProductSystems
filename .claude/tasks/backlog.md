# Backlog

Tasks waiting to be picked up. Ordered by priority.
Rewritten 2026-06-12: v2 (TASK-046..060) fully done — see current.md.

---

## v1 — remaining (none block production demo)

(empty — TASK-040..042 done 2026-06-12)

---

## Infrastructure polish (Phase 7) — deferred by user 2026-06-12

## TASK-043: Domain + HTTPS (Let's Encrypt) + drop cleartext from mobile
**Status:** deferred · **Priority:** high before real clients · Updated: 2026-06-12
API/web on plain http IP:ports; mobile ships usesCleartextTraffic=true as workaround.

## TASK-044: CI (GitHub Actions: build + test on PR), DB backups
**Status:** deferred · **Priority:** medium · Updated: 2026-06-12

---

## Done (recent)
- TASK-042 per-channel notification statuses ✅ (2026-06-12) — log: 042_2026-06-12_notification-per-channel-status_backend-developer.md
- TASK-041 floor-plan constructor ✅ (2026-06-12) — log: 041_2026-06-12_floor-plan-constructor_frontend-developer.md; QA e2e pending
- TASK-040 weekly-report + cleanup jobs ✅ (2026-06-12) — log: 040_2026-06-12_weekly-report-cleanup-jobs_backend-developer.md
- TASK-035 bin/obj untracked ✅ (2026-06-12) — 473 files, git status clean after builds
- TASK-034 auth tests fixed ✅ (2026-06-12) — suite 249/249 green
- TASK-039 Telegram /start linking ✅ (2026-06-12) — deep-link codes + worker listener
- TASK-038 impersonation e2e ✅ PASS 12/12 (2026-06-12)
- TASK-032 device smoke ✅ (2026-06-11) · TASK-045 mobile polish ✅ (2026-06-12)
- v2 complete: TASK-046..060 ✅ — logs in .claude/logs/tasks/
- Pending external: Anthropic credits for live AI e2e; Resend key for email channel.

## Process note (for all agents)
NEVER edit markdown/source files via PowerShell Get-Content/-replace/Set-Content —
it mojibakes UTF-8 (happened 3×). Use the Write/Edit tools.
