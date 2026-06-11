---
task_id: TASK-051
date: 2026-06-11
agent: database-engineer + backend-developer
status: done
---

# TASK-051 — CDA Buffer Engine (v2-spec §2)

## Files
| Layer | File |
|---|---|
| Domain | `Entities/ProductBuffer.cs`, `Interfaces/IBufferRepository.cs` |
| Infrastructure | Migration `V2ProductBuffer` (table + RLS), `Data/Repositories/BufferRepository.cs` |
| Application | `Features/Buffer/{IBufferService, BufferService, Dtos}.cs` — incl. pure `CdaBufferCalculator` |
| Api | `Controllers/BufferController.cs` |
| Tests | `Tests/Buffer/CdaBufferCalculatorTests.cs` — 9 tests |

## Endpoints (`[Authorize(AtLeastStoreManager)]`)
```
GET  /api/buffer/{storeId}/{productId} → BufferDto | 404
POST /api/buffer/recalculate           → {buffersCalculated, skippedNoSchedule}
```

## Formulas (spec-exact)
```
Green  = ADU × (LT + OC)         — full-cycle demand
Yellow = ADU × OC × variability  — demand unevenness
Red    = ADU × LT × safety       — safety stock
Total  = G + Y + R
```
- **LT/OC dynamic from supply schedule** (rule 3): OC = 7 / deliveries-per-week
  (Mon/Wed/Fri → 2.3d); LT = schedule.OrderLeadDays (default 1).
- **Variability** = coefficient of variation (σ/μ) of valid-day sales over the
  product group's ADU window, clamped to [0.2, 1.5]. Spec gives no formula —
  CV is the standard CDA approach; clamps protect against thin data.
- **Safety factor** = 1.0 constant for now (spec range 0.5–1.5, per-tenant
  setting planned).
- Buffer rows are upserted only on explicit recalculate (rule 1: order-day only,
  frozen between orders).
- Eligibility: product has effective ADU + active schedule for its default supplier.

## Production e2e
- `product_buffer` created with RLS (tenant_isolation + provider_bypass) ✓
- recalculate → `{buffersCalculated: 2, skippedNoSchedule: 0}` ✓
- Вода Моршинська (ADU 10.9167, LT 1, OC 2.3, CV 0.2):
  Green 36.03 / Yellow 5.02 / Red 10.92 / **Total 51.97** — hand-checked ✓

## Next
TASK-052 (order formula) consumes BufferTotal + product.SafetyBuffer + stock + in-transit.
