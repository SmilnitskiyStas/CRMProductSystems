# TASK-070 — Mobile: POS screens (tablet)

**Agent:** mobile-developer  
**Date:** 2026-06-13  
**Status:** review  
**Depends on:** TASK-068 (API endpoints)

---

## Summary

Implemented full POS cashier flow for the Expo mobile app (tablet-optimized).
`tsc --noEmit` from `mobile/` — **zero errors**.

---

## Files Created

### Feature layer

| File | Description |
|---|---|
| `mobile/features/pos/types.ts` | PosShift, SaleRequest, SaleResponse, SaleItem, FiscalStatus types |
| `mobile/features/pos/api/posApi.ts` | API functions: getCurrentShift, openShift, closeShift, createSale, getSales |
| `mobile/features/pos/hooks/usePosApi.ts` | useCurrentShift (polls 10s on focus), useOpenShift, useCloseShift, useSale |
| `mobile/features/pos/components/FiscalBadge.tsx` | Green/yellow/red fiscal status badge component |

### Screens (`mobile/app/(app)/pos/`)

| File | Description |
|---|---|
| `pos/_layout.tsx` | Stack navigator (no headers, slide_from_right animation) |
| `pos/index.tsx` | POS Home — shift status, open/close buttons, fiscal badge |
| `pos/scanner.tsx` | Barcode scanner (CameraView) + cart management (qty +/-, remove, subtotal) |
| `pos/payment.tsx` | Payment: Cash/Card toggle, cash input with live change calc, POST /api/pos/sales |
| `pos/receipt.tsx` | Receipt: items, totals, fiscal number/status, «Новий продаж» + «Головна каси» |

### Modified

| File | Change |
|---|---|
| `mobile/app/(app)/_layout.tsx` | Added «Каса» tab with `cash-outline` icon; visible only to cashier/store_manager/director/admin roles via `href: null` for others |

---

## Screen Flow

```
POS Home (pos/index)
  ├── [no shift] → «Відкрити зміну» → POST /pos/shifts/open → reload
  └── [open shift] → «Розпочати продаж» → scanner (shiftId param)
                   → «Закрити зміну» → confirm dialog → POST /pos/shifts/close

Scanner (pos/scanner)
  ├── CameraView with cssInterop (NativeWind fix, same as existing scan.tsx)
  ├── On barcode → GET /products/by-barcode/:barcode → add to cart
  ├── Cart: qty +/-, remove, critical badge (yellow) if status=critical
  └── «Перейти до оплати» → payment (cartJson + shiftId params)

Payment (pos/payment)
  ├── Cart summary (FlatList, scrollEnabled:false)
  ├── Cash/Card toggle
  ├── Cash: TextInput → live change display, insufficient-cash guard
  └── «Провести продаж» → POST /api/pos/sales
        → 423: expired item alert
        → 400/409: error alert
        → success: replace → receipt (resultJson + shiftId params)

Receipt (pos/receipt)
  ├── Transaction ID, items, totals, payment info, change
  ├── FiscalBadge + fiscal number (if available) or «в процесі» note
  ├── «Новий продаж» → replace → scanner (shiftId retained)
  └── «Головна каси» → replace → pos/index
```

---

## Dependencies confirmed

- `expo-camera` ~56.0.7 — installed, CameraView + useCameraPermissions used
- `@tanstack/react-query` ^5 — installed
- `@expo/vector-icons` (Ionicons) — `cash-outline`, `receipt-outline` available
- `nativewind` v4 — cssInterop pattern from existing scan.tsx applied to CameraView
- `axios` — apiClient from `@/lib/api-client` used

---

## Notes

- No new packages needed — all dependencies already in package.json.
- Role check uses `user.role` string from AuthUser (existing type); roles checked: cashier, store_manager, director, admin.
- shiftId is threaded through scanner → payment → receipt via Expo Router params (string encoding via JSON.stringify for cart).
- useFocusEffect in useCurrentShift triggers refetch when screen regains focus; query also polls every 10s while mounted.
- 404 from GET /pos/shifts/current is handled gracefully (returns null = no open shift).
