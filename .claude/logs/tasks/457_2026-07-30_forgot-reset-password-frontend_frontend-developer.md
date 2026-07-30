# TASK-457: Forgot/reset-password UI + back-to-landing navigation (frontend)

**Agent:** frontend-developer
**Date:** 2026-07-30
**Status:** done — `tsc --noEmit` 0 errors, `npm run build` clean, live-verified end-to-end against the real backend (TASK-456). No blocker.

## Context

Frontend half of `C:\Users\stass\.claude\plans\reflective-churning-quail.md` — Частина A
(back-to-landing navigation) + Частина B (forgot/reset-password UI), consuming TASK-456's
already-shipped `POST /api/auth/forgot-password` / `POST /api/auth/reset-password` contract.
Session was continued after an interruption; state was re-verified directly against the repo
before resuming (auth.ts/useAuth.ts/types.ts/LoginCard.tsx/LoginForm.tsx already done, i18n keys
and the two new cards/pages/routes/middleware/notifications entry were still outstanding).

## Done

### Частина A — back-to-landing navigation
- New `frontend/features/auth/components/AuthLogo.tsx` — the shield SVG + "ShelfGuard"
  wordmark previously inlined in `LoginCard.tsx`, now wrapped in `<Link href="/">`. Inline
  `style={{}}`, matching the auth card's own styling (deliberately not
  `features/landing/components/Logo.tsx`'s Tailwind convention — a different design system).
  Used by `LoginCard.tsx`, `ForgotPasswordCard.tsx`, `ResetPasswordCard.tsx`.
- `LoginCard.tsx` — inline logo markup replaced with `<AuthLogo />` (net -33 lines).

### Частина B — forgot/reset-password
- `LoginForm.tsx` — "Forgot password?" link added after the password field (own row,
  right-aligned, reuses the file's existing `linkBtnStyle`), pointing at `/forgot-password`.
- New `ForgotPasswordCard.tsx` + `ForgotPasswordForm.tsx` (react-hook-form + zod, modeled on
  `LoginForm.tsx`'s step-1 pattern): single email field → `useForgotPassword()` → **always**
  the same success message once the HTTP call itself resolves (backend returns 204
  unconditionally per TASK-456's no-enumeration contract — there is no "email not found"
  state to render). A genuine transport failure (rate limit or network error) shows an error
  instead; 429 reuses the existing `tooManyAttempts` key.
- New `ResetPasswordCard.tsx` + `ResetPasswordForm.tsx`: new-password + confirm fields, zod
  schema reusing `ChangePasswordForm.tsx`'s exact validation rule (12+ chars, needs a letter +
  a digit) and error/hint **text** (not its useState logic — this feature area's convention is
  react-hook-form+zod, per `LoginForm.tsx`). Reads `?token=` via `useSearchParams()`, wrapped in
  `<Suspense>` by the Card (same pattern `LoginCard.tsx` uses for `SessionExpiredNotice`). No
  token at all in the URL → friendly `resetPasswordMissingToken` message instead of a form
  (impossible to submit an empty token). On submit: 204 → success message; 400 → the backend's
  raw `error` text is shown **as-is** for password-policy violations (same convention as
  `ChangePasswordForm.tsx`'s `change.error.message` — ~100+ possible policy messages, not worth
  enumerating), but the one well-known, expected sentinel `"Invalid or expired reset link."`
  (matched by exact string equality) is swapped for a friendlier localized
  `resetPasswordInvalidOrExpired` message instead.
- New routes `frontend/app/(auth)/forgot-password/page.tsx` and
  `.../reset-password/page.tsx` — Server Components with `metadata`, byte-for-byte the same
  shape as `login/page.tsx`.
- `frontend/features/auth/types.ts`/`api/auth.ts`/`hooks/useAuth.ts` —
  `ForgotPasswordRequest`/`ResetPasswordRequest`, `authApi.forgotPassword`/`.resetPassword`,
  `useForgotPassword`/`useResetPassword` (plain `useMutation`, no `onSuccess` — neither touches
  the token/user cache, per brief).
- `frontend/middleware.ts` — `/forgot-password` added to `AUTH_ROUTES` (logged-in users
  redirected to `/dashboard`, same as `/login`). `/reset-password` deliberately **not** added —
  its token in the URL authorizes the action independent of session state.
- `frontend/features/notifications/types.ts` — `"auth.password_reset_requested"` added to
  `NotificationEventType` + `EVENT_TYPE_I18N_KEY` (→ `authPasswordResetRequested`). **Not**
  added to `NotificationSettingsTable.tsx`'s `ALL_EVENTS` (would let a user disable
  notifications about their own password reset) — confirmed `access.temporary_expiring_soon`/
  `access.temporary_expired` follow the identical exclude-from-ALL_EVENTS precedent already.
- i18n: full `Dashboard.auth` block (both `en.json`/`uk.json`) — the 19 keys from the brief,
  plus one addition not in the enumerated list: **`somethingWentWrongError`** (generic
  "something went wrong, try again later" fallback). The brief's own prose mandated this exact
  behavior for a forgot-password transport failure but didn't assign it a key name; reused it
  as `ResetPasswordForm`'s fallback for a non-`ApiError` (pure network) failure too, since both
  forms need the identical fallback and duplicating a second key for the same string made no
  sense. Also added `Dashboard.notifications.eventTypes/eventSource.authPasswordResetRequested`.

## Verification

