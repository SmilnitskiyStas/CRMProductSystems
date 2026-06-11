---
task_id: TASK-049
date: 2026-06-11
agent: backend-developer
status: done
---

# TASK-049 — Supply Schedules CRUD (v2-spec §9)

## Files
| Layer | File |
|---|---|
| Domain | `Interfaces/ISupplyScheduleRepository.cs` |
| Application | `Features/SupplySchedules/{ISupplyScheduleService, SupplyScheduleService, Dtos}.cs` |
| Infrastructure | `Data/Repositories/SupplyScheduleRepository.cs` |
| Api | `Controllers/SupplySchedulesController.cs` |
| Tests | `Tests/SupplySchedules/SupplyScheduleValidationTests.cs` — 11 tests |

## Endpoints (`[Authorize(AtLeastStoreManager)]`)
```
GET    /api/supply-schedules?store_id&supplier_id → SupplyScheduleDto[]
POST   /api/supply-schedules    → 201 | 400 | 404 | 409 (duplicate active pair)
PUT    /api/supply-schedules/{id} → 200 | 400 | 404 | 409
DELETE /api/supply-schedules/{id} → 204 | 404 (soft — IsActive=false)
```

## Rules
- DayOfWeek: ISO 1–7, non-empty, deduped+sorted on save ([4,2,2] → [2,4])
- OrderLeadDays: 0–60
- One ACTIVE schedule per (store, supplier) — 409 on create/re-activate conflicts
- Soft delete per project convention; inactive schedules drop out of ADU eligibility

## Production e2e
GET (seeded row visible) ✓ · POST normalized days ✓ · duplicate → 409 ✓ ·
day=9 → 400 ✓ · PUT → updated ✓ · DELETE → 204 + isActive=false ✓

## Sprint v2.1 status
TASK-046 ✅ 047 ✅ 048 ✅ 049 ✅ — remaining: TASK-050 (web sales entry page, frontend-developer)
