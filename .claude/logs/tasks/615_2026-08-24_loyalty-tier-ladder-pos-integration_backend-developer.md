# TASK-615 — Loyalty tier ladder CRUD + consumer endpoints + PosService accrual/discount integration

**Status:** done · **Agent:** backend-developer · **Updated:** 2026-08-24
Plan: `goofy-bubbling-naur.md` §2. Handoff read: `.claude/logs/handoffs/613-to-backend_database-engineer.md`.

## What changed

**`Features/Loyalty` extension** (`ILoyaltyRepository`/`LoyaltyRepository`,
`ILoyaltyService`/`LoyaltyService`, `Dtos/LoyaltyDtos.cs`):
- `GetTierLadderAsync`/`UpsertTierLadderAsync` (admin) — mirrors `GetSettingsAsync`/
  `UpsertSettingsAsync`'s shape. `UpsertTierLadderAsync` bulk-replaces the ladder by matching
  submitted rows to existing ones **by SortOrder** (not blind delete+recreate) so an unchanged
  tier keeps its database Id — `LoyaltyMembership.CurrentTierId` pointing at it survives the
  edit until the next nightly recompute. A SortOrder dropped from the request deletes that row
  (FK SetNull cascades to any membership pointing at it — safe, the nightly job re-evaluates
  everyone). Reordering (same tier, new SortOrder) is therefore delete+insert — documented
  limitation, not a bug.
- `GetTierProgressAsync`/`GetTierHistoryAsync` (consumer) — current tier + composite score +
  next-tier gap; paged tier-change history. `loyalty_tier_definitions` has no
  `consumer_self_access` RLS policy (staff-only config per the TASK-613 handoff), so
  `GetTierProgressAsync` reads it through `ITenantSessionOverride` — same mechanism
  `ResolveCustomerCodeFormatAsync` already uses for `loyalty_program_settings`.
  `GetTierHistoryAsync` reads `loyalty_tier_change_history` ambiently (it does carry
  `consumer_self_access`), same as `GetHistoryAsync`.
- `GetMembershipByIdAsync` now `.Include(m => m.CurrentTier)`.

**New controller** `LoyaltyTierSettingsController` (`api/settings/loyalty/tiers`, GET/PUT,
`AppPolicies.AtLeastEnterpriseAdmin` — copied from `LoyaltySettingsController`).

**`ConsumerLoyaltyController`** gained `GET {tenantId}/tiers` and `GET {tenantId}/tiers/history`.

**`PosService.CreateSaleAsync`** (the core of this task):
- Accrual: `accrual = round(tx.TotalAmount * AccrualRatePercent/100 * tierMultiplier, 2)`,
  `tierMultiplier = membership.CurrentTier?.AccrualMultiplier ?? 1.0m`.
- Tier discount: applied **per item**, not as a lump-sum reduction on `tx.TotalAmount`.
  Computed off `priceRetail` independently of the critical-batch auto-discount, then combined
  into one `DiscountAmount` per line (capped at the item's price). Gated the same way as
  accrual/redemption: only when a membership is present and the program is enabled — a
  membership with no `CurrentTier` yet behaves identically to pre-TASK-615 code (multiplier
  1.0, discount 0).
- One-line comment at the discount site pointing out that both redemption and tier discount
  reduce `tx.TotalAmount`, the base the (not-yet-built) RFM/tier composite-score job will read
  — pre-existing accepted pattern, not new.

## Item-level vs. lump-sum discount decision

Went with the plan's default (itemized) — no correctness reason found to deviate. Rejected
alternative: applying tier discount as one subtraction from `tx.TotalAmount` (mirroring how
redemption works). Rejected because it would leave per-item `PriceFinal`/`DiscountAmount`
inconsistent with the transaction total, which matters for the Checkbox fiscal receipt (line
items are built from `PosTransactionItem`, not backfilled from the total) — same reasoning the
existing critical-batch auto-discount already follows per-item. Sub-decision: tier discount
amount is computed off `priceRetail` (not stacked on top of the promo-discounted price) and
additively combined with any promo discount — avoids an arbitrary compounding-order choice;
documented as a judgment call, no live ПРРО-compliance owner was available to confirm
synchronously.

## Tests / build

`dotnet build`: 0 errors, 1 pre-existing unrelated warning (Marketplace tests, same as
TASK-614's log). Full `dotnet test`: **1871/1871 passing** (19 new — 3 in
`PosServiceTests.cs` per the task brief: no-tier regression pin, 1.5× multiplier, 10% discount
reducing both the item total and the accrual base; 16 in `LoyaltyServiceTests.cs` covering the
new ladder CRUD/progress/history methods). Also updated two other manual `ILoyaltyRepository`
fakes that don't implement history through mocks (`FiscalizationRetryTests.cs`,
`LoyaltyConcurrencySalesIntegrationTests.cs`) to satisfy the extended interface — no-op stubs,
no behavior change to those tests.

## Not implemented here (separate follow-up tasks per plan §5)

`Features/CustomerSupport`, `Features/Reviews`, `Features/Customers` extension, worker
tier-recompute job, frontend (tier ladder admin page, customer card tabs). `mobile/` untouched
(owned by a separate concurrent agent).
