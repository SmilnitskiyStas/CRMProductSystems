# TASK-344 — Temporary permission grants UI (ADR-019 frontend)

**Agent:** frontend-developer
**Date:** 2026-07-12
**Status:** done

## Scope

Frontend UI for the already-deployed ADR-019 backend (TASK-341/342): temporary,
self-expiring page-access grants layered over `User.Permissions`.

## Changes

- `frontend/features/users/types.ts` — added `PermissionGrantDto`,
  `GrantTemporaryPermissionRequest`, `MAX_GRANT_DURATION_DAYS = 90` (mirrors backend
  `UserService.MaxGrantDurationDays`).
- `frontend/features/users/api/users.ts` — added `grantTemporaryPermission`,
  `getActivePermissionGrants`, `revokeTemporaryPermission` (POST/GET/DELETE
  `/api/users/{id}/permission-grants[...]`).
- `frontend/features/users/hooks/useUsers.ts` — added `useActivePermissionGrants`,
  `useGrantTemporaryPermission`, `useRevokeTemporaryPermission` (React Query,
  optimistic cache updates on the `["users", id, "permission-grants"]` key).
- `frontend/features/users/components/UserPermissionsEditor.tsx` — per-page "⏱ Тимчасово"
  button next to Надати/За замовчуванням/Заборонити, opens an inline
  `<input type="datetime-local">` picker (min=+1min, max=+90d, client-side validated to
  match server rules for fast feedback). Pages with an active temp grant show an amber
  "⏳ Тимчасово до {date}" badge and the temp button is disabled (no duplicate grants).
  New "Активні тимчасові доступи" section lists all active grants (page, granter,
  granted-at, expires-at) with early-revoke. Temp button/section only render when
  `canEdit` (same `ROLE_RANK` outrank check already gating grant/deny) — mirrors the
  server-side rule, doesn't duplicate it.
- `frontend/features/notifications/types.ts` — added `access.temporary_expiring_soon`
  and `access.temporary_expired` to `NotificationEventType`, `EVENT_TYPE_LABELS`,
  `EVENT_TYPE_SOURCE` (not added to `NotificationSettingsTable.ALL_EVENTS` — that list is
  a fixed subset of user-toggleable channels and other TASK-340 event types weren't added
  there either).

## Verification

- `npx tsc --noEmit` — clean.
- `npm run build` — succeeded, all 51 routes generated.
- Live-tested via local dev servers (`backend-dev` + `frontend-dev`, local Postgres):
  logged in as `ea@demo.local` (enterprise_admin), opened `manager@demo.local`
  (store_manager) → Доступ tab → granted temporary "Аналітика" access for +24h →
  confirmed `POST .../permission-grants` → 201, badge and "Активні тимчасові доступи"
  entry appeared, temp button disabled with "Вже надано тимчасовий доступ" tooltip →
  revoked it → confirmed `DELETE .../permission-grants/{id}` → 204, badge/list cleared,
  button re-enabled. Verified via accessibility tree + network log (native
  `window.confirm()` on revoke blocked the `computer` screenshot pipeline in this sandbox —
  worked around by patching `window.confirm` and driving clicks via `javascript_tool` in a
  second tab; no visual screenshot was captured, but full state was verified via DOM
  queries and network requests).

## Files touched

- `frontend/features/users/types.ts`
- `frontend/features/users/api/users.ts`
- `frontend/features/users/hooks/useUsers.ts`
- `frontend/features/users/components/UserPermissionsEditor.tsx`
- `frontend/features/notifications/types.ts`
