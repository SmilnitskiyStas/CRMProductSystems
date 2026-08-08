# TASK-497 — Mobile: dual-token session model, wallet/history restoration, dead-code cleanup

**Status:** done · **Agent:** mobile-developer · **Depends:** TASK-496 (backend-developer,
parallel, same working tree) — handoff `.claude/logs/handoffs/496-to-mobile-developer.md`
confirmed exact wire shape; matched the pre-agreed contract with zero deviations.

## What changed

Product framing: an employee is first a normal loyalty-program participant (personal
`ConsumerAccount`) who *additionally* gets workspace access when linked to an active staff
`User`. Both identities are usable at once, backed by two independent JWTs held
simultaneously. Fixed 4 confirmed issues from the audit of the earlier (codex) unified-auth
work.

**Store/session model** (`features/auth/store.ts`, `types.ts`, `session.ts`,
`lib/api-client.ts`): replaced the single `accessToken` with `personalAccessToken` /
`workspaceAccessToken`, both persisted to SecureStore (`personal_access_token` /
`workspace_access_token`) and restored on cold start. New store actions:
`setWorkspaceAuth`, `setPersonalAuth`, `setPersonalToken` (token-only grant for the
mid-2FA-challenge case, no profile data available yet). `sessionKind` kept as a
"staff if workspace token exists, else consumer" label but is no longer load-bearing for
route guards — those now check the token fields directly. Two axios clients:
`apiClient` (workspace-scoped, unchanged refresh-and-retry flow, now keyed off
`workspace_access_token`) and new `personalApiClient` (consumer-scoped, attaches
`personal_access_token`, no refresh — the backend issues no refresh token for a consumer
session). Loyalty wallet API calls (`getMemberships`/`getLoyaltyCode`/`getLoyaltyHistory`/
`joinTenantProgram`) moved to `personalApiClient`; staff POS/cabinet loyalty calls stay on
`apiClient`. This structurally guarantees the scoping (per-module, not a runtime flag).

**Fix 1 — wallet/history restored.** Moved `(consumer)/wallet.tsx` and `history.tsx` into
`(personal)/` verbatim (all imports were `@/`-aliased, no path changes needed). Added
"Бонуси"/"Історія" tabs to `(personal)/_layout.tsx`, gated on `personalAccessToken !== null`
only (not staff-vs-consumer identity) — visible to both a plain consumer and a linked staff
member. Deleted `app/(consumer)/` entirely (4 files) and its `<Stack.Screen>` registration
in `app/_layout.tsx`. Updated stale `(consumer)/...` path references in loyalty
comments (`loyaltyApi.ts`, `useLoyalty.ts`, `loyalty/store.ts`).

**Fix 2 — old staff login unreachable.** Deleted `(auth)/login.tsx` outright (nothing else
imports a route file). Removed its `<Stack.Screen name="login">` from `(auth)/_layout.tsx`.
`two-factor.tsx` cancel still targets `/(auth)/consumer-login` (unchanged, re-verified).

**Fix 3 — 401 handling.** Added `/mobile-auth/login` and `/mobile-auth/register` to
`isPublicAuthRequest()`'s allowlist in `lib/api-client.ts`. New tests in
`lib/__tests__/api-client.test.ts`: 401 from each new endpoint does not trigger
`/auth/refresh`; regression test confirms an ordinary protected workspace endpoint still
does. Also added `personalApiClient` tests (attaches the right token, no refresh on 401).

**Fix 4 — dead code removed.** Deleted `useConsumerLogin()`
(`hooks/useConsumerAuth.ts`, kept `useConsumerRegister`), the whole
`api/consumerAuthApi.ts` file (`loginConsumer`/`registerConsumer`), `consumerLoginSchema`/
`ConsumerLoginFormData` (`validation.ts` + its test block), and now-orphaned
`ConsumerLoginRequest`/`ConsumerAuthResult` types. Confirmed zero remaining hits:
`rg "useConsumerLogin|loginConsumer|registerConsumer|consumerLoginSchema|\(consumer\)" mobile`.

