# TASK-498 — Backend: auto-create loyalty membership by phone at POS

**Status:** done · **Agent:** backend-developer · **Date:** 2026-08-09

## Problem

A freshly-registered consumer could not use the loyalty wallet at all: the only way to create a
`LoyaltyMembership` was the consumer manually typing a tenant's GUID into a "Код магазину" field
(`POST /api/consumer/loyalty/{tenantId}/join`), with no store discovery UI — this required a
store employee to verbally read out a GUID. Product decision: remove manual store selection
entirely. When a customer actually makes a purchase, the POS terminal already knows which tenant
it is — the membership should be created silently at that moment.

## What changed

### `backend/ShelfGuard.Application/Features/Loyalty/LoyaltyService.cs`

- Extracted `JoinAsync`'s membership-creation body (customer find-or-create + `LoyaltyMembership`
  construction + `TotpSecret` generation + save) into a new private
  `CreateMembershipCoreAsync(Guid tenantId, Guid consumerAccountId, string phone, string fullName,
  CancellationToken ct)`. Does **not** check for an existing membership — each call site owns that
  idempotency check, since the right way to read "does this pair already have a membership"
  differs by call site (ambient consumer session vs. already-tenant-scoped staff session).
  `JoinAsync`'s external behavior/signature is unchanged — it still does its own pre-check outside
  `ITenantSessionOverride.ExecuteAsync`, then calls the shared core inside the override.
- Added `ResolveOrCreateMembershipByPhoneAsync(Guid tenantId, string phone, CancellationToken ct)`:
  1. `PhoneNormalizer.Normalize(phone)` — `null` → `(null, "Invalid phone number.", 400)` (a real
     client error).
  2. Tenant lookup + `HasModule("loyalty")` check — disabled/missing → `(null, null, null)` ("not
     applicable", not an error).
  3. `IConsumerAccountRepository.GetByPhoneAsync` — no match or inactive → `(null, null, null)`.
  4. Existing membership at this tenant → returned as-is, `IsNewMembership: false`, balance
     untouched.
  5. No existing membership → `CreateMembershipCoreAsync(...)`, `IsNewMembership: true`, balance 0.
- Runs **entirely inside the caller's existing (staff JWT) tenant RLS context** — no
  `ITenantSessionOverride` used or needed, unlike `JoinAsync`'s consumer-session call site. A
  staff request already carries a real `tenant_id` claim, and `TenantConnectionInterceptor` has
  already set `app.tenant_id` for the whole request before this method runs, so
  `loyalty_memberships`' `tenant_isolation` RLS policy is already satisfied by construction.
  Getting this wrong would either silently write to the wrong tenant or fail RLS entirely — it's
  not wrong here because the tenantId passed in is the JWT's own claim, never a request body value.

### `backend/ShelfGuard.Application/Features/Loyalty/ILoyaltyService.cs`
Interface declaration for the new method, XML doc documenting the Error/Result convention.

### `backend/ShelfGuard.Application/Features/Loyalty/Dtos/LoyaltyDtos.cs`
- `ResolveOrCreateMembershipByPhoneRequest(string Phone)`
- `LoyaltyMembershipLookupResult(Guid MembershipId, decimal Balance, bool IsNewMembership, string ConsumerFullName)`
  — deliberately omits phone/email (caller already has the phone it searched with).

### `backend/ShelfGuard.Api/Controllers/LoyaltyController.cs`
New endpoint:

```
POST /api/loyalty/resolve-or-create-by-phone
[Authorize(Policy = AppPolicies.CanAccessPos)]
```
Same auth policy as the sibling `resolve-code` action (`cashier`+). Tenant resolved only from the
JWT `tenant_id` claim (`GetTenantId()`), never from the request body.

Request:
```json
{ "phone": "0501234567" }
```
Response 200 (found/created):
```json
{
  "found": true,
  "membershipId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "balance": 0,
  "isNewMembership": true,
  "consumerFullName": "Іван Іваненко"
}
```
Response 200 (not applicable — invalid-but-not-malformed phone match failure never happens here;
this is specifically: no matching/active ConsumerAccount, or tenant's loyalty module disabled):
```json
{ "found": false }
```
Response 400 (only for a structurally unparseable phone string):
```json
{ "error": "Invalid phone number." }
```

## Tests

`backend/ShelfGuard.Tests/Auth/LoyaltyServiceTests.cs` — added under a new
`ResolveOrCreateMembershipByPhoneAsync (TASK-498)` section:
- new consumer → creates membership, `IsNewMembership: true`, balance 0
- existing membership at this tenant → returned idempotently, `IsNewMembership: false`, balance
  preserved
- membership exists at a *different* tenant only → still creates a new, independent membership at
  this tenant (multi-tenant proof)
- no matching `ConsumerAccount` → `Result: null`, `Error: null` (no membership created)
- tenant's `loyalty` module disabled → `Result: null`, `Error: null`, no `ConsumerAccount` lookup
  even attempted
- invalid/unparseable phone (`""`, `"123"`, `"not-a-phone"`) → `Error` set, `StatusCode: 400`,
  tenant lookup never attempted

## Pre-existing, unrelated issue found (not fixed)

The working tree already had uncommitted, unrelated WIP predating this session (visible via `git
diff HEAD`, not something this task introduced) redesigning the consumer-facing QR/checkout code
to be cross-tenant: `GetCurrentCodeAsync` → `GetConsumerCodeAsync` (new signature, drops
`tenantId`), a new `ConsumerAccount.LoyaltyTotpSecret` column, and a "legacy `SGLOY1.` code"
fallback branch inside `ResolveCodeAsync`. This is explicitly out of this task's scope
(`ConsumerLoyaltyController` / `GetCurrentCodeAsync` / `ResolveCodeAsync` are all named
"do not touch" in the brief), so it was left alone except for the minimum needed to get a clean
build:
- `LoyaltyServiceTests.cs` had 2 tests still calling the old `GetCurrentCodeAsync(id, tenantId)`
  signature, which no longer compiled — updated them to the current
  `GetConsumerCodeAsync(id)`/`ConsumerAccount.LoyaltyTotpSecret`-based behavior.
- No EF migration exists yet for the new `LoyaltyTotpSecret` column. All 8 Loyalty *integration*
  tests (`LoyaltyRepositoryIntegrationTests`, `LoyaltyJoinRlsIntegrationTests`,
  `LoyaltyConcurrencySalesIntegrationTests`) fail against the real Postgres test DB with
  `Npgsql.PostgresException 42703: column "LoyaltyTotpSecret" of relation "consumer_accounts" does
  not exist`. This needs a migration before that other (unrelated) work can be considered done —
  flagged here for whoever owns it, not fixed as part of TASK-498.

## Verification

- `dotnet build` (Api + Tests): 0 errors, 0 new warnings.
- `dotnet test --filter LoyaltyServiceTests`: 39/39 pass (all unit tests, mocked repos — no DB
  needed).
- Full `dotnet test`: 1355/1363 pass; the 8 failures are the pre-existing integration tests above
  (missing migration), unrelated to this task's changes.
- Nothing staged or committed.
