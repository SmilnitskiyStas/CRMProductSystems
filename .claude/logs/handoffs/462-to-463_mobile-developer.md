# TASK-462 → TASK-463 handoff

**From:** mobile-developer  
**To:** security-reviewer + qa-tester  
**Date:** 2026-08-01

Validate `OfflineReadStatus` on Android and iOS phones for only these persisted families:
`schedules`, `marketplace-suppliers`, `production-recipes`. Confirm process-death hydration,
offline current/stale timestamps, hard-expired/no-data behavior, reconnect/refetch clearing,
failed refresh preserving a visible warning, font scaling, screen-reader announcement and retry.

Also prove tenant/user isolation, logout/terminal deletion, corruption/version fail-closed behavior,
storage pressure and backup exclusion. Confirm no cached search/detail/secret/PII/payment/stock/
permission/module data and no offline mutation. Do not infer iOS acceptance from Android.
