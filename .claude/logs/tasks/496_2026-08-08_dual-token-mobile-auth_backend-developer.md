# TASK-496: Dual-token mobile-auth response (personal + workspace JWT)

**Agent:** backend-developer
**Date:** 2026-08-08
**Status:** done

## Context

The codex-built unified mobile-auth endpoints (`POST /api/mobile-auth/login`,
`POST /api/mobile-auth/register`, `MobileAuthController.cs`) previously returned a single
`AccessToken` per response, discarding the consumer JWT once a linked active staff `User` was
found. Product decision: an employee is first and foremost a loyalty-program participant
(personal `ConsumerAccount` — bonus wallet, cashback, QR, purchase history), who *additionally*
gets workspace access when linked to an active `User`. Both identities must be usable
simultaneously on the client, so the response needed two independent tokens instead of one.

## Changes

### `backend/ShelfGuard.Application/Features/MobileAuth/Dtos/MobileAuthDtos.cs`
- `MobileLoginResponse`: `AccessToken` → `PersonalAccessToken` + `WorkspaceAccessToken` (both
  `string?`).
- `ForStaff(workspaceAccessToken, user)`: now sets `PersonalAccessToken = null` (legacy
  staff-only fallback path — no personal identity exists to expose).
- New `ForLinkedStaff(personalAccessToken, workspaceAccessToken, user)`: `ForStaff(...) with
  { PersonalAccessToken = personalAccessToken }` — reuses `ForStaff`'s
  role/permissions/capabilities/tabs computation via a record `with` expression instead of
  duplicating it (kept `ForStaff` un-overloaded since it's also called from the true
  staff-only branch, which has no personal token).
- `ForConsumer(personalAccessToken, ...)`: field renamed, `WorkspaceAccessToken = null`.

### `backend/ShelfGuard.Api/Controllers/MobileAuthController.cs`
Both `Login` and `Register` share the same 4-branch structure; each got the equivalent edit:
1. **Consumer only** (no linked active staff): `ForConsumer(consumer.AccessToken, ...)` —
   unchanged behavior, renamed field.
2. **Consumer linked to active staff, no 2FA**: now calls `ForLinkedStaff(consumer.AccessToken,
   workspaceSession.Response.AccessToken, workspaceSession.Response.User)` instead of discarding
   `consumer.AccessToken` and calling `ForStaff` alone. No second consumer JWT minted — reuses
   the one already in scope from `_consumerAuth.LoginAsync`/`RegisterAsync`.
3. **Consumer linked to staff requiring 2FA**: challenge response gains a `personalAccessToken`
   field: `{ requiresTwoFactor: true, challengeToken, personalAccessToken: consumer.AccessToken }`
   — the consumer already fully authenticated (password verified) in this branch, so the client
   can show personal/loyalty features immediately while the second factor for workspace access is
   pending.
4. **Legacy staff-only fallback** (`FindStaffAsync` branch, no `ConsumerAccount` exists at all):
   unchanged — `ForStaff(outcome.Response.AccessToken, outcome.Response.User)`, and its own 2FA
   challenge stays exactly `{ requiresTwoFactor, challengeToken }` with no personal-token field at
   all (nothing to expose, no consumer session was ever created in this branch).
5. Added the missing `[ProducesResponseType(typeof(object), StatusCodes.Status200OK)]` on
   `Register` (the 2FA-challenge branch's `200` shape wasn't declared there, unlike `Login` which
   already had both).

### Tests
- `backend/ShelfGuard.Tests/Auth/MobileLoginResponseFactoryTests.cs`: updated field assertions
  for `ForStaff`/`ForConsumer`, added `Linked_staff_response_carries_both_tokens_with_staff_effective_access`
  covering the new `ForLinkedStaff` factory method.
- `backend/ShelfGuard.Tests/Auth/MobileAuthControllerTests.cs`: updated existing assertions
  (`response.AccessToken` → `response.PersonalAccessToken`/`WorkspaceAccessToken`), added:
  - `Login_linked_staff_requiring_two_factor_still_exposes_personal_token` (branch 3, login)
  - `Register_linked_staff_requiring_two_factor_still_exposes_personal_token` (branch 3, register)
  - `Staff_only_fallback_two_factor_challenge_has_no_personal_token_field` (branch 4's challenge —
    asserts the anonymous object literally has no `personalAccessToken` property, not just that
    it's null)

## Deviations from brief

None. Implemented exactly as specified, including the `ForLinkedStaff` factory-method approach
(rather than overloading `ForStaff`) called out in the brief.

## Build / test status

- `dotnet build ShelfGuard.sln`: **0 errors** (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`).
- `dotnet test --filter "FullyQualifiedName~MobileAuth"`: **7/7 pass**
  (`MobileAuthControllerTests` only — `MobileLoginResponseFactoryTests` doesn't match this
  substring filter since its class name doesn't contain "MobileAuth" contiguously).
- `dotnet test --filter "FullyQualifiedName~MobileLoginResponseFactoryTests"`: **3/3 pass**.
- `dotnet test --filter "FullyQualifiedName~ConsumerAuthService"`: **12/12 pass**, unaffected
  (out of scope, ran per brief for regression confidence).
- Confirmed no `AddJsonOptions` in `ShelfGuard.Api/Program.cs` — camelCase is ASP.NET Core's
  default `System.Text.Json` policy, applies automatically.

## Files touched

- `backend/ShelfGuard.Application/Features/MobileAuth/Dtos/MobileAuthDtos.cs`
- `backend/ShelfGuard.Api/Controllers/MobileAuthController.cs`
- `backend/ShelfGuard.Tests/Auth/MobileAuthControllerTests.cs`
- `backend/ShelfGuard.Tests/Auth/MobileLoginResponseFactoryTests.cs`

Nothing staged or committed (per instructions — orchestrator reviews and commits).
