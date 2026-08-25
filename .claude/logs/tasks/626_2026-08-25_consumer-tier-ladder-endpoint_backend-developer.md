# TASK-626 — Consumer loyalty tier ladder endpoint (mobile contract gap)

**Status:** done · **Agent:** backend-developer · **Updated:** 2026-08-25
Extends TASK-615 (`.claude/logs/tasks/615_2026-08-24_loyalty-tier-ladder-pos-integration_backend-developer.md`).
Reported by the mobile Codex agent: the rank-progress screen needs the full tier ladder, but the
existing consumer endpoint only returns current+next tier, and the admin ladder endpoint is
enterprise-admin-only.

## What changed

- `ILoyaltyService`/`LoyaltyService` — new `GetTierLadderForConsumerAsync(consumerAccountId,
  tenantId, ct)`. Same membership check as `GetTierProgressAsync` (404 "You are not a member of
  this loyalty program." if none), then reads the ladder through the same
  `ITenantSessionOverride`-scoped call (`loyalty_tier_definitions` has no `consumer_self_access`
  RLS policy), reusing the existing `ToTierDefinitionDto` mapper. Returns the full list instead of
  computing current/next.
- `ConsumerLoyaltyController` — new `GET {tenantId}/tiers/ladder`, mirrors `GetTierProgress`'s
  structure (resolve consumer id → `Forbid()` if null → delegate → map error/success tuple).
- Tests added to `ShelfGuard.Tests/Auth/LoyaltyServiceTests.cs`: non-member 404, active member
  gets full ladder ordered by SortOrder, empty ladder returns `[]` not null. No new mock setup
  needed — the `List<LoyaltyTierDefinition>` `ITenantSessionOverride` pass-through added in
  TASK-615 already covers this method's `_tenantScope.ExecuteAsync` call.

## Build / tests

`dotnet build`: 0 errors, 1 pre-existing unrelated warning (Marketplace tests, same as prior
sessions). `dotnet test`: **1949/1949 passing** (3 new, up from TASK-625's 1946/1946 baseline,
zero regressions).

## Docs

Updated `.claude/docs/api-contracts.md` (new `tiers/ladder` route under "Loyalty tier ladder —
consumer-facing", header date bumped). Updated `.claude/tasks/current.md`. Wrote
`.claude/logs/handoffs/626-to-mobile-codex.md` confirming the route/response shape matches the
mobile team's spec exactly.

## Not touched

`mobile/`, `frontend/` — backend only, per the task constraint (mobile owned by a separate
concurrent Codex agent).
