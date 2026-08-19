# TASK-437 — Cold session hydration

**Date:** 2026-07-29  
**Agent:** mobile-developer (Codex)  
**Status:** done / device verified

## Implemented

- Added explicit pending/ready auth hydration and a single-flight bootstrap.
- Delayed every route guard until persisted session initialization completes.
- Staff restore waits for `/auth/me`; valid auth routes redirect into staff tabs.
- Consumer snapshots remain isolated and redirect only into consumer tabs.
- Persistence, `/auth/me`, refresh, and invalid-snapshot failures still terminate centrally.
- Added five focused cold-start/force-stop-equivalent tests.

## Verification

TypeScript passes; lint passes with 0 errors/13 existing warnings; Jest passes
(20 suites/84 tests); Android bundle export passes. Physical force-stop/relaunch is pending.

## Physical-device acceptance — 2026-07-29

Store-manager force-stop/relaunch through the installed SDK 56 development client now restores the
persisted staff session after the loading/bootstrap phase. HOT resume and logout cleanup also pass.
The prior TASK-437 defect is closed.