- `npx tsc --noEmit` — 0 errors.
- `npm run build` — clean (`/forgot-password` and `/reset-password` both present in the route
  output, 1.6 kB / 1.9 kB). Repeated `ENVIRONMENT_FALLBACK` stderr noise during static
  generation is pre-existing/unrelated (already documented in TASK-430's log; exit code 0).
- **Live end-to-end verification**, dev servers started via `preview_start`
  (frontend port auto-reassigned to 57505 — 3000 was held by an unrelated `website-web-1`
  Docker container; backend via `dotnet run --project ShelfGuard.Api`, confirmed migrations
  already applied/up to date). Backend's default CORS origin is `http://localhost:3000` only
  (`Program.cs:160`) — restarted it once with `Cors__Origins=http://localhost:3000,http://localhost:57505`
  (env var, not a code change) so the real cross-origin requests would succeed instead of
  failing CORS, matching the exact issue TASK-421's log already hit and documented.
  Note: the `computer` click tool could not land clicks in this sandbox (`screenshot` reported
  "the Browser pane is not displayed, so the page is not compositing frames") — interactions
  were driven instead via `javascript_tool` dispatching real bubbling `input`/`change`/`click`
  DOM events through React's native value-tracking setter (the standard Testing-Library-style
  approach), which exercises the exact same component code paths a real click would.
  Confirmed via `read_network_requests`/`get_page_text`:
  - `/login` → clicking the logo (`href="/"` confirmed in the DOM) → real navigation lands on
    the public marketing landing page.
  - `/login`'s "Forgot password?" link → correct `href="/forgot-password"` in the DOM.
  - `/forgot-password`: submitted a real email → real `POST /api/auth/forgot-password` →
    **204 No Content** from the live TASK-456 backend → unconditional success message rendered.
  - `/reset-password?token=<fake>`: submitted a new password → real
    `POST /api/auth/reset-password` → **400**, body
    `{"error":"Invalid or expired reset link."}` (confirmed byte-for-byte via
    `read_network_requests`'s response body) → UI correctly shows the translated
    `resetPasswordInvalidOrExpired` message, not the raw English string.
  - `/reset-password` with no `?token=` at all → friendly `resetPasswordMissingToken` message,
    no form rendered, no network call possible.
  - Client-side zod validation confirmed with **no network call fired**: a 6-char password →
    "At least 12 characters"; matching-length but mismatched confirm → "Passwords don't match".
  - Middleware: set the `sg_session` cookie via JS (same cookie `lib/api.ts` sets on login) →
    `/forgot-password` redirected to `/dashboard`; `/reset-password?token=xyz` did **not**
    redirect (rendered normally) — confirms the `AUTH_ROUTES` change and the deliberate
    exclusion of `/reset-password` both work as intended.
  - Cleaned up afterward: killed the ad-hoc backend process, stopped both preview servers.

## Deviations from the brief (both minor, noted per CLAUDE.md's judgment-call rule)

1. Added i18n key `somethingWentWrongError` (not in the brief's enumerated list of 19) — see
   "Done" above for why; content was dictated by the brief's own prose, only the key name is
   new.
2. Reused `tooManyAttempts` (already existing) for a 429 on both new forms, matching
   `LoginForm.tsx`'s own handling of the same rate-limit family — not explicitly requested but
   a 3-line, zero-new-key addition consistent with sibling code.

## Not in scope (per brief)

- `backend/`, `worker/`, `mobile/` — untouched.
- `.claude/docs/*` (api-contracts.md etc.) — TASK-459.
- `frontend/features/users/types.ts` and the `Dashboard.users.activityLog.actions.*`/`auth.*`
  i18n additions visible in `git diff` — pre-existing uncommitted TASK-403 work, not touched or
  authored by this task.

## For TASK-458 (security-reviewer) / TASK-459 (documentation-writer)

- The reset-password 400 body is shown to the user in two ways depending on content: the known
  `"Invalid or expired reset link."` sentinel is replaced with a localized string; anything else
  (a `PasswordValidator` message) is rendered **verbatim, in English**, inside an otherwise
  Ukrainian-localized UI — this mirrors `ChangePasswordForm.tsx`'s existing, already-shipped
  convention, not a new gap introduced here.
- Confirmed live that the reset-link token travels only in the URL query string and is read
  purely client-side (`useSearchParams`) — worth a Referer-leak sanity check in TASK-458 per the
  plan's own checklist (this task did not audit third-party script/analytics presence on these
  pages).
- `/reset-password` is intentionally unauthenticated-and-ungated in `middleware.ts` (see Частина
  A above) — confirmed behavior live, not just by reading the code.

## Files

New:
- `frontend/features/auth/components/AuthLogo.tsx`
- `frontend/features/auth/components/ForgotPasswordCard.tsx`
- `frontend/features/auth/components/ForgotPasswordForm.tsx`
- `frontend/features/auth/components/ResetPasswordCard.tsx`
- `frontend/features/auth/components/ResetPasswordForm.tsx`
- `frontend/app/(auth)/forgot-password/page.tsx`
- `frontend/app/(auth)/reset-password/page.tsx`

Modified:
- `frontend/features/auth/components/LoginCard.tsx`
- `frontend/features/auth/components/LoginForm.tsx`
- `frontend/features/auth/types.ts`
- `frontend/features/auth/api/auth.ts`
- `frontend/features/auth/hooks/useAuth.ts`
- `frontend/middleware.ts`
- `frontend/features/notifications/types.ts`
- `frontend/messages/en.json`
- `frontend/messages/uk.json`

## Git

Not committed — working tree left for review (repo convention: main session/user commits).
