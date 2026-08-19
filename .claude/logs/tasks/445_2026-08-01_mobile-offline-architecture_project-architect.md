# TASK-445 — Mobile offline architecture decision

**Date:** 2026-08-01  
**Agent:** project-architect  
**Status:** done  
**Scope:** mobile architecture and `.claude` documentation only; no mobile/frontend code changed

## Product decisions recorded

- First release: Android and iOS phones.
- Tablet adaptation: deferred.
- Orientation: portrait-only, including POS.
- Preview builds: production API.
- Offline: durable drafts plus limited offline reads.
- Explicit exclusions: no mutation queue, no full offline POS; every business submit is online-only.

## Architecture result

Added accepted ADR-025 to `.claude/docs/decisions.md`. It defines the cached-read allowlist,
staleness timestamps, TTL/retention, tenant+user namespaces, sensitive-data exclusions, React Query
persistence boundary, NetInfo posture, logout/invalidation, online revalidation, timeout/conflict
handling, FEFO authority, POS/fiscal/loyalty limits, Android/iOS storage concerns, privacy-safe
observability, rollout/migration, and rejected alternatives.

Created bounded follow-ups TASK-461..463 in the mobile roadmap. Existing TASK-443/444 behavior is
preserved: uncertain mutations are never blindly retried, and FEFO stays server-authoritative.

## Verification

- Documentation-only scope confirmed; no `mobile/` or `frontend/` file changed by TASK-445.
- Roadmap product checklist and TASK-445 Definition of Done are complete.
- `current.md` and `blocked.md` cross-reference the accepted boundary and follow-ups.
- `git diff --check`: PASS.

## Remaining product decisions

None required for TASK-445. Receipt creation on mobile and future backend idempotency contracts
remain separate TASK-444/TASK-443 product/backend work; tablet/landscape adaptation is intentionally
deferred rather than unresolved for the phone launch.
