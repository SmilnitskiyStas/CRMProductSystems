---
task_id: TASK-052
date: 2026-06-11
agent: backend-developer
status: done
---

# TASK-052 — Order Formula (v2-spec §3)

## Files
| Layer | File |
|---|---|
| Domain | `Interfaces/IOrderCalcRepository.cs` |
| Application | `Features/Orders/{IOrderCalcService, OrderCalcService, Dtos}.cs` — pure `OrderFormula` |
| Infrastructure | `Data/Repositories/OrderCalcRepository.cs` |
| Api | `Controllers/OrdersController.cs` |
| Tests | `Tests/Orders/OrderFormulaTests.cs` — 9 tests |

## Endpoint (`[Authorize(AtLeastStoreManager)]`)
```
POST /api/orders/calculate  body: {storeId}
→ OrderCalcResult { productsEvaluated, linesToOrder, lines: [
    {productId, productName, barcode, bufferTotal, safetyBuffer, stockOnHand,
     inTransit, quantityRaw, quantityToOrder, moq, usq, rounding}] }
```
Stateless — persistence comes with ai_order_suggestions (phase 5).

## Formula (spec-exact)
```
Raw = BufferTotal + SafetyBuffer − StockOnHand − InTransit
Raw ≤ 0        → 0   (rounding="none")
0 < Raw ≤ MOQ  → MOQ (rounding="moq_floor")
Raw > MOQ      → round to nearest USQ multiple, never below MOQ ("usq_rounded")
```
- **StockOnHand** = SUM(product_stock.Quantity) for store+product
- **InTransit** = SUM(QuantityOrdered) of receipts with Status='draft' into the store
  (project's receipt lifecycle: draft → received/cancelled; draft = ordered, not arrived)
- **MOQ/USQ** from active product_supplier_settings (IsPrimary first; default 1/1)
- ОЗ (one-off) and РТО (MTO reservation) — deferred until MTO support; currently 0
- Event/weather/promo multipliers (spec §3 coefficients) — phases 3-4 hook into Raw

## Production e2e (full chain ADU → Buffer → Order)
```
Вода Моршинська: buf 51.97 + bb 24 − 0 − 0 = 75.97 → ORDER 76
Гречка Жменька:  buf 51.88 + bb 15 − 0 − 0 = 66.88 → ORDER 67
```
Tests 9/9 (covered demand, in-transit reduction, MOQ floor, USQ nearest/midpoint,
never-below-MOQ, degenerate settings, full spec example).
