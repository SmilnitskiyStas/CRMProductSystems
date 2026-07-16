# TASK-355 — Backend: Block 5 pre-launch audit — Orders/ADU/Buffer

**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-354

Block 5 of the pre-launch audit (`eager-pondering-tower.md`). Reviewed `Features/Adu`,
`Features/Buffer`, `Features/Orders` (services, repositories, controllers, EF model,
worker/frontend integration) against v2-spec.md §1–3 and v1-spec.md §2.7 (MOQ/USQ).

## Reviewed, no changes needed (formulas match spec)

- **ADU** (`AduCalculator.Compute`): valid-day rule (not promo, not anomaly, sold>0 OR
  eod>0), today excluded, three windows (30/60/90d), group assignment tightest-first
  (≥20/30→g3, ≥15/60→g2, ≥10/90→g1, else null) — all match §1 exactly. Division-by-zero
  already guarded: `WindowAdu` returns `(null, 0)` when the window has 0 valid days
  instead of dividing.
- **CDA Buffer** (`CdaBufferCalculator.Compute`): Green/Yellow/Red formulas match §2
  exactly. Rule 1 (buffer computed at order time, not daily) and rule 3 (LT/OC derived
  dynamically) are honored — confirmed via `frontend/features/orders/hooks/useOrders.ts`
  `useGenerateOrder`, which chains recalc-ADU → recalc-buffer → calculate on every
  "generate order" click, matching the spec comment already in the file.
- **Order formula** (`OrderFormula.Compute`): `Raw = Buffer + SafetyBuffer − OnHand −
  InTransit` matches §3. MOQ floor / USQ rounding, degenerate MOQ/USQ (≤0) defaulted to
  1, never-below-MOQ clamp — all correct. ОЗ (one-off) and РТО (MTO-reserved) terms are
  openly `0` with an in-code comment explaining they arrive with MTO support — this is
  already a flagged, deliberate deferral, not a silent gap.
- **In-transit source**: `GetInTransitAsync` sums `StockReceipts` with `Status ==
  "draft"`. §3 spec text says `status=in_transit`, but the receipt lifecycle in this
  codebase only has `draft → received/cancelled` (confirmed in `ReceiptService.cs`) — no
  literal `in_transit` state exists anywhere in the system. This is a naming difference
  only, not a functional bug: `draft` *is* "ordered, not yet received" here.

## Documented spec deviation (not silently fixed — flagged per audit brief)

**MOQ/USQ rounding ladder is anchored at zero, not at MOQ, when MOQ is not itself a
multiple of USQ.** v1-spec §2.7: *"MOQ=12, USQ=6 → можна: 12, 18, 24, 30..."* — read
literally ("після MOQ кожен крок = USQ") this is the ladder `MOQ + k×USQ`. The example
only looks like "any multiple of USQ ≥ MOQ" because 12 happens to already be a multiple
of 6. `OrderFormula.Compute` actually does `Math.Round(raw/usq)*usq` (nearest multiple of
USQ from zero), then clamps up to MOQ if needed — it does not walk the `MOQ + k×USQ`
ladder. Divergence only shows up when MOQ is **not** a USQ multiple, e.g. MOQ=10, USQ=6:
spec ladder → 10, 16, 22, 28...; actual code for raw=15 → 18 (nearest multiple of 6), not
16. Narrow real-world impact (suppliers typically set MOQ as a USQ multiple in practice),
but flagged explicitly rather than silently changed — needs a product decision on
whether to switch to the MOQ-anchored ladder. Regression test added documenting current
behavior: `OrderFormulaTests.Rounding_ladder_is_anchored_at_zero_not_at_moq_KNOWN_SPEC_DEVIATION`.

## DB / code review

- **N+1**: none in `AduRepository`/`BufferRepository`/`OrderCalcRepository` — every
  per-store recalculation (`RecalculateAsync` in both `AduService` and `BufferService`,
  `OrderCalcService.CalculateAsync`) bulk-fetches sales/suppliers/schedules/stock/MOQ-USQ
  as dictionaries keyed by ProductId before the per-product loop, then does in-memory math
  only. Confirmed by reading every repository method body, not just the service loops.
- **Indexes**: `product_adu` and `product_buffer` both have a unique composite index on
  `(StoreId, ProductId)` (covers `GetAsync`/`GetByStoreAsync`) plus a `ProductId` index
  (FK). No standalone `TenantId` index, but every query already filters by `StoreId`
  first (which is 1:1 scoped to a tenant), so this isn't a real seq-scan gap the way it
  was for `stock_receipts`/`stock_transfers` in TASK-354. `daily_sales` and
  `supply_schedules` similarly indexed. All four tables already carry `worker_bypass` +
  `provider_bypass` RLS policies via the TASK-343 hotfix migration
  (`20260712175141_AddWorkerBypassRlsPolicy`) — re-verified they're in that migration's
  table list, no gap here.
- **Duplication with Block 3 (Stock)**: no business-logic duplication. `Features/Stock`
  owns batch/expiry/FEFO concerns; `Features/Orders` only sums `ProductStock.Quantity`
  for on-hand totals via its own bulk query (`OrderCalcRepository.GetStockOnHandAsync`) —
  a different aggregation, not a copy of Stock's logic. `AiOrderService` correctly reuses
  `IOrderCalcService.CalculateAsync` instead of re-deriving the formula — good separation.

