# TASK-444 defect — switching user deletes previous owner's operational draft

**Date:** 2026-07-29  
**Device:** realme RMX2063, Android 11 / API 30  
**Severity:** high  
**Status:** resolved / device verified

## Device reproduction

1. Sign in as the approved store manager.
2. Open the transfer-create form and enter a harmless local note without submitting.
3. Confirm the offline banner and local field preservation.
4. Sign in as the approved storekeeper and open the same transfer-create route.
5. Confirm the manager note is not exposed — isolation initially fails closed.
6. Return to the manager and reopen transfer-create.

## Actual

The manager's draft is absent after the foreign-owner route was opened. It is isolated from the
storekeeper, but not preserved for its owner.

## Cause boundary

`draftStorageKey()` uses one key per operation/scope, not per tenant/user. When
`loadOperationalDraft()` sees a foreign owner, it removes that shared key. This implements
non-disclosure but destroys the prior owner's draft, contradicting switch-away/switch-back
durability acceptance.

## Expected

Draft keys must include tenant and user identity, or foreign-owner loading must not delete another
owner's record. Switching users must hide the draft and returning to its owner must restore it.

No business operation was submitted. A separate disposable draft was explicitly deleted and its
absence was confirmed after cold restart.

## Current-source retest — incomplete / still failing, 2026-07-29

A fresh harmless manager transfer note was entered and given two seconds to autosave. After
force-stop and development-client reconnect, bootstrap showed a textless loading state for roughly
one minute before restoring the authenticated manager dashboard. Opening transfer-create for the
same owner did **not** restore the marker. The essential owner cold-restore assertion therefore
failed before the user-switch sequence, and risky repetitions were stopped.

No marker was visible on the restored form, so there was no remaining test draft to disclose or
submit. Storekeeper switching and the fixed owner-return assertion were not repeated in this run.
The defect remains open.

## Final focused retest — PASS, 2026-07-29

Manager entered only a harmless note with destination/items empty. After a six-second autosave
window, background/foreground and force-stop/reconnect both preserved it. Storekeeper saw no
marker and did not discard anything; returning manager restored the marker. Manager explicitly
deleted it, and another cold restart confirmed absence.

The transfer owner-switch deletion and incomplete-draft loss no longer reproduce. No transfer was
submitted. Development-client bootstrap reached authenticated dashboard in about 64 seconds; at
15 seconds no timeout/retry UI was present.

## Fix prepared — 2026-07-29

Operational draft storage keys now include encoded tenant ID, user ID, operation kind, and scope.
Save, load, autosave serialization, and explicit discard therefore operate only inside the current
owner namespace. Opening the same form as another user neither reads nor removes the first owner's
draft; switching back restores it.

Existing shared versioned keys are migrated only when the embedded owner matches the current
tenant/user. A foreign legacy record is left untouched and undisclosed so its owner can migrate it
later. Corrupt/current-owner legacy records fail closed; secrets remain excluded by the existing
whitelist.

Tests cover switch-away/switch-back restoration, owner-only discard, operation isolation, and safe
legacy migration. Status is `fix_ready_for_device_retest`; physical owner switching across
transfer/write-off/production remains required.

## Same-owner cold-restore root cause and second fix — 2026-07-29

The owner namespace fix did not explain the later same-owner blank form. End-to-end inspection
found a second root cause: autosave accepted incomplete form payloads, but restore validation
required non-empty source/location fields. A note entered before an assigned/source location was
available could be written successfully, then classified invalid and deleted on cold load.

Restore validation now accepts structurally valid incomplete draft strings; business submission
validation remains unchanged and server-authoritative. The shared hook retains the latest
whitelisted snapshot and flushes it again when AppState leaves `active`, while immediate autosave
continues. Owner changes clear the in-memory flush reference before asynchronous hydration.

Deterministic integration coverage now performs mount → edit → confirmed AsyncStorage write →
unmount/process-queue reset → same-owner remount and verifies restoration. A second integration
test mounts a foreign owner between sessions and verifies non-disclosure, non-deletion, and owner
restore. Status remains `fix_ready_for_device_retest`; no physical pass is claimed.
