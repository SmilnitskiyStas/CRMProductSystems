# TASK-364 — Frontend: Block 13 pre-launch audit — cross-cutting frontend quality

**Status:** done (2026-07-15) · **Agent:** frontend-developer (main session) · **Depends:** —

Block 13 of the pre-launch audit (`eager-pondering-tower.md`) — first block focused on
frontend-wide quality rather than a single feature module. Avoided
`frontend/features/support/`, `frontend/features/service-desk/`, and the settings
support tab per instructions (parallel rework in progress elsewhere).

## 1. KI-004 (duplicate `apiFetch`) — confirmed already resolved, no code change
`features/inventory/api/products.ts` and `features/dashboard/api/dashboard.ts` both already
`import { api } from "@/lib/api"`. `grep -rn "function apiFetch|const apiFetch" frontend/`
matches only `lib/api.ts` itself — no duplicate implementation left anywhere. Doc was stale;
`known-issues.md` KI-004 marked resolved.

## 2. Error boundaries (Next.js App Router) — added
New `frontend/app/error.tsx` and `frontend/app/global-error.tsx` — neither existed before.
Friendly Ukrainian-language fallback UI ("Щось пішло не так" / "Спробувати ще раз" /
"На головну"), `console.error` with a `TODO(KI-020)` marker at the exact spot a Sentry
`captureException` call would go once KI-020 is resolved. `global-error.tsx` renders its own
`<html>/<body>` with inline styles only (no Tailwind/provider dependency — it replaces the
root layout when *that* throws, so it must not depend on anything that could itself be broken).

**Found + fixed a build-blocking bug while verifying:** adding `global-error.tsx` broke
`npm run build` on the pinned `next@14.1.0` — `PageNotFoundError: Cannot find module for page:
/_document` during "Collecting page data". Root-caused to a known Next.js 14.1.0 bug (fixed in
14.1.1+) where the framework's automatic static 500-page generation conflicts with
`app/global-error.tsx` when no `pages/_document` exists (this app is pure App Router). Verified
by removing `global-error.tsx` (build succeeds) then re-adding it with `next@14.1.4` temporarily
installed (build succeeds). Fix: bumped `next` `14.1.0` → `14.1.4` (last patch release on the
same minor line — pure bugfixes, not the `14.2.x` minor which changes more, e.g. Metadata API
behavior across all 43 pages; deliberately the smallest fix that unblocks this). `package.json`/
`package-lock.json` updated.

Live-verified in the browser (not just build): temporary throwing test page (outside any
feature/support/service-desk path, deleted after) hit `error.tsx`'s fallback — confirmed the
UI renders (icon, message, both buttons) and `console.error` fires with the
"Unhandled application error: ..." prefix, not a raw Next.js dev overlay crash screen.

## 3. Token in `localStorage` — evaluated, NOT changed, documented as KI-021
Traced the actual boot sequence before deciding: `app/(dashboard)/layout.tsx` hard-gates on
`getToken()` truthy *before* any network call (`if (mounted && !getToken()) router.replace
("/login")`), `useAuth.ts`'s `useMe()` is `enabled: Boolean(getToken())`, and **no code anywhere
in the app calls `POST /api/auth/refresh` proactively on mount** — `tryRefresh()` in `lib/api.ts`
is only ever reached reactively from inside a request that already 401'd. Removing the
`localStorage` mirror without adding a new "attempt silent refresh on mount, block render behind
it" bootstrap would log every user out on every page reload/new tab — a guaranteed regression,
not a safe quick fix. `middleware.ts`'s Edge check is a third, independent auth-state read
(cookie presence only, doesn't touch the access token — left alone, it's fine as-is). Full
finding + 3 mitigation options (do nothing / add CSP / full bootstrap rewrite) written into
`known-issues.md` KI-021 for the user to pick from.

## 4. Sentry / error tracking — confirmed absent, documented as KI-020
`grep -ri sentry frontend/` — zero matches; no `@sentry/*` in `package.json`. Not installed
(explicitly out of scope — needs a real DSN/account only the user can provision). Documented in
`known-issues.md` KI-020 with the exact install/wire-up steps (`@sentry/nextjs` +
`npx @sentry/wizard`, env vars, where the two new error boundaries' `console.error` calls become
`Sentry.captureException`).

## 5. Test coverage — 5 new test files, 46 new tests (0% → covers the highest-traffic pure logic)
No `@testing-library/react` in the project, so component-level tests were out of reach without
adding a new dependency (not done — kept to the existing Vitest-only setup, matches "pick files
with real logic" over broad shallow coverage). Picked the 5 files with real, previously-untested
domain logic:
- `lib/api.test.ts` (15 tests) — the shared API client: Authorization header injection, JSON/204
  handling, error message extraction (incl. non-JSON error bodies), FormData Content-Type
  skip, and the full 401→refresh→retry state machine (successful refresh+retry, failed refresh
  clears token, anonymous `/api/auth/login` never triggers refresh, `markLoggedOut()` silences
  racing 401s, a fresh `setToken()` clears the logged-out flag). This is the single choke point
  every feature's API calls go through.
- `lib/roles.test.ts` (12 tests) — `hasRole`/`canManageLegalEntities`/role-set sanity (RBAC
  gating mirrored from backend `AppPolicies`).
- `lib/providerPermissions.test.ts` (5 tests) / `lib/supplierPermissions.test.ts` (5 tests) —
  per-user permission-override merge logic (the exact class of logic BUG-019 was about).
  Two source files with concretely different failure surfaces, so kept as two test files
  instead of parameterizing into one.
- `lib/slug.test.ts` (9 tests) — UA/RU→LAT transliteration + sanitization for tenant slugs
  (never crashes/produces unsafe output on garbage input).
POS/money form validation (mentioned as a candidate in the brief) lives inline inside
`CloseShiftDialog.tsx`'s `handleSubmit`, not as an exported pure function — testing it would
require either extracting it (component behavior change, out of scope for a test-only task) or
`@testing-library/react` (new dependency); left as-is, noted here rather than silently skipped.

## Verification
- `npx tsc --noEmit` — clean.
- `npx vitest run` — 6 files, 48 tests (2 pre-existing + 46 new), all green.
- `npm run build` — clean after the `next@14.1.4` bump (was broken with `global-error.tsx` on
  `14.1.0`, see above).
- Live browser check of the error boundary fallback UI (see §2) — done, then temporary test
  route deleted.

## Files changed
- `frontend/app/error.tsx` (new)
- `frontend/app/global-error.tsx` (new)
- `frontend/lib/api.test.ts` (new)
- `frontend/lib/roles.test.ts` (new)
- `frontend/lib/providerPermissions.test.ts` (new)
- `frontend/lib/supplierPermissions.test.ts` (new)
- `frontend/lib/slug.test.ts` (new)
- `frontend/package.json` / `frontend/package-lock.json` (next 14.1.0 → 14.1.4, patch-only)
- `.claude/docs/known-issues.md` (KI-004 → resolved; new KI-020 Sentry, KI-021 token storage)

## Needs a user decision
KI-021 (token storage) — three options laid out in `known-issues.md`, no default assumed.
KI-020 (Sentry) — needs the user to create a Sentry project/DSN before any code can be wired.
