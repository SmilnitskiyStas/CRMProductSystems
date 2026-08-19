# TASK-526 — Backend & Web App Builder repository audit

**Agent:** project-architect
**Date:** 2026-08-17
**Status:** done

## Scope

Audited `backend/` (Domain/Application/Infrastructure/Api) and `frontend/` against
`docs/CLAUDE CODE SPEC — Web Admin, App Builder & Backend.md` ЕТАП 0-18. `mobile/` excluded per
task brief (owned by the parallel Codex mobile workstream — its own same-day audit,
`docs/mobile/MOBILE_CURRENT_STATE.md`/`STAGE_0_REPORT.md`, was read and cross-referenced but not
touched).

## What was produced

- `docs/architecture/CURRENT_STATE.md` — inventory of what already exists: `Tenant` root entity +
  RLS isolation, `ConsumerAccount` global identity, unified dual-token mobile auth
  (`MobileAuthController`), TOTP cross-tenant loyalty code, `LoyaltyMembership` join mechanism,
  `Banner` Draft→Publish lifecycle, anonymous public consumer-content browsing
  (`ConsumerContentController`), `/consumer-app` admin area, `RequireModuleAttribute`/`Tenant.Modules`
  feature-flag precedent, Swagger already wired but unpublished. Cites actual file paths and
  entity/controller names throughout, not generic descriptions.
- `docs/architecture/TARGET_ARCHITECTURE.md` — ЕТАП-by-ЕТАП (1-18, plus 27-31) gap table against
  CURRENT_STATE, each item tagged done/partially shipped/not started with evidence. Includes a
  proposed (not registered) breakdown of 29 candidate follow-up tasks (TASK-527 through TASK-555)
  across 6 stages, with agent assignment per CLAUDE.md's Agent→Task table and dependency order, plus
  3 explicit open decisions flagged for the user before implementation starts.
- `.claude/docs/decisions.md` — new **ADR-029**, recording (1) the confirmed Tenant-mapping decision
  and (2) the open, deliberately-unresolved `UserTenant`/`LoyaltyMembership` coupling question.

## Tenant-mapping conclusion

**Confirmed with evidence: the spec's "Tenant" is ShelfGuard's existing `tenants` table — not a
new, parallel entity.** `Tenant.cs`'s existing fields already satisfy the spec's minimal model
(only `LogoUrl`/`UpdatedAt` are missing, additive); `TenantConnectionInterceptor` and the canonical
RLS `tenant_isolation`/`provider_bypass`/`worker_bypass` triad already enforce the spec's isolation
requirement for every tenant table; `Banner` (the newest tenant-scoped entity, added before the
spec existed) already uses a plain `TenantId` FK to `tenants` with the standard shape, not a new
model. Recorded as ADR-029 point 1.

One related question was **not** silently resolved: the spec's generic `UserTenant` (customer
joined a retailer, independent of loyalty) has no shipped equivalent — `LoyaltyMembership` is the
only join mechanism today and is loyalty-module-coupled (a tenant without `loyalty` enabled cannot
appear in retailer discovery or be joined at all). Recorded as ADR-029 point 2 and as open decision
#1 in `TARGET_ARCHITECTURE.md` §3 — flagged for the user/orchestrator, not decided unilaterally.

## Proposed next-stage tasks

29 candidate tasks, `TASK-527`–`TASK-555`, in `docs/architecture/TARGET_ARCHITECTURE.md` §3:

- **Stage A** (ЕТАП 1-2, identity foundation): TASK-527–530
- **Stage B** (ЕТАП 3-4, Mobile Configuration domain + API): TASK-531–534
- **Stage C** (ЕТАП 5-9, Retailer Admin/Theme/App Builder/Navigation): TASK-535–542
- **Stage D** (ЕТАП 10-13, Feature Flags/Draft-Preview-Publish/Versioning): TASK-543–547
- **Stage E** (ЕТАП 14-17, discovery/QR onboarding/audit): TASK-548–550
- **Stage F** (cross-cutting: versioning/OpenAPI/docs/tests/subscription-readiness): TASK-551–555

Plus 3 open decisions to resolve first: `UserTenant` shape, API-versioning scope (new endpoints
only vs. whole API), audit-log reuse (`ActivityLog`) vs. a dedicated table.

Not registered in `.claude/tasks/mobile-roadmap.md` — per the task brief, that is left to the
orchestrating session after user review.

## Verification

- No business code, EF Core migrations, or `mobile/` changes made.
- `git status` confirms the only changes attributable to this task are: `docs/architecture/CURRENT_STATE.md`
  (new), `docs/architecture/TARGET_ARCHITECTURE.md` (new), `.claude/docs/decisions.md` (79 insertions,
  1 line changed — new ADR-029 + "Updated" date), and this task log. All other working-tree changes
  (mobile/, backend/, frontend/ modifications, other untracked docs/logs) pre-date this task and were
  left untouched.
- Documentation-only task — no build/lint/test run required or applicable.

## Handoff

None — this is a planning/audit deliverable. Next step is for the orchestrating session to review
the proposed task breakdown with the user and, if approved, register the chosen tasks in
`.claude/tasks/mobile-roadmap.md` and update TASK-526 to `done`.
