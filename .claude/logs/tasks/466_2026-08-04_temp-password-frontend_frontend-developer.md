# TASK-466: Temporary-password forgot-password — frontend (remove reset-password UI, banner, auth-locale default)

**Agent:** frontend-developer
**Date:** 2026-08-04
**Status:** done — `tsc --noEmit` 0 errors, `npm run build` clean (`/reset-password` confirmed
absent from the route table), `npm run lint` 0 warnings/errors. Live-verified against the real
TASK-465 backend + a scripted client-side fetch-mock for the banner (details below). No blocker.

## Numbering note

Confirmed 466 free right before writing this log — `.claude/logs/tasks/` max was 465,
`current.md`'s own `## TASK-` headers max was 465 too. TASK-461/462/463 are the parallel mobile
work the brief flagged, unrelated to this task.

## Context

TASK-465 rewrote the backend for the temp-password redesign (forgot-password now issues a
directly-usable password, 3h validity, no link/token — full contract in that task's log). This
task consumes that contract: removes the old reset-password-by-link UI, updates forgot-password
copy, adds a persistent temp-password banner, and fixes the auth-pages' default locale.

## Done

### 1. Removed reset-password-by-link entirely
Deleted `ResetPasswordCard.tsx`, `ResetPasswordForm.tsx`, `app/(auth)/reset-password/` (dir now
gone). Removed `resetPassword`/`ResetPasswordRequest` from `auth.ts`/`types.ts`,
`useResetPassword` from `useAuth.ts`. Removed the 12 listed i18n keys from `Dashboard.auth` in
both `en.json`/`uk.json` — verified full leaf-key parity between the two files afterward (3589
keys each, zero drift either direction). Fixed now-stale doc comments in
`AuthLogo.tsx`/`ForgotPasswordCard.tsx` ("three public auth surfaces" → "both") and removed
`middleware.ts`'s comment explaining why `/reset-password` wasn't in `AUTH_ROUTES` — the route
doesn't exist at all anymore, nothing left to explain. `AUTH_ROUTES`/`PROTECTED` themselves needed
no behavior change; `/reset-password` was never in either array.

### 2. Forgot-password copy
Updated `forgotPasswordSuccessMessage` per the brief's suggested wording. Also updated
`forgotPasswordDescription`/`forgotPasswordSubmitButton` (not explicitly listed in the brief) —
both still said "instructions" ("we'll send instructions" / "Send instructions"), which
contradicts the new temp-password behavior and would have read as inconsistent copy against the
new success text on the same small form. `ForgotPasswordForm.tsx` itself needed no code change —
it only ever references these strings via `t(...)`.

### 3. Temporary-password banner — new `features/auth/components/TemporaryPasswordBanner.tsx`
Reads `useMe()`; renders nothing unless `passwordIsTemporary && temporaryPasswordExpiresAt`.
Mounted in `app/(dashboard)/layout.tsx` as the first child of the content column, directly above
`<TopBar />`. Checked `ImpersonationBanner.tsx` first (the one existing persistent-banner
convention) but deliberately did NOT copy its `position:fixed` — normal flow avoids z-index/offset
math for the rare case both banners are live at once (a provider impersonating a tenant user who
separately has an active temp password), and the content column never scrolls independently
anyway (only `<main>` does), so `fixed` buys nothing here. Colors reuse
`SessionExpiredNotice.tsx`'s established amber warning palette (`#F59E0B`) rather than
Impersonation's blue/purple — this is an actionable warning, not a neutral mode indicator.
Date/time formatting reuses the `useLocale()` + `toLocaleString(intlLocale, {day,month,year,
hour,minute})` pattern already used by `UserActivityLog.tsx`/`NotificationDetailDrawer.tsx`.
Action link → `/settings-user#password`; added `id={section.id}` to `SectionCard` in
`settings-user/page.tsx` so the anchor actually lands on the password card instead of the top of
a 4-card stacked page.

Disappearance path: `useChangePassword` (`features/profile/hooks/useProfile.ts`) had no
`onSuccess` at all before this task, unlike its siblings `useTwoFactorEnable`/
`useTwoFactorDisable` in the same file, which already invalidate `ME_KEY`. Added the same
`qc.invalidateQueries({queryKey: ME_KEY})` — backend clears the temp-password marker on a
successful change (TASK-465), so this makes the banner drop as soon as the change succeeds
instead of waiting on an unrelated refetch/relogin, consistent with the brief's own "поки не
завантажиться свіжий useMe()" framing.

### 4. Login 401 sentinel (implied by the brief's contract section, not one of its 4 numbered steps)
`LoginForm.tsx`'s `loginErrorMessage` previously collapsed every 401 to the generic
`invalidCredentials`, which would have silently swallowed the backend's new
"Temporary password has expired..." text. Added exact-string sentinel matching
(`TEMP_PASSWORD_EXPIRED_SENTINEL`) → new key `temporaryPasswordExpiredError`, same convention the
now-removed `ResetPasswordForm.tsx` used for its own sentinel. Flagging this since it wasn't
literally one of the brief's 4 steps, but skipping it would mean the backend's new 401 case never
actually reached a user.

### 5. Auth-pages default locale
`dashboard-locale.ts`: `resolveBrowserLocale`/`resolveDashboardLocale` now take a
`fallback: DashboardLocale` param (defaulted to `"uk"` on the exported one, so the dashboard's
existing no-arg call sites are behavior-identical to before). `DashboardIntlProvider` takes an
optional `defaultLocale` prop (default `"uk"`) used for both the initial `useState` and passed
into `resolveDashboardLocale()`. `app/(auth)/layout.tsx` passes `defaultLocale="en"`;
`app/(dashboard)/layout.tsx` passes nothing (unchanged "uk" default).

## Deviations from the brief (flagging, not blocking)

1. Updated `forgotPasswordDescription`/`forgotPasswordSubmitButton` beyond the explicitly-listed
   `forgotPasswordSuccessMessage` — see step 2 above.
2. Added `LoginForm.tsx`'s temp-password-expired sentinel handling — not in the 4 numbered steps,
   but implied by the brief's own "Контекст"/TASK-465's "Contract for TASK-466" — see step 4.
3. Added `id={section.id}` to `settings-user/page.tsx`'s `SectionCard` for `#password`
   deep-linking — small, makes the banner's CTA land on the right card instead of page top.
4. Added `onSuccess` (ME_KEY invalidation) to `useChangePassword` — not explicitly requested, but
   the banner's "disappears once you change your password" requirement doesn't happen promptly
   without it.

All four are same-feature/same-file-area, low-risk, and follow sibling-code conventions already
live elsewhere in this codebase — per CLAUDE.md's judgment-call rule, not product/scope calls.

## Verification

- `npx tsc --noEmit` — 0 errors (after clearing a stale `.next/types/app/(auth)/reset-password/
  page.ts` left over in the gitignored build cache from before this task's deletion — regenerates
  clean).
- `npm run build` — exit 0, clean. Confirmed via `grep -c reset-password` on the full build log:
  **zero** occurrences anywhere, including the route table.
- `npm run lint` — 0 warnings/errors.
- Live, against the real TASK-465 backend (`dotnet run --project ShelfGuard.Api`, local dev
  Postgres — log showed "No migrations were applied. The database is already up to date.") +
  frontend dev server (`preview_start`, port 60000, backend `Cors__Origins` opened for it):
  - `/login`, fresh session (no `sg_locale` cookie, `navigator.language`="en-US"): rendered in
    English ("Inventory management system" / "EMAIL" / "PASSWORD" / "Forgot password?" /
    "Sign in") — confirms the fallback fix. Dashboard's own default is unchanged (still "uk"),
    not re-verified live since nothing about it changed.
  - `/forgot-password`: real submit of `stassmilnitskiy@gmail.com` (the seeded dev admin —
    **note:** this really did call the live `ForgotPasswordAsync`, which overwrites that
    account's password with a new temp one, 3h validity from ~00:10 UTC 2026-08-05; the actual
    generated value was never visible to me, only delivered via that account's own notification
    channel — if that dev login is needed before it expires, go through forgot-password again or
    check Telegram) → real `204` → new success copy shown verbatim: "If that email exists in our
    system, we've sent a temporary password to it. Sign in with it, then set a new password."
  - `/reset-password` and `/reset-password?token=abc123` → both hit Next's standard 404 page.
  - Banner: mocked `POST /api/auth/login` and `GET /api/auth/me` client-side (`window.fetch`
    override) with a fake `AuthUserDto` carrying `passwordIsTemporary:true` — chose this over
    chasing a real temp password through Telegram/email (delivery blocked per known issues, and
    reaching into worker/DB internals to retrieve it is outside frontend-developer's scope; the
    backend side of temp-password issuance is already covered by TASK-465's own test suite).
    Confirmed live: banner rendered `"Your password is temporary and valid until 08/05/2026,
    03:10 AM." / "Set a new password"` (correct UTC→local conversion), positioned above TopBar
    with no layout breakage, link resolved to `/settings-user#password` and landed on the actual
    password `SectionCard`. Then simulated a successful password change (mocked 204 + flipped the
    fake user's `passwordIsTemporary` to `false`) → banner disappeared automatically via the new
    `useChangePassword` → `invalidateQueries(ME_KEY)` → fresh `useMe()` chain, no manual reload.
  - Not live-tested: `LoginForm.tsx`'s new 401 sentinel branch — needs a real password that
    hashes to a match AND is already-expired, a narrow real-backend state not reachable through
    the mocked path above. Verified by code review only: string matches TASK-465's documented
    contract byte-for-byte, same pattern as the (now-removed) `ResetPasswordForm.tsx` sentinel
    that TASK-457 did live-verify.
  - Cleaned up: killed the ad-hoc `dotnet run` process, stopped the `preview_start` frontend
    server.

## Not in scope (per brief)

- `backend/`, `worker/`, `mobile/` — untouched.
- `.claude/docs/*` — TASK-468 (documentation-writer).

## For TASK-467 (security-reviewer)

- Banner component: `frontend/features/auth/components/TemporaryPasswordBanner.tsx`, mounted in
  `frontend/app/(dashboard)/layout.tsx`.
- Login 401 sentinel: `frontend/features/auth/components/LoginForm.tsx`
  (`TEMP_PASSWORD_EXPIRED_SENTINEL`).
- The dev admin account `stassmilnitskiy@gmail.com` in the local dev DB currently has a live temp
  password (see Verification note above) — not a prod/staging concern, flagging only so it isn't
  mistaken for something else during review.

## Files

New:
- `frontend/features/auth/components/TemporaryPasswordBanner.tsx`

Deleted:
- `frontend/features/auth/components/ResetPasswordCard.tsx`
- `frontend/features/auth/components/ResetPasswordForm.tsx`
- `frontend/app/(auth)/reset-password/page.tsx`

Modified:
- `frontend/features/auth/types.ts`
- `frontend/features/auth/api/auth.ts`
- `frontend/features/auth/hooks/useAuth.ts`
- `frontend/features/auth/components/AuthLogo.tsx`
- `frontend/features/auth/components/ForgotPasswordCard.tsx`
- `frontend/features/auth/components/LoginForm.tsx`
- `frontend/middleware.ts`
- `frontend/app/(auth)/layout.tsx`
- `frontend/app/(dashboard)/layout.tsx`
- `frontend/app/(dashboard)/settings-user/page.tsx`
- `frontend/features/profile/hooks/useProfile.ts`
- `frontend/i18n/dashboard-locale.ts`
- `frontend/i18n/DashboardIntlProvider.tsx`
- `frontend/messages/en.json`
- `frontend/messages/uk.json`

## Git

Not committed — working tree left for review (repo convention: main session/user commits).
