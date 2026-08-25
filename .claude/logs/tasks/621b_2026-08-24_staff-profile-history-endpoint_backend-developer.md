# TASK-621b — Staff-facing customer profile-change history endpoint

**Agent:** backend-developer · **Date:** 2026-08-24 · **Status:** done

Plan: `goofy-bubbling-naur.md` §2/§4. Small, narrowly-scoped gap found while wiring up TASK-621
(frontend customer-detail drawer): TASK-614 built a consumer self-service history endpoint
(`GET api/consumer/profile/history`, gated to the caller's own `consumerAccountId`), but there was
no staff-authorized way to view a given CRM `Customer`'s profile-change history. Task logs read:
`.claude/logs/tasks/614_2026-08-24_consumer-profile-self-edit_backend-developer.md`,
`.claude/logs/tasks/618_2026-08-24_customer-detail-tier-tickets-reviews_backend-developer.md`.

## What changed

**`ICustomerService`/`CustomerService`**
(`backend/ShelfGuard.Application/Features/Customers/`) — new method
`GetProfileChangeHistoryAsync(Guid customerId, Guid tenantId, int page, int pageSize, ct)`:
1. Resolves the customer's `LoyaltyMembership` via `ILoyaltyRepository.GetMembershipByCustomerIdAsync`
   (already injected into `CustomerService` from TASK-618 — no new dependency added for this part).
2. If a membership exists, delegates to `IConsumerProfileService.GetProfileChangeHistoryAsync` for
   its `ConsumerAccountId` (new constructor dependency — `IConsumerProfileService` was already
   DI-registered from TASK-614, so no `DependencyInjection.cs` changes needed).
3. If no membership exists at all (customer never joined this tenant's loyalty program), returns an
   empty `PagedResult<ConsumerProfileChangeDto>` — not an error — matching the "no membership = no
   data, not a failure" convention TASK-618 already established for the tier/ticket/review fields.

`LoyaltyMembership.ConsumerAccountId` is a required (non-nullable) column (confirmed in
`AddLoyaltyProgram` migration and the entity), so the only "no data" branch here is "no membership
at all" — there's no case of a membership existing with an empty `ConsumerAccountId`. If
`IConsumerProfileService.GetProfileChangeHistoryAsync` itself returns an error (e.g. a deactivated
consumer account, 404), that also falls back to the empty page rather than propagating — kept
consistent with the same "don't fail the customer-detail view over this" philosophy.

**`CustomersController`** (`backend/ShelfGuard.Api/Controllers/CustomersController.cs`) — new
action:

```
GET api/customers/{id}/profile-history?page=&pageSize=
```

Same `[Authorize(Policy = AppPolicies.AtLeastStoreManager)]` class-level gate as the rest of the
controller. Returns `PagedResult<ConsumerProfileChangeDto>` (200). Reuses the existing `PagedQuery`
clamping helper (page ≥ 1, pageSize 1–200), same pattern as `GetAll`.

Did **not** touch `CustomerDetailDto` — this is a separate, lazily-loaded paged endpoint per the
brief, since profile history could be long and the frontend drawer will only fetch it when that tab
is opened.

## Tests

2 new cases in `backend/ShelfGuard.Tests/Customers/CustomerServiceTests.cs` (added
`IConsumerProfileService` substitute to the fixture, updated the `CustomerService` constructor
call):
- `GetProfileChangeHistoryAsync_CustomerWithLinkedMembership_ReturnsConsumerHistory` — mocks the
  membership lookup and the `IConsumerProfileService` call, asserts the service passes through the
  consumer's actual `PagedResult` unchanged and calls `IConsumerProfileService` with the resolved
  `ConsumerAccountId`.
- `GetProfileChangeHistoryAsync_CustomerWithNoLoyaltyMembership_ReturnsEmptyPage_NotAnError` —
  asserts an empty page (not null, not an exception) and that `IConsumerProfileService` is never
  called when there's no membership to resolve.

## Build/test status

`dotnet build -c Release`: 0 errors, 1 pre-existing unrelated warning
(`MarketplaceServiceTests.cs:534`, same as TASK-618's report — untouched).

`dotnet test -c Release` (full solution): **1925/1925 passing** (2 new, 0 regressions; baseline was
1923/1923 as of TASK-618).

## Files changed

- `backend/ShelfGuard.Application/Features/Customers/ICustomerService.cs`
- `backend/ShelfGuard.Application/Features/Customers/CustomerService.cs`
- `backend/ShelfGuard.Api/Controllers/CustomersController.cs`
- `backend/ShelfGuard.Tests/Customers/CustomerServiceTests.cs`

No schema, DI registration, or other feature files touched. `mobile/` untouched.

## Handoff

`.claude/logs/handoffs/621b-to-frontend_backend-developer.md` — route + response shape for
TASK-621 (frontend customer-detail drawer's profile-history tab).
