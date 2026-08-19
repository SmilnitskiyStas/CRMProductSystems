# TASK-437 follow-up — cold bootstrap terminates session after network transition

**Date:** 2026-07-29  
**Device:** realme RMX2063, Android 11 / API 30  
**Severity:** high  
**Status:** open / controlled reproduction required

## Observed sequence

1. Authenticated store-manager session was active.
2. Wi-Fi was disabled; mobile data was already disabled.
3. The transfer form correctly showed its offline banner and preserved local input.
4. Wi-Fi was re-enabled and `wifi_on=1` confirmed.
5. The app was force-stopped immediately, relaunched, and reconnected to the current server.
6. Cold bootstrap returned to auth choice instead of preserving the session.

## Diagnostic interpretation

Wi-Fi had been toggled back on, but internet/API readiness was not independently established before
force-stop. Root bootstrap currently terminates the session for any `/auth/me` exception, so a
temporary transport failure can be treated like invalid authentication. This is consistent with
the observation but requires a controlled offline-cold-start retest to prove the exact branch.

## Expected

A transport-unavailable cold start must preserve SecureStore auth and show a retry/offline state.
Only an authenticated terminal response should clear the session.

Wi-Fi was restored to its original enabled state; mobile data remained in its original disabled
state.

## Current-source retest status

Not executed in the interrupted follow-up run. The prerequisite draft cold-restore check failed
first, and repeated dev-client reconnects required an unusually long textless bootstrap. To avoid
leaving state or expanding retries, connectivity was not toggled. Final state was verified:
Wi-Fi ON, mobile data OFF. This defect remains open pending a controlled dedicated retest.

## Fix prepared — 2026-07-29

Cold bootstrap now distinguishes terminal authentication failures from retryable availability
failures. `401`/`403`, rejected refresh credentials, or an invalid refresh payload still use
centralized terminal cleanup. SecureStore read errors, transport failures, timeouts, and server
`5xx` responses preserve the persisted token, clear private query cache, keep staff identity
unhydrated, and render an offline-safe retry screen.

Retry repeats `/auth/me`; successful recovery hydrates the user and enters the correct route group.
The refresh interceptor likewise no longer deletes a session for transient refresh network/5xx
errors, but continues to terminate on invalid refresh authentication. Explicit logout behavior is
unchanged.

Automated tests cover offline cold start, recovery, terminal failure, cleanup ordering/race safety,
and transient refresh preservation. Status is `fix_ready_for_device_retest`; controlled offline
force-stop and reconnect must be rerun physically.

## Physical-device closure — 2026-08-01

**Status:** closed / device pass

The controlled retest passed on realme RMX2063 (Android 11/API 30) using the installed SDK 56
development client and current source through `adb reverse tcp:8082 tcp:8082`. An authenticated
manager was confirmed online; original connectivity was Wi-Fi ON and mobile data OFF. With both
phone transports off, force-stop/relaunch withheld private UI and rendered only `Немає з’єднання`,
`Сесію збережено...`, and Retry. Wi-Fi was restored, API-host reachability was proven
from the device, and Retry restored the same manager dashboard without login. Final connectivity
is Wi-Fi ON/mobile data OFF; focused logcat contained no React Native fatal, SecureStore, or
navigation-context error.
