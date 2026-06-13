# TASK-072 — Web: POS dashboard

**Agent:** frontend-developer
**Date:** 2026-06-13
**Status:** review

## What was built

### Feature directory: `frontend/features/pos/`

| File | Description |
|---|---|
| `types.ts` | `ShiftDto`, `SaleDto`, `SaleItemDto`, `ShiftSalesResponse`, `OpenShiftRequest`, `ShiftStatus`, `FiscalStatus`, `PaymentType` |
| `api/pos.ts` | `posApi` — thin wrappers over `api.get/post` for all 4 POS endpoints |
| `hooks/usePos.ts` | `useCurrentShift` (retry skipped on 404), `useShiftSales`, `useOpenShift`, `useCloseShift` |
| `components/ShiftStatusCard.tsx` | Status badge (green/gray/red), opened-at, shift number, totalSales, close button |
| `components/SalesTable.tsx` | Transaction rows: receiptNumber, time, item count, payment type, total, fiscal badge; click → drawer |
| `components/SaleDetailDrawer.tsx` | Full sale breakdown using shared `DetailDrawer` + sub-components |
| `components/FiscalBadge.tsx` | pending_fiscalization → yellow, fiscalized → green, fiscalization_failed → red |
| `components/OpenShiftDialog.tsx` | Store selector (GET /api/stores) + optional opening cash, validated form |

### Page: `frontend/app/(dashboard)/pos/page.tsx`
- No active shift → centered empty state card + "Відкрити зміну" button
- Open shift → `ShiftStatusCard` + `SalesTable`
- Closed shift → Z-report summary (closedAt, totalSales, shiftNumber, fiscalStatus) + closed-shift sales history + "Відкрити нову зміну" button
- Transitional states (Opening/Closing/OpenFailed/CloseFailed) → `ShiftStatusCard` without close button

### Sidebar
- Added `CreditCard` import to Sidebar.tsx
- Added `{ href: "/pos", label: "Каса", icon: <CreditCard size={18} />, roles: CAN_RECEIVE_STOCK }` nav item

## Constraints followed
- CSR only (`"use client"` on all interactive components)
- React Query owns all server state — no Zustand
- 404 on GET /api/pos/shifts/current is treated as "no active shift" (retry suppressed)
- Ukrainian UI text throughout
- Inline styles matching project dark-theme palette (#0D1117 / #161B26 / #1F2937)

## Verification
- `npx tsc --noEmit` — passed with zero errors
