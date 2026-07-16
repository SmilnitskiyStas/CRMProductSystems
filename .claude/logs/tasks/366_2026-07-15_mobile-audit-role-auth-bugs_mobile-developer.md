# TASK-366 — Mobile: Block 14 pre-launch audit — write-offs/POS contract, role gating, token restore

**Status:** done (2026-07-15) · **Agent:** mobile-developer (main session, no sub-agent per
explicit instruction) · **Depends:** TASK-354 (write-off FEFO fix), TASK-356/357 (POS
concurrency + cash reconciliation)

Block 14 of the pre-launch audit (`eager-pondering-tower.md`) — first mobile-focused block.
Verified the mobile app against the backend contracts Blocks 4/6 changed, and against
`.claude/agents/mobile-developer.md` + CLAUDE.md's Mobile layout section.

## 1. Write-off mobile flow vs the Block 4 backend fix

`write-offs/create.tsx` already sends the expected `{productId, quantity}` shape (no batch) —
matches the now-fixed FEFO-consume backend. But two bugs made the flow non-functional before
reaching that backend logic at all (see §3/§4) — fixed both.

Also added missing error handling: `write-offs/[id].tsx`'s approve/reject mutations had no
`onError` (every other mutation in the app does). Now that `WriteOffService.ApproveAsync` hard-
fails (400 `{error}`) on insufficient stock (Block 4 P1), the mobile "Затвердити" button would
silently do nothing with zero feedback. Fixed: both mutations show `Alert.alert` on failure,
approve surfaces the backend's actual message.

## 2. POS 409 / close-shift compatibility — verified correct, no change

`pos/payment.tsx`'s `onError` already branches on `status === 400 || status === 409` and shows
the server's `error` message via `Alert.alert('Помилка продажу', ...)` — confirmed against
`PosService.CreateSaleAsync`'s actual 409 text ("Stock was updated concurrently by another
sale..."). No crash, no false "success". `pos/api/posApi.ts::closeShift()` sends no body →
exactly the backward-compatible path Block 6's addendum documented (omit `actualClosingCash` =
old behavior). Mobile does not implement the cash-reconciliation UI TASK-357 built for web
(no `actualClosingCash` input, no discrepancy display) — not a bug, just an unbuilt feature;
flagged as a candidate for a future task, not filed as a KI (low severity, web already covers
the reconciliation need).

## 3. Found + fixed: every mobile role gate used non-existent role names (KI-024)

`CASHIER_ROLES`/`MANAGER_ROLES`/`ALLOWED_ROLES` across 9 screens used invented PascalCase names
(`'StoreManager'`, `'Director'`, `'Admin'`, `'EnterpriseAdmin'`, `'Provider'`, ...) that never
match the real lowercase snake_case role strings (`store_manager`, `enterprise_admin`, ...).
Every `.includes(user.role)` check always evaluated false — POS tab invisible to real cashiers,
manager approve/reject actions invisible everywhere. New `mobile/lib/roles.ts` (mirrors
`frontend/lib/roles.ts`) is now the single source; 9 call sites updated. Full detail in
`known-issues.md` KI-024.

## 4. Found + fixed: `user.locationId` never populated + wrong list-filter query params (KI-025)

Backend's `AuthUserDto` still names the field `StoreId` (JSON `storeId`) — mobile's `AuthUser`
type expected `locationId` with no mapping anywhere, so it was always `undefined`. This
unconditionally blocked write-off/transfer/production-order creation (`if (!user?.locationId)`
guards always fired) and hid the incoming-transfer confirm button. Separately,
`WriteOffsController`/`TransfersController`/`StockController` read `store_id` (snake_case) but
mobile sent `location_id`/`locationId` — so those three lists would have stayed unfiltered even
after fixing the first bug. Fixed both: `authApi.ts` now maps wire `storeId` → `locationId` at
the API boundary; the three `*Api.ts` files send `store_id` on the wire. Confirmed exact param
names by reading all 5 relevant controllers directly (Schedules/Production already used
`locationId` correctly — left untouched). Full detail in `known-issues.md` KI-025.

## 5. Found + fixed: `user` never restored after a cold app restart (KI-026)

