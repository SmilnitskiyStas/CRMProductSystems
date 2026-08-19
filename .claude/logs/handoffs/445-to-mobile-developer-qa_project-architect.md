# TASK-445 handoff — limited offline reads

**From:** project-architect  
**To:** mobile-developer, security-reviewer, qa-tester  
**Date:** 2026-08-01  
**Authority:** ADR-025

## Implement next

1. **TASK-461:** explicit React Query persistence allowlist; versioned tenant+user namespaces;
   timestamps/TTL/size bounds; fail-closed migration; NetInfo-driven UX; logout cleanup.
2. **TASK-462:** catalog, schedules and marketplace first; then individually approved read models.
   Every cached screen shows offline/stale status and last successful update time.
3. **TASK-463:** Android+iOS process-death, account-switch, reconnect, storage/backup and privacy QA.

## Hard constraints

- Never persist the entire React Query cache.
- Never cache auth/payment secrets, TOTP/recovery/challenge values, rotating loyalty codes or
  unrestricted PII.
- Never use cached stock, price, shift, loyalty, permission, module or fiscal state to authorize a
  business operation.
- No offline mutation queue/background replay/full offline POS.
- Every business submit requires connectivity plus fresh server revalidation; FEFO stays on server.
- Timeout/no response remains `uncertain`; no blind retry until an idempotency/reconciliation
  contract is implemented separately.
- Launch matrix is Android+iOS phones in portrait; tablet/landscape work is deferred.

## QA acceptance anchor

Prove stale timestamps, TTL expiry, safe reconnect, corrupt/version mismatch cleanup, process death,
same-owner restore, cross-owner non-disclosure, logout deletion, telemetry redaction and absence of
offline requests. Record Android and iOS independently; do not infer iOS from Android.
