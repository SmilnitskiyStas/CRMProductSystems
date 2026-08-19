## Bug: Required-field validation is not localized on mobile auth forms

**Date:** 2026-07-29  
**Severity:** low  
**Task:** TASK-435  
**Device:** realme RMX2063, Android 11 / API 30  
**Build:** `com.shelfguard.mobile` 1.0.0 (1), installed 2026-07-29 21:35:45

### Steps

1. Open the current SDK56 ShelfGuard development build.
2. Select staff login and submit with email/password empty.
3. Return and select consumer login; submit phone/password empty.

### Expected

Required-field messages use Ukrainian consistently with the surrounding auth UI.

### Actual

Both forms show the English string `Required`. Staff malformed-email validation is correctly
localized as `Невірний email`, and invalid synthetic credentials correctly show
`Невірний email або пароль`.

### Impact

No authentication bypass or functional blocker. The mixed-language first-run experience is
visible to every user who submits an incomplete auth form.

### Evidence

TASK-435 UI Automator hierarchies for staff-empty and consumer-empty submissions.

### Fix prepared — 2026-07-29

The auth forms did not provide React Hook Form default values, so untouched controls reached Zod as
`undefined`; Zod emitted its default English `Required` before the existing string validators could
produce Ukrainian messages.

Staff login, consumer login, and consumer registration now use shared auth validation schemas and
explicit empty-string defaults. Empty fields produce field-specific Ukrainian messages; malformed
staff email remains `Невірний email`, and mutation/API error rendering is unchanged. Focused schema
tests cover all three forms.

Static verification passes: TypeScript, lint (0 errors/13 existing warnings), and Jest
(18 suites/78 tests).

### Device closure — 2026-07-29

Status is `resolved_device_verified`. On the rebuilt physical-device app, empty staff login,
consumer login, and consumer registration submissions all render field-specific Ukrainian
`Введіть ...` messages. No English `Required` message remains in those flows.
