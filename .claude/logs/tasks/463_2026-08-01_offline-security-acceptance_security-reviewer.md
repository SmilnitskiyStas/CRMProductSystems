# TASK-463 — Offline security acceptance

**Date:** 2026-08-01  
**Agent:** security-reviewer  
**Status:** security_review_pass_android / ios_device_build_pending  
**Scope:** mobile and `.claude` only

## Result

- Exact query-key allowlisting now permits only schedule, marketplace supplier and recipe lists;
  search/detail/arbitrary scopes are denied. Serializers discard unexpected secret, PII, payment,
  stock, permission, module and rotating-code fields.
- Cache is schema/environment/tenant/user namespaced. Cross-owner hydration fails closed; switching
  clears memory first. Corrupt/version/foreign/oversized records are rejected; invalid owner pointers
  are deleted; pointer-loss terminal cleanup deletes all offline-read namespaces.
- Seven-day pruning covers inactive owners. Limits remain 256 KiB/entry and 2 MiB/owner; storage
  pressure fails without widening persistence or blocking auth.
- SecureStore contains auth secrets and the cleanup owner pointer. AsyncStorage contains minimized
  summaries and sanitized durable drafts and is not claimed application-encrypted.
- No mutation queue/replay/persisted mutation state exists. POS sale/open/close now perform a fresh
  connectivity guard immediately before mutation and controls are disabled offline. Reconnect only
  invalidates approved reads; successful server refetch remains authoritative.
- No mobile telemetry/logger records payloads, queries, owners or draft values.
- SDK 56 config plugin disables Android backup and legacy full backup and excludes all private domains
  from Android 12+ cloud backup/device transfer. Audio and legacy external-storage permissions are removed.

## Verification

- TypeScript: PASS.
- ESLint: PASS, 0 errors (existing unrelated warnings only).
- Jest: PASS, 28/28 suites and 126/126 tests; final focused security/POS run 11/11 suites, 51/51 tests.
- Expo config/prebuild: PASS; generated manifest/XML inspected.
- Android export: PASS.
- `git diff --check`: PASS.

## Platform verdict

Android code/config security is clear for physical QA. AsyncStorage remains plaintext at the app layer;
minimization, sandboxing and backup exclusion mitigate but do not protect a rooted/unlocked device.
Durable drafts intentionally contain owner-validated operational values and are also backup-excluded.

iOS is not cleared: this Windows workspace has no generated iOS build/device. AsyncStorage backup
exclusion, Keychain accessibility, process death and restore/device-transfer behavior must be proven on
an iOS build/device and must not be inferred from Android.
