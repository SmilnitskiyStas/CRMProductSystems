# Backlog

Tasks waiting to be picked up. Ordered by priority.
Rewritten 2026-06-12: v2 (TASK-046..060) fully done — see current.md; mobile polish done.

---

## v1 — remaining (none block production demo)

## TASK-035: Untrack bin/obj from git
**Status:** planned · **Priority:** medium · **Agent:** devops-engineer
~70 phantom modified files per build; caused a stash conflict on 2026-06-11.

## TASK-040: weekly-report.job + cleanup.job implementations
**Status:** planned · **Priority:** low · **Agent:** backend-developer
Both placeholders. weekly-report blocked on Resend key (deferred by user).

## TASK-041: Web floor-plan constructor (/stores/:id/floor-plan)
**Status:** planned · **Priority:** low (v1.1) · **Agent:** frontend-developer
v1-spec §6.4 — dnd-kit canvas. Only unimplemented web page from the spec.

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
- TASK-034 auth tests fixed ✅ (2026-06-12) — suite 249/249 green
- TASK-039 Telegram /start linking ✅ (2026-06-12) — deep-link codes + worker listener
- TASK-038 impersonation e2e ✅ PASS 12/12 (2026-06-12) — log in .claude/logs/reviews/
- TASK-032 device smoke ✅ (2026-06-11) · TASK-045 mobile polish ✅ (2026-06-12)
- v2 complete: TASK-046..060 ✅ — logs in .claude/logs/tasks/
- Pending external: Anthropic credits for live AI e2e; Resend key for email channel.
