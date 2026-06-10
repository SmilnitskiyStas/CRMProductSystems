---
task_id: BUG-002
date: 2026-06-11
agent: backend-developer (via qa-tester)
status: done
---

# BUG-002 — GET /api/stock/summary → 404

## Problem
Endpoint did not exist. Dashboard stats cards had no data source for
safe/warning/critical/expired counts.

## Fix
Implemented `GET /api/stock/summary` across all layers.

## Files changed

| Layer | File | Change |
|---|---|---|
| Domain | `IStockRepository.cs` | + `GetStatusCountsAsync(storeId, ct)` |
| Application DTOs | `StockDtos.cs` | + `StockSummaryDto` record |
| Application interface | `IStockService.cs` | + `GetSummaryAsync(storeId, ct)` |
| Application service | `StockService.cs` | + `GetSummaryAsync` implementation |
| Infrastructure | `StockRepository.cs` | + `GetStatusCountsAsync` — GROUP BY Status |
| API | `StockController.cs` | + `GET /api/stock/summary` |

## Endpoint

```
GET /api/stock/summary?store_id={guid}   [optional filter]
Authorization: Bearer {token}

Response 200:
{
  "safe": 11,
  "warning": 7,
  "critical": 5,
  "expired": 2,
  "needsVerification": 0,
  "total": 25
}
```

## Verified on production
- Without store filter: `{safe:11, warning:7, critical:5, expired:2, total:25}` ✅
- With store filter: `{safe:2, warning:1, critical:1, expired:1, total:5}` ✅
- Build: 0 Warnings, 0 Errors ✅