**2FA verify merge** (`hooks/useLogin.ts`): `useVerifyTwoFactor`'s success handler now calls
`setWorkspaceAuth` (was `setAuth`, which no longer exists) instead of clobbering the whole
session — `setWorkspaceAuth` only ever touches workspace fields, so whatever
`personalAccessToken` the initial login step already stored survives untouched. Navigates
to `/(personal)` (unchanged).

**Bug caught in self-review:** the `(auth)/_layout.tsx` "already authenticated → redirect to
/(personal)" guard, if keyed on token presence alone, would fire the instant
`setPersonalToken` runs mid-2FA-challenge — bouncing the user away before the two-factor
screen (which lives inside the same `(auth)` stack) could ever render, breaking the whole
workspace-2FA step. Fixed: the guard now also checks `!twoFactorChallenge` before
redirecting, so the two-factor screen stays reachable while a challenge is pending, and the
redirect fires correctly once the challenge clears (success or cancel).

**Bootstrap** (`bootstrap.ts`): `BootstrapState` now exposes both tokens. Restructured to
validate the workspace half only (via `/auth/me`, unchanged terminal/offline/retry
semantics) when `workspaceAccessToken` is present; a personal token with no restorable
`consumerUser` snapshot (corrupted SecureStore) terminates the session; a personal-only
session is trusted from its SecureStore snapshot with no live validation (no consumer "me"
endpoint exists, same as before this task).

## Scoping decisions (not explicitly requested, called out for transparency)

- **No migration for pre-TASK-497 installs.** SecureStore key names changed
  (`access_token` → `personal_access_token`/`workspace_access_token`); an already-installed
  dev/QA build will be logged out once and must re-login. Not mentioned in the brief; this
  app is local-APK-only internal testing (see memory note), so skipped rather than adding
  migration complexity.
- **`useLogin()` (plain, non-verify staff login hook), `staffLoginSchema`/
  `StaffLoginFormData`, and `authApi.login()` are now fully orphaned** by deleting
  `login.tsx` (its only caller) but were left in place — TASK-497's dead-code list named
  only the *consumer*-login path, not the staff one. Fixed the one hard compile break
  (`useLogin()` referenced the now-removed `setAuth` store action) but did not delete the
  hook/schema itself. Flagged here for a follow-up cleanup pass.
- Deliberately did **not** add any 401-driven local-logout behavior to `personalApiClient` —
  no spec for it; errors just propagate to React Query, and the wallet/history screens
  already degrade to their existing empty-state UI when a query fails (verified: neither
  screen crashes on an error, both fall back to "no memberships" empty state).

## Verification

`npx tsc --noEmit`: clean. `npx jest --runInBand` (full suite): **30/30 suites, 151/151
tests pass**, including all rewritten/new auth, api-client, bootstrap, and mobileAuthApi
tests. No changes made under `backend/`.

## Files touched

Modified: `features/auth/{store,types,session,bootstrap,validation}.ts`,
`features/auth/hooks/{useMobileLogin,useConsumerAuth,useLogin}.ts`,
`features/auth/__tests__/{store,bootstrap,validation}.test.ts`,
`features/auth/api/__tests__/mobileAuthApi.test.ts`, `lib/api-client.ts`,
`lib/__tests__/api-client.test.ts`, `features/loyalty/{store,hooks/useLoyalty,api/loyaltyApi}.ts`,
`app/_layout.tsx`, `app/(auth)/_layout.tsx`, `app/(app)/_layout.tsx`,
`app/(personal)/{_layout,index}.tsx`.
Added: `app/(personal)/{wallet,history}.tsx`.
Deleted: `app/(consumer)/` (4 files), `app/(auth)/login.tsx`,
`features/auth/api/consumerAuthApi.ts`.
