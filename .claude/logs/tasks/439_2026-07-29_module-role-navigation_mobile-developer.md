# TASK-439: Module activation and role-aware mobile navigation

**Date:** 2026-07-29
**Agent:** mobile-developer (Codex)
**Status:** review_pending_device

## Investigation result

The first documentation-only review suggested a blocker. Inspection of the current controller
then proved the document stale: `/api/settings/modules` is `[Authorize]` for all tenant staff,
resolves `tenant_id` server-side, and returns `businessType` plus active modules. Implementation
therefore resumed without inventing module state.

Confirmed current contract:

- `AuthUserDto` carries permissions, capabilities, and tabs; mobile now preserves all three.
- `GET /api/settings/modules` is `[Authorize]` for every authenticated tenant role.
- The controller derives tenant identity exclusively from the authenticated `tenant_id` claim.
- The response contains the calling tenant's `businessType` and active module-key list.
- Provider identities intentionally have no tenant claim and receive `403`; mobile therefore
  fails closed for tenant-module routes while retaining non-tenant shell routes.

## Implemented

- Added a typed modules settings API/query boundary.
- Preserved permissions, capabilities, and tabs in the mobile AuthUser mapping.
- Added a pure centralized route policy for role, capability/tab, business type, and module.
- Applied the same policy to Dashboard/More shortcuts, bottom tabs, and global route guards.
- Added Ukrainian Access Denied and Module Disabled states.
- Added cashier, storekeeper, manager, enterprise admin, provider, supplier, disabled-module,
  missing-context, business-type, shortcut, and API tests.

## Verification

- `npm run type-check`: PASS
- `npm run lint`: PASS — 0 errors, 19 pre-existing warnings
- `npx jest --runInBand --watch=false`: PASS — 10 suites, 45 tests
- Android smoke QA: pending TASK-435 device/AVD

**Historical contract clarification:** `.claude/logs/handoffs/439-to-backend_mobile-developer.md`
