---
task_id: TASK-048
date: 2026-06-11
agent: backend-developer
status: done
---

# TASK-048 — ADU Calculation Engine (v2-spec §1)

## Files
| Layer | File |
|---|---|
| Domain | `Interfaces/IAduRepository.cs` |
| Application | `Features/Adu/{IAduService, AduService, Dtos/AduDtos}.cs` — incl. pure `AduCalculator` |
| Infrastructure | `Data/Repositories/AduRepository.cs` |
| Api | `Controllers/AduController.cs` |
| Tests | `Tests/Adu/AduCalculatorTests.cs` — 9 tests |

## Endpoints (`[Authorize(AtLeastStoreManager)]`)
```
GET  /api/adu/{storeId}/{productId}  → AduDto | 404 (not calculated yet)
POST /api/adu/recalculate            → RecalculateResult {productsProcessed, withEffectiveAdu, insufficientData}
     body: {storeId}
```

## Business rules implemented
- **Valid day:** not promo, not anomaly, (sold > 0 OR end-of-day stock > 0).
  Out-of-stock zero days are invalid (don't drag ADU down); zero-sale days WITH stock
  on shelf are valid (real zero demand). Today never counts (incomplete).
- **Eligibility:** MTS + active product + default supplier has active supply schedule
  for the store + no active discount today.
- **Group assignment** (decision — spec says "company settings", none exist yet, so
  derived from data density, tightest window first):
  ≥20 valid/30d → group 3 (effective=ADU30) · ≥15/60d → group 2 (ADU60) ·
  ≥10/90d → group 1 (ADU90) · else no group, effective=null.
- ADU = SUM(sold on valid days) / COUNT(valid days), per window, round 4 dp.
- Recalculate upserts product_adu (UNIQUE store+product); one batched sales query.

## Spec gaps noted
- "Дні з оптовими продажами" — no wholesale flag exists; covered by is_anomaly
  (mark-anomaly endpoint). Revisit if POS integration adds order-type data.

## Verification
- Unit: 9/9 (groups 1/2/3, insufficient data, promo/anomaly exclusion, zero-day
  semantics, today exclusion, cumulative windows)
- Production e2e: seeded supply schedule + 30 days × 2 products →
  recalculate `{processed: 8, withEffectiveAdu: 2, insufficientData: 6}`;
  GET → Вода Моршинська: adu_effective 10.9167, group 3, 30 valid days ✓

## Next
TASK-051 (CDA buffer) consumes adu_effective + supply_schedules lead/cycle times.
