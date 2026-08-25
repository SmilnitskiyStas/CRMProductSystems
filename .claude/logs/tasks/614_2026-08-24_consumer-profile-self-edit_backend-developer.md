# TASK-614 — Consumer self-service profile editing (name/email/phone + audit history)

**Agent:** backend-developer · **Date:** 2026-08-24 · **Status:** done

Plan: `goofy-bubbling-naur.md` §2. Schema handoff read:
`.claude/logs/handoffs/613-to-backend_database-engineer.md`.

## What changed

New `backend/ShelfGuard.Application/Features/ConsumerProfile/`:
- `IConsumerProfileService`/`ConsumerProfileService` — `GetProfileAsync`,
  `UpdateNameOrEmailAsync`, `ChangePhoneAsync`, `GetProfileChangeHistoryAsync`. House
  `(Dto?, string? Error, int? StatusCode)` tuple pattern, `ct = default` last param.
- `Dtos/ConsumerProfileDtos.cs` — `ConsumerProfileDto`, `UpdateConsumerProfileRequest`,
  `ChangeConsumerPhoneRequest`, `ConsumerProfileChangeDto`.

Every name/email/phone write appends a `ConsumerAccountProfileChange` audit row in the
**same** `SaveChangesAsync` call as the `ConsumerAccount` update — one atomic commit.

`IConsumerAccountRepository`/`ConsumerAccountRepository` extended with
`AddProfileChangeAsync` (stage-only) and `GetProfileChangesPagedAsync` (newest first) —
same combined-repository precedent as `ILoyaltyRepository` pairing `LoyaltyMembership`
with its child `LoyaltyLedgerEntry`. `ConsumerAccountProfileChange` has no RLS (per
TASK-613 handoff) — queried purely by `ConsumerAccountId`, no tenant-scoping added.

New `backend/ShelfGuard.Api/Controllers/ConsumerProfileController.cs`, route
`api/consumer/profile`, `[Authorize]`:
- `GET api/consumer/profile`
- `PUT api/consumer/profile` (name/email)
- `PUT api/consumer/profile/phone` (separate route — different verification gate)
- `GET api/consumer/profile/history`

Authorization copied exactly from `ConsumerLoyaltyController`'s private
`ResolveConsumerAccountId()` (`consumer_account_id` JWT claim, `Forbid()` if absent).

DI: registered `IConsumerProfileService`/`ConsumerProfileService` in
`ShelfGuard.Application/DependencyInjection.cs` next to `ILoyaltyService` (re-read the
file immediately before editing — no concurrent-wave conflicts found).

Tests: `backend/ShelfGuard.Tests/ConsumerProfile/ConsumerProfileServiceTests.cs`, 15
cases (NSubstitute, mirrors `LoyaltyServiceTests`/`ConsumerAuthServiceTests` style) —
audit row written on each field change, no-op writes nothing, wrong password rejected
(and short-circuits before any duplicate-phone lookup), duplicate email/phone rejected,
unknown/inactive account 404s.

## Judgment calls

1. **Email duplicate check** — app-level, case-insensitive, same as
   `ConsumerAuthService.RegisterAsync` (no DB unique constraint on `Email`). Empty string
   clears the email.
2. **Status codes** — wrong password and malformed phone both return 400, matching this
   repo's existing `UserService.ChangePasswordAsync` → `AuthController` convention (no
   401 precedent exists anywhere in this codebase); introduced none.
3. **Phone no-op** — setting phone to its own current normalized value succeeds silently,
   writes no audit row (nothing actually changed).
4. **Order of checks in `ChangePhoneAsync`** — password verified first, then phone
   normalized, then duplicate-checked, matching the exact order specified in the task
   brief.

## Build/test status

`dotnet build`: 0 errors, 1 pre-existing unrelated warning (`MarketplaceServiceTests.cs`).
`dotnet test` (full solution): **1852/1852 passing** (15 new, 0 regressions).

## Out of scope (separate follow-up tasks, plan §5)

`Features/Loyalty` tier ladder + consumer endpoints, `PosService.cs` accrual/discount
integration, `Features/CustomerSupport`, `Features/Reviews`, `Features/Customers`
extension, worker recompute job, frontend. `mobile/` untouched.
