# TASK-461 — Allowlisted mobile query-cache foundation

**Date:** 2026-08-01  
**Agent:** mobile-developer  
**Status:** review_pending_device  
**Authority:** ADR-025 / TASK-445

## Implemented

- Added a dependency-free React Query persistence adapter over existing AsyncStorage and NetInfo.
- Persisted allowlist is deliberately small: `schedules`, `marketplace-suppliers`, and
  `production-recipes` list summaries. Family-specific serializers discard all unexpected fields.
- Added production/environment + schema + tenant + user namespaces, 24h/6h/24h soft TTL,
  seven-day hard retention, 256 KiB entry and 2 MiB owner limits, plus `lastSyncedAt` metadata.
- Added fail-closed corrupt/version/foreign-owner handling and hard-expiry deletion.
- Hydration waits for a known staff owner before private identity is exposed. Owner switch clears
  in-memory server state immediately; stale async hydration cannot disclose the previous owner.
- Explicit logout and terminal session cleanup remove only the current owner's cache. A SecureStore
  owner pointer covers terminal `/auth/me` rejection before the restored user object is available.
- Successful online query updates persist minimized summaries; reconnect invalidates only allowlisted
  families. No mutation state, queue, replay, offline submit, stock authority or full offline POS was added.
- Documented APIs, exact query keys, exclusions and TASK-462/463 boundaries in
  `mobile/features/offline-read-cache/README.md`.

## Automated acceptance

- TypeScript: PASS.
- ESLint: PASS with 0 errors and 12 unrelated existing warnings.
- Jest: PASS — 24/24 suites, 108/108 tests.
- Android Expo export: PASS.
- Focused policy/storage/lifecycle tests cover allowlist and field exclusions, tenant/user isolation,
  stale and hard TTL, corruption/version mismatch, owner-safe cleanup, hydration race, reconnect and
  online refresh.

## Pending

- TASK-462: expose offline/stale/last-updated UI on the three approved screens.
- TASK-463: Android and iOS process-death, account-switch, backup/storage-pressure and privacy QA.
- iOS was configuration-covered only; no iOS build/device result is claimed here.

## TASK-463 HIGH process-death follow-up

Device QA showed the TASK-437 retry gate made the safe cache unreachable after offline process
death. Added the minimal SecureStore offline-session snapshot and exact-route offline shell described
in the defect log. Owner mismatch, expiry/version/corruption, logout, terminal auth failure and
reconnect promotion fail closed. Offline screens do not call their query APIs or expose search,
details, dashboard, stock, POS or mutation routes.

Post-fix checks: TypeScript PASS; lint 0 errors (12 unrelated warnings); focused 8 suites / 43 tests
PASS; full Jest 29 suites / 136 tests PASS. Per the user's stop request, no new build or device run
was started; the fix is ready for a later controlled Android retest and separate iOS acceptance.