`loadToken()` only restored the SecureStore token, not `user` — every role-gated screen (§3, POS
tab) silently broke after any cold restart until re-login, since `getMe()` existed but was never
called. Fixed: `app/_layout.tsx`'s boot effect now calls `getMe()` when a token is present but
`user` is null, via a new `setUser()` store action; falls back to `clearAuth()` on a stale/
expired token. Full detail in `known-issues.md` KI-026.

## 6. Found + fixed: mobile login silently mishandled 2FA-enabled accounts (KI-023, partial)

`login()` blindly destructured `{accessToken, user}` from every `/auth/login` response — a
2FA-enabled account (TASK-330/331, web-only feature) returns `{requiresTwoFactor, challengeToken}`
instead, so mobile would call `setAuth(undefined, undefined)` and silently navigate into a
broken session. Fixed to fail loudly (`Error('TWO_FACTOR_REQUIRED')`, clear Ukrainian message)
instead of silently proceeding — does **not** add a mobile 2FA input screen (real feature gap,
needs a product decision, filed as the "still open" half of KI-023).

## 7. Offline behavior — confirmed still absent, documented (KI-022)

`grep -ri "netinfo|offline|asyncstorage" mobile/` — zero matches, confirms prior research.
Documented as KI-022 with severity assessment (medium-high for POS/warehouse use cases) —
not attempted, offline-first is a substantial dedicated effort out of scope for an audit block.

## 8. Token storage — reconfirmed correct

`expo-secure-store` used for the access token everywhere (`api-client.ts`, `store.ts`); no
`AsyncStorage` usage anywhere in `mobile/` (grep confirmed). No refresh token stored client-side
(relies on the `withCredentials` cookie flow, same shape as web).

## 9. React/TypeScript version divergence — assessed, not a real problem

Web: React 18.2 / TS 5.3.3. Mobile: React 19.2.3 / TS ~6.0.3. No monorepo/workspace linking —
mobile has its own `package.json`/`package-lock.json`, no shared package or relative import
crosses the `frontend/`↔`mobile/` boundary (confirmed via grep). Independent app evolution,
not a real risk. No change made.

## Verification
- `npx tsc --noEmit` in `mobile/` — clean, after every fix.
- `npm run lint` — fails immediately: no `eslint.config.js` exists (ESLint 9 flat-config
  requirement not met). Pre-existing gap, same class of issue noted for `frontend/` in prior
  blocks — not fixed here (adding a lint config is a separate, non-trivial task: needs a
  ruleset decision, not a quick fix).
- `npx expo start --web` — could not verify rendering. First attempt hung on an offline
  dependency-version-cache lookup (`ENOENT ... versions-cache`, no network reachable to Expo's
  registry) and the process died. Retried with `EXPO_OFFLINE=1 CI=1` — got past that, but then
  failed cleanly: `react-dom`/`react-native-web` are not installed dependencies (web target was
  never set up for this project). Did not install them — that's a real dependency addition
  outside this block's scope, not something to do silently. Rendering was checked via
  typecheck + direct code review only, not a live/browser render.
- No emulator/device available in this environment (per task brief) — POS 409 handling,
  write-off create/approve flow, and the role-gate fixes are verified by reading the exact
  request/response contracts on both sides, not by running the app.

## Files changed
- `mobile/lib/roles.ts` (new — canonical role constants + `hasRole()`)
- `mobile/app/(app)/_layout.tsx`, `index.tsx`
- `mobile/app/(app)/write-offs/[id].tsx`
- `mobile/app/(app)/customers/index.tsx`, `[id].tsx`
- `mobile/app/(app)/transfers/[id].tsx`
- `mobile/app/(app)/schedules/index.tsx`
- `mobile/app/(app)/service-desk/index.tsx`, `[id].tsx`
- `mobile/app/(auth)/login.tsx`
- `mobile/app/_layout.tsx`
- `mobile/features/auth/api/authApi.ts`, `mobile/features/auth/store.ts`
- `mobile/features/dashboard/types.ts` (removed now-duplicate role array)
- `mobile/features/write-offs/api/writeOffApi.ts`
- `mobile/features/transfers/api/transferApi.ts`
- `mobile/features/stock/api/stockApi.ts`
- `.claude/docs/known-issues.md` (KI-022..026, new)

## Needs a user decision
- KI-022 (offline support) — priority/scheduling call, real dedicated effort.
- KI-023 (mobile 2FA UI) — build it or declare 2FA web-only; not attempted here.
