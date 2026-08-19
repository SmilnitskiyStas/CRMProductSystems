# TASK-461 handoff — offline read UX and cross-platform acceptance

**From:** mobile-developer  
**To:** TASK-462 mobile-developer; TASK-463 security-reviewer + qa-tester  
**Date:** 2026-08-01

TASK-461 provides the persisted-data boundary only. Use `getOfflineReadMetadata(queryKey)` for
`lastSyncedAt` and soft-stale state. TASK-462 may roll out only the currently approved list keys:
`schedules`, `marketplace-suppliers`, and `production-recipes`. Every cached surface must visibly say
offline/stale and show last update time; missing metadata means no usable offline data.

Do not widen the allowlist without a field-level serializer, TTL decision, tests and documentation.
Never use cached data to enable or parameterize submit. Stock/FEFO, prices, shifts, loyalty, modules,
permissions, fiscal/payment and every business mutation remain server-authoritative and online-only.

TASK-463 must independently prove Android and iOS process-death hydration, same-owner recovery,
cross-owner non-disclosure, logout/terminal cleanup, corrupt/version migration, hard retention,
storage pressure, backup exclusion and absence of secret/payment/rotating-code data. Do not infer an
iOS result from Android.
