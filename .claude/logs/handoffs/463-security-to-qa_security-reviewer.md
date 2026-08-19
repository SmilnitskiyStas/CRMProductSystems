# TASK-463 security → QA handoff

Android automated/config security is clear. QA should verify current-source Android process-death
hydration, same-owner recovery, cross-owner non-disclosure, logout/terminal cleanup, stale/hard-expired
UX, storage pressure, reconnect refresh, and zero offline requests for POS/warehouse/production actions.

Do not mark cross-platform done from Android. iOS needs a generated build/device check for backup/restore,
device transfer, AsyncStorage exclusion, SecureStore/Keychain behavior, process death and accessibility.
Never record cache bodies, credentials, owner IDs or draft values.
