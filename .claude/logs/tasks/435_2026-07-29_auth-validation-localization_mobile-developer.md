# TASK-435 — Auth required-field localization

**Date:** 2026-07-29  
**Agent:** mobile-developer (Codex)  
**Status:** fix_ready_for_device_retest  
**Scope:** `mobile/` only

## Root cause

Staff and consumer auth forms omitted React Hook Form `defaultValues`. Untouched controls therefore
reached Zod as `undefined`, producing Zod's default English `Required` instead of the existing
Ukrainian validators.

## Implemented

- Added shared schemas for staff login, consumer login, and consumer registration.
- Added explicit empty-string defaults to all three forms.
- Added field-specific Ukrainian required messages, including empty staff email and registration
  password.
- Preserved malformed-email validation and existing API/mutation error rendering.
- Added four focused schema regression tests.

## Verification

- Focused auth validation tests — pass, 4/4.
- `npm run type-check` — pass.
- `npm run lint` — pass, 0 errors / 13 existing warnings.
- `npm run test:ci` — pass, 18 suites / 78 tests.
- `git diff --check` — pass.
- Physical-device validation — pending rebuilt APK; no device-pass claim.
