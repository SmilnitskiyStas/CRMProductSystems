# TASK-438 — Android Back challenge cancellation

**Date:** 2026-07-29  
**Agent:** mobile-developer (Codex)  
**Status:** done / device verified

## Implemented

- Added one cancellation boundary for hardware Back and visible header Back.
- Handles Android IME dismissal because the keyboard can consume the first Back event.
- Clears memory-only challenge on every focused-route cleanup/unmount.
- Resets verification without submitting and replaces the route with staff login.
- Added a focused cancellation regression test.

## Verification

TypeScript passes; lint passes with 0 errors/13 existing warnings; Jest passes
(20 suites/84 tests); Android bundle export passes. Physical Back and successful verification
navigation remain pending.

## Physical-device acceptance — 2026-07-29

Hardware Back safely cancels the challenge both before input and after focusing the OTP field.
Exactly one approved recovery code was submitted and accepted; enterprise-admin navigation and
logout pass. The consumed code value is not recorded, and reuse was not tested.
