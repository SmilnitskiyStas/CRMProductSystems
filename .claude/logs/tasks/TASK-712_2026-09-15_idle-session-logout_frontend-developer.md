# TASK-712 — Idle-based session logout (frontend-only)

**Status:** done · **Agent:** frontend-developer

## What changed
- New `frontend/features/auth/hooks/useIdleLogout.ts`: hardcoded `IDLE_TIMEOUT_MS = 5 * 60_000`.
  Tracks last-activity in a ref (no re-renders) from `mousemove/mousedown/keydown/scroll/touchstart`
  (passive). Cross-tab sync via `localStorage["sg_last_activity"]` (throttled writes, `storage`
  event listener) so activity in one tab resets the clock in all tabs and an idle logout in one
  tab logs out all tabs. `setInterval` (5s) checks elapsed idle time; only attaches when
  `getToken()` is truthy at mount; full cleanup on unmount. No warning modal — silent logout at
  threshold, per spec.
- `useLogout()` (`frontend/features/auth/hooks/useAuth.ts`) now accepts an optional `reason`,
  threaded to `router.push(reason ? `/login?reason=${reason}` : "/login")`. Had to type the
  mutation explicitly as `useMutation<void, unknown, string | void>(...)` — `string | void` (not
  `string | undefined`) is what keeps TanStack Query's inferred `mutate()` callable with zero
  arguments, so the existing no-arg call site (`UserMenu.tsx`) is unaffected.
- Wired `useIdleLogout()` into `DashboardChrome` (`frontend/app/(dashboard)/layout.tsx`) — active
  across all authenticated routes (dashboard/provider/supplier/analytics).
- `SessionExpiredNotice.tsx` now also renders for `reason === "idle_timeout"`, using new key
  `Dashboard.auth.idleTimeout` (uk + en, added next to `sessionExpired` in both files).

## Build/lint
- `npx tsc --noEmit` — clean.
- `npm run lint` — clean.
- `messages/uk.json` / `en.json` — valid JSON, leaf-key parity confirmed (5039/5039).

## Notes
- No backend changes — relies on the existing `/api/auth/logout` revoking the refresh token.
- Not manually verified in a running browser session (no dev server in this session); verified by
  static build/lint checks only.
