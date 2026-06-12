# Backlog

Tasks waiting to be picked up. Ordered by priority.
Rewritten 2026-06-12: v2 (TASK-046..060) fully done — see current.md.

---

## v1 — remaining (none block production demo)

## TASK-042: notification_queue per-channel status accuracy
**Status:** planned · **Priority:** low · **Agent:** backend-developer
Write one row per channel with sent/skipped/failed instead of blanket 'sent'.

---

## Infrastructure polish (Phase 7)

## TASK-043: Domain + HTTPS (Let's Encrypt) + drop cleartext from mobile
**Priority:** high before real clients
API/web on plain http IP:ports; mobile ships usesCleartextTraffic=true as workaround.

## TASK-044: CI (GitHub Actions: build + test on PR), DB backups
**Priority:** medium

---

## Done (recent)
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
