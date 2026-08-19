# TASK-462 — Limited offline-read UX rollout

**Date:** 2026-08-01  
**Agent:** mobile-developer  
**Status:** review_pending_device  
**Authority:** ADR-025, TASK-461 handoff

## Implemented

- Added one shared, non-obstructive `OfflineReadStatus` and deterministic UX state model.
- Rolled it out only to the allowlisted schedules list, marketplace supplier list and production
  recipe list. `my-shifts`, search, details and every other query family remain unchanged.
- Ukrainian states distinguish offline cached, online refreshing, current online, soft-stale,
  refresh-failed-but-viewable and no-cache/hard-expired data.
- Cached states show the last successful server timestamp. Fresh successful query metadata removes
  the warning; online stale/failed reads expose an accessible retry.
- Existing cached data stays visible if a refetch fails. No-cache screens retain their existing
  loading/error/empty states.
- The alert uses design-system colors, safe in-flow layout, live accessibility announcement and a
  minimum 44-point retry target. Existing role/module guards and portrait layout are unchanged.

## Boundary

No query allowlist, serializer, TTL, cache payload, mutation, submission rule or server state was
expanded. No stock, price, permissions, module, PII, detail, auth or secret data was added. Cached
data does not authorize or parameterize any business operation; all submits remain online-only.

## Verification

- TypeScript: PASS.
- ESLint: PASS, 0 errors and 12 unrelated existing warnings.
- Jest: PASS, 26/26 suites and 118/118 tests.
- Android Expo export: PASS (`.expo-export-task462`).
- Focused tests cover timestamp formatting, offline cached, stale, online refresh/current, online
  refresh failure, no-cache offline, retry behavior and accessible rendering. Existing lifecycle
  reconnect invalidation and navigation policy tests remain green.
- Physical Android/iOS device verification: not run; owned by TASK-463.

## Next

TASK-463 must perform Android and iOS process-death, reconnect, stale/hard-expiry, owner isolation,
logout, storage/backup and accessibility acceptance. No iOS result is inferred from Android.
