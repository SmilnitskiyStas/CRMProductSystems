---
task_id: TASK-053
date: 2026-06-11
agent: frontend-developer
status: done
---

# TASK-053 — Web: orders page + CDA buffer funnel

## Files
```
app/(dashboard)/orders/page.tsx           — store selector, "Сформувати замовлення", pipeline stats
features/orders/
  types.ts                                — OrderLine (with buffer zones), OrderCalcResult
  api/orders.ts                           — recalculateAdu, recalculateBuffers, calculate
  hooks/useOrders.ts                      — useGenerateOrder: chained mutation ADU → buffers → order
  components/BufferFunnel.tsx             — red|yellow|green bar + current-position marker
  components/OrderLinesTable.tsx          — lines table, ORDER badge with MOQ/USQ tag, "покрито"
components/layout/Sidebar.tsx             — + "Замовлення" (Calculator icon, AT_LEAST_STORE_MANAGER)
backend OrderLineDto                      — + bufferGreen/Yellow/Red (funnel needs zones)
```

## UX
- One button runs the full order-day chain (spec §2 rule 1): ADU recalc → buffer rebuild
  → order calculation; toast + 3 stat cards (ADU done / buffers / lines to order)
- Funnel: zone widths ∝ share of total buffer; marker = stock+inTransit position,
  colored by zone it falls into; tooltip with exact zone values
- Covered lines (order 0) dimmed with green "покрито"; rounding source shown as
  MOQ/USQ tag inside the order badge

## Verification
- `tsc --noEmit` clean; backend build 0/0
- Production: /orders → 200; calculate returns zones
  (Вода Моршинська G 36.03 / Y 5.02 / R 10.92 → ORDER 76)

## Sprint v2.2 «Buffer & Formula» — COMPLETE
TASK-051 ✅ 052 ✅ 053 ✅ → next: Phase 3 (Events & Weather, TASK-054..056)
