# TASK-403: Complete activity-log action label allow-list (11 missing action types)

**Agent:** frontend-developer
**Date:** 2026-07-26
**Status:** done

## Контекст

`getActionLabel` (`frontend/features/users/types.ts`) only translates actions in the
`KNOWN_ACTIONS` allow-list; anything else falls back to the raw backend action code
string. The list had 7 entries but the backend (`AuthService`/`UserService`) writes 18
distinct `ActivityLog.Action` values — `user.login` and 10 others were falling through
to raw strings in the "Активність" tab of the user detail panel. Pure data-completeness
fix: no component/logic changes, only the allow-list array + two locale JSON files.

## Зроблено

- `frontend/features/users/types.ts` — `KNOWN_ACTIONS` expanded from 7 to 18 entries
  (added `user.login`, `user.login_failed`, `user.2fa_enabled/disabled/failed`,
  `user.locked_out`, `auth.refresh_reuse_detected`, `user.permission_granted/revoked`,
  `user.tenant_role_assigned`, `user.locations_updated`). `getActionLabel` untouched —
  its generic `t(\`actions.${action}\`)` lookup already handles the new `auth.*` sibling.
- `frontend/messages/uk.json` and `frontend/messages/en.json` —
  `Dashboard.users.activityLog.actions` block: added the 11 new keys under `user`, plus
  a new sibling `auth.refresh_reuse_detected` key. `"user.login"` label matches the
  existing precedent at `Dashboard.provider.logsPanel.actions.user.login` elsewhere in
  the app.
- Left `frontend/features/provider/components/ProviderLogsPanel.tsx` untouched per brief
  (separate component, own allow-list, not in scope).

## Верифікація

- `npx tsc --noEmit` — 0 errors.
- `npm run lint` — 0 warnings/errors.
- Both JSON files parsed with `node -e "JSON.parse(...)"` — valid.
- Manually cross-checked all 18 `KNOWN_ACTIONS` entries resolve to a matching nested key
  in both locale files — no gaps.
- Live browser check on local dev stack (backend `dotnet run` on :5000, frontend
  `npm run dev` on :3000, against existing local dev Postgres — started fresh for this
  check, stopped after). Logged in as `ea@demo.local`, opened own user's "Activity" tab:
  real activity history already contained several of the previously-broken action
  types, and all now render correctly instead of raw codes — confirmed "Login",
  "Token reuse detected — sessions revoked", "Temporary access granted"/"revoked", and
  "Locations updated" all rendering as proper labels (en locale). uk.json not
  re-verified live (identical key structure to en.json, same code path already proven
  live, JSON validity + key cross-reference already confirmed statically).
- No lasting side effects: only mutation was the normal login-audit-trail row the login
  itself creates. Dev servers stopped after; ports 3000/5000 confirmed free; no
  unrelated pre-existing processes touched.

## Не в скоупі

- `ProviderLogsPanel.tsx`'s own separate action allow-list — not reported broken, left
  as-is per brief.
- Backend — `ActivityLog.Action` values already correct, not touched.

## Git

Not committed — working tree left with 3 modified files
(`frontend/features/users/types.ts`, `frontend/messages/uk.json`,
`frontend/messages/en.json`) for the main session/user to review and commit.