## Found, not fixed (out of scope for this block, flagged as a background task)

`AiOrderService.GetListAsync` (`Features/AiOrders/AiOrderService.cs`) calls
`_repo.GetByIdAsync(s.Id, ct)` once per suggestion (up to 30) purely to read
`Items.Count` for the list DTO — each call is a full query with `Items`+`Product`
eager-loaded. Real N+1, found while tracing `OrderCalcService` reuse. `AiOrders` is
outside this block's assigned scope (Orders/Adu/Buffer), so left untouched here; spun
off as a separate background task instead of silently fixing or ignoring.

## Tests added

4 new edge-case tests (all pass):
- `AduCalculatorTests.Brand_new_product_with_no_sales_history_is_null_not_an_exception`
  — zero `daily_sales` rows at all (not just sparse data) → all fields null, no throw.
- `CdaBufferCalculatorTests.Cycle_from_misconfigured_empty_schedule_does_not_divide_by_zero`
  — active `SupplySchedule` with empty `DayOfWeek` → falls back to weekly cycle instead
  of `7m / 0`.
- `CdaBufferCalculatorTests.Zero_effective_adu_yields_zero_buffer_not_an_exception`
  — ADU=0 (valid days present, zero units sold) → all-zero buffer, no negative/throw.
- `OrderFormulaTests.Rounding_ladder_is_anchored_at_zero_not_at_moq_KNOWN_SPEC_DEVIATION`
  — documents the MOQ/USQ divergence above.

## Build/test status

`dotnet build`: 0 errors, 1 pre-existing unrelated warning (`MarketplaceServiceTests.cs`,
same as TASK-354).
`dotnet test`: 821/821 green (was 817 after TASK-354; net +4 new tests).
No migrations, no prod changes — review + tests only, per block scope.

## Needs a decision

**MOQ/USQ rounding ladder** (see above) — whether to switch `OrderFormula.Compute` from
"nearest USQ multiple from zero, clamped to MOQ" to the literal `MOQ + k×USQ` ladder
implied by v1-spec §2.7. Low urgency (narrow real-world trigger condition), but a genuine
behavior change either way, not a judgment call I should make silently in an audit block.

## Follow-up (2026-07-15, same day) — MOQ/USQ ladder fixed per user decision

User confirmed directly in chat: switch to the MOQ-anchored ladder exactly as v1-spec
§2.7's example implies. Implemented in `OrderFormula.Compute`
(`backend/ShelfGuard.Application/Features/Orders/OrderCalcService.cs`):

```
var steps = Math.Ceiling((raw - moq) / usq);
var rounded = moq + steps * usq;
```

replacing the old "round to nearest USQ multiple from zero, then clamp up to MOQ"
two-step logic. Semantics: once `raw > moq`, always round **up** to the first ladder step
(`moq + k×usq`) that covers `raw` — never below what was actually needed, and by
construction the result can never fall below MOQ (no separate clamp required anymore).

Updated XML doc comment on `OrderFormula` to describe the ladder and cite this decision.

**Tests** (`ShelfGuard.Tests/Orders/OrderFormulaTests.cs`):
- Replaced `Above_moq_rounds_to_nearest_usq` (pinned the old "nearest, can round down"
  behavior) with `Above_moq_rounds_up_the_moq_anchored_ladder` — MOQ=10, USQ=5 ladder
  (10,15,20,25,30…), three cases including an exact-step boundary.
- Replaced `Rounding_ladder_is_anchored_at_zero_not_at_moq_KNOWN_SPEC_DEVIATION` (which
  asserted the old, wrong 18-for-raw=15 result) with
  `Rounding_ladder_is_anchored_at_moq_not_at_zero`, asserting the spec-correct ladder for
  the exact case the deviation was found on: MOQ=10, USQ=6 → raw=15 → 16 (not 18),
  raw=17 → 22.
- Updated expected values that changed because the underlying formula changed globally,
  not just for MOQ/USQ-misaligned inputs (the old "nearest, symmetric" rounding could
  round *down* below `raw`, which the new "always round up to cover raw" formula no
  longer does):
  - `Usq_rounding_never_dips_below_moq`: MOQ=12, USQ=10, raw=13 → now 22 (was 12;
    property is now guaranteed by construction rather than by an explicit clamp).
  - `Degenerate_moq_usq_default_to_one`: raw=7.3, defaults moq/usq=1 → now 8 (was 7).
  - `Full_spec_example_buffer_plus_bb_minus_stock_minus_transit`: raw=24.97, MOQ=6,
    USQ=6 → now 30 (was 24 — the old rounding under-ordered relative to the computed
    buffer need; the ladder's first step ≥ 24.97 is 30).

**Build/test**: `dotnet build` 0 errors (same 1 pre-existing unrelated warning).
`dotnet test` 821/821 green (net 0 — one test replaced per case, not added; edge-case
coverage from the earlier pass in this task is unchanged).

No other test file in the repo referenced `OrderFormula` or pinned `usq_rounded` output
values, so this was a self-contained change (verified via grep before editing).
