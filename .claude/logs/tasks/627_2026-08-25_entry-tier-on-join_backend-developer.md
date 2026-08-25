# TASK-627 — Assign entry-level tier immediately on loyalty membership creation

**Status:** done · **Agent:** backend-developer · **Updated:** 2026-08-25
Extends TASK-615 (tier ladder)/TASK-619 (nightly recompute job). Read both task logs first per
the brief — TASK-619's log is the source of the "CurrentTierId/CompositeScore are nightly-job-
only writes" constraint this task deliberately carves a narrow exception into.

## What changed

New shared helper `LoyaltyService.AssignEntryTierAsync(membership, tenantId, ct)`: reads
`ILoyaltyRepository.GetTierLadderAsync(tenantId, ct)` directly (no `ITenantSessionOverride` —
both call sites already run with the right tenant context by the time it executes), and when
the ladder is non-empty, sets `CurrentTierId`/`CompositeScore = 0m`/`TierScoreUpdatedAt = UtcNow`
on the not-yet-saved membership and stages a `LoyaltyTierChangeHistory` row
(`FromTierId = null`, `ToTierId = <entry tier>`, scores 0→0) via a new
`ILoyaltyRepository.AddTierHistoryAsync` method — the table's first C#-side writer; previously
only the nightly worker job's raw SQL wrote it. Empty ladder → no-op, unchanged pre-existing
behavior (null tier, 0 score).

Wired into both fresh-membership creation paths:
- `CreateMembershipCoreAsync` (shared by `JoinAsync` and `ResolveOrCreateMembershipByPhoneAsync`)
- `JoinAsStaffAsync` — creates its own `LoyaltyMembership` directly rather than delegating to
  `CreateMembershipCoreAsync` (different Customer-resolution path, sets `LinkedUserId`), so it
  needed the same fix applied separately. Confirmed and fixed, not just noted.

Both call sites still do exactly one `AddMembershipAsync` + one `SaveChangesAsync` — the helper
only stages fields/the history row, so the membership insert and its history row commit
atomically together, matching TASK-614's audit-row pattern.

The rejoin branch in `JoinAsync` (`existing.Status == Left` → reactivate) is untouched — it never
calls `AssignEntryTierAsync`, confirmed by a dedicated regression test.

**Doc comments updated**: `LoyaltyMembership.CurrentTierId`/`CompositeScore` XML docs no longer
say "never written at request time" — they now document the TASK-627 exception (INSERT-only,
no existing row, no concurrent writer) alongside the still-true "no request-time UPDATE" rule.

## Docs

No `api-contracts.md`/`domain-model.md` update: `LoyaltyMembershipSummaryDto` (JoinAsync's
response shape) has no `CurrentTierId`/tier fields at all — checked directly, confirmed by a
compile error when a draft test assumed otherwise. The observable effect only shows up through
the separate `GetTierProgressAsync`/`GetTierHistoryAsync` endpoints (TASK-615), whose contracts
are unchanged; they just return non-null/non-empty data sooner now.

## Tests / build

`dotnet build`: 0 errors, 1 pre-existing unrelated warning (Marketplace tests, same as prior
logs). Had to add a no-op `AddTierHistoryAsync` stub to three hand-written `ILoyaltyRepository`
fakes that don't implement the interface through NSubstitute (`FiscalizationRetryTests.cs`,
`LoyaltyConcurrencySalesIntegrationTests.cs`, `PosServiceTests.cs`) — same class of fixup TASK-615
already needed once for this interface.

`dotnet test`: **1953/1953 passing** (4 new in `LoyaltyServiceTests.cs`, up from TASK-626's
1949/1949 baseline, zero regressions):
- `JoinAsync_new_member_with_configured_ladder_assigns_entry_tier`
- `JoinAsync_new_member_with_no_ladder_leaves_tier_unassigned`
- `ResolveOrCreateMembershipByPhoneAsync_new_consumer_with_configured_ladder_assigns_entry_tier`
- `JoinAsync_rejoining_a_left_membership_does_not_touch_tier_even_with_ladder_configured`

Added a constructor-level default (`_loyalty.GetTierLadderAsync(Arg.Any<Guid>(), ...).Returns(
new List<LoyaltyTierDefinition>())`) to `LoyaltyServiceTests`, mirroring the existing
`_locations.GetAllAsync` default pattern, so every pre-existing JoinAsync/
ResolveOrCreateMembershipByPhoneAsync/JoinAsStaffAsync test keeps its pre-TASK-627 behavior
without needing to touch each one individually.

## Not implemented here

`JoinAsStaffAsync` has no dedicated new unit test beyond the shared `AssignEntryTierAsync` path
already covered via `JoinAsync`/`ResolveOrCreateMembershipByPhoneAsync` — same helper, same
branch logic, and the brief's test list didn't ask for a fifth case. `mobile/`/`frontend/`
untouched (owned by a separate concurrent Codex agent per this session's established
constraint) — no handoff needed, pure server-side behavior change, no new endpoint/contract.
