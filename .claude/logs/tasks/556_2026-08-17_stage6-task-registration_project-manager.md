# TASK-556 — Register Stage A–F implementation tasks (TASK-527–555)

**Agent:** project-manager
**Date:** 2026-08-17
**Depends:** TASK-526 (done — audit + `docs/architecture/CURRENT_STATE.md` /
`TARGET_ARCHITECTURE.md`)
**Status:** done

## What this was

Documentation/task-tracking only. TASK-526 (`project-architect`) audited the backend/web
against the new `docs/` spec files and produced `TARGET_ARCHITECTURE.md` §3 — a proposed
(not registered) breakdown of 29 candidate tasks (TASK-527–555) across Stage A–F, gated on
3 open decisions. This task resolves those 3 decisions and registers the tasks in
`.claude/tasks/mobile-roadmap.md` as `planned`.

## Decisions resolved

1. **`UserTenant` shape** — kept coupled to loyalty. `LoyaltyMembership` remains the sole
   join mechanism; no new generic `UserTenant`/`ConsumerTenantMembership` entity. Accepted
   consequence: a tenant without `loyalty` enabled has no consumer-app presence. Effect:
   TASK-529/530 descoped (recorded as one-liners in the roadmap for traceability, not
   registered as active tasks). TASK-548 reworded to generalize the existing
   `LoyaltyMembership`-based network endpoints (`GET /api/consumer/loyalty/networks`,
   `POST .../join`) into `GET /api/v1/retailers[/{slug}]` + join/leave, keeping the
   loyalty-module gate as-is.
2. **API versioning scope** — version only new consumer-platform endpoints under `/api/v1/`
   (Stage B onward). The existing live API surface is not retroactively versioned/aliased.
   Resolved by the orchestrating session, explicitly authorized by the user. Recorded in
   TASK-551.
3. **Audit log reuse** — reuse the existing generic `ActivityLog` table for the new
   config/publish/rollback/feature-flag events; no new audit table. Recorded in TASK-550.

## What was registered

`.claude/tasks/mobile-roadmap.md`, Stage 6 section:

- TASK-527, TASK-528 — Stage A (multi-tenant/identity foundation), both `planned`, no
  dependency on the open decisions — ready to start immediately.
- TASK-529, TASK-530 — recorded as descoped one-liners (decision 1), not registered as
  active tasks.
- TASK-531–534 — Stage B (Mobile Configuration domain & API).
- TASK-535–542 — Stage C (Retailer Admin: Theme, App Builder, Pages, Navigation).
- TASK-543–547 — Stage D (Feature flags, Draft/Preview/Publish, Versioning).
- TASK-548–550 — Stage E (retailer discovery, QR onboarding, audit) — TASK-548 and
  TASK-550 reworded per decisions 1 and 3.
- TASK-551–555 — Stage F (API versioning, OpenAPI/docs, isolation-test suite, security
  review, subscription ADR) — TASK-551 reworded per decision 2.

27 tasks registered as `planned` total (TASK-527, TASK-528, TASK-531–555). Every entry
follows the roadmap's existing Status/Agent/Priority/Depends/Context/Scope/Definition-of-Done
format (matched against TASK-434–TASK-463's style). Agent and Depends columns taken directly
from `TARGET_ARCHITECTURE.md` §3's table except for the three reworded tasks above. Definition
of Done bullets were expanded from each task's one-line scope in the audit table.

Stage 6 header's `**Stage status:**` line updated to
`decisions_resolved_stage_a_planned`, noting all 3 decisions resolved and pointing to
TASK-527/528 as the next work.

## Files changed

- `.claude/tasks/mobile-roadmap.md` — Stage 6 section: TASK-556 entry, decisions record,
  27 registered `planned` tasks, updated Stage status line.
- `.claude/tasks/current.md` — short pointer added noting Stage 6 implementation is ready
  to start, referencing the roadmap.
- `.claude/logs/tasks/556_2026-08-17_stage6-task-registration_project-manager.md` — this file.

No `backend/`, `frontend/`, `mobile/`, or migration changes — verified via `git status`.

## Next

TASK-527 (`database-engineer`) and TASK-528 (`backend-developer`) start Stage A. They have
no dependency on each other or on any of the three now-resolved decisions.
