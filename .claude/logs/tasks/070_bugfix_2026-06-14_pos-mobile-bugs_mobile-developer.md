# TASK-070 Bugfix — Mobile POS Critical Bugs

**Date:** 2026-06-14
**Agent:** mobile-developer
**Status:** done

## Summary

Fixed three critical bugs in the mobile POS feature that prevented the Каса tab from appearing and the shift from being opened.

## Bugs Fixed

### Bug 1 — Каса tab hidden (role case mismatch)
**File:** `mobile/app/(app)/_layout.tsx`

`CASHIER_ROLES` used lowercase strings (`cashier`, `store_manager`, etc.), but the API returns PascalCase roles (`Cashier`, `StoreManager`, `Director`, `Admin`). The `includes()` check always returned `false`, so `href` was always `null` and the tab was invisible.

**Fix:** Updated `CASHIER_ROLES` to `['Cashier', 'StoreManager', 'Director', 'Admin']`.

---

### Bug 2 — Open shift shown as "Зміна не відкрита" (status case mismatch)
**Files:**
- `mobile/features/pos/types.ts` — `ShiftStatus` type
- `mobile/app/(app)/pos/index.tsx` — status comparison

Backend `ShiftDto.Status` is PascalCase: `Opening | Open | OpenFailed | Closing | Closed | CloseFailed`. The UI compared against lowercase `'open'` which always failed.

**Fix:**
- Updated `ShiftStatus` type to PascalCase union: `'Opening' | 'Open' | 'OpenFailed' | 'Closing' | 'Closed' | 'CloseFailed'`
- Updated condition from `shift.status === 'open'` to `shift.status === 'Open' || shift.status === 'Opening'`

---

### Bug 3 — `openShift()` returned 400 (missing storeId)
**Files:**
- `mobile/features/pos/api/posApi.ts` — added `getStores()`, updated `openShift(storeId)`
- `mobile/features/pos/hooks/usePosApi.ts` — updated `useOpenShift` mutation signature
- `mobile/app/(app)/pos/index.tsx` — updated `handleOpenShift` to fetch stores and pass storeId

Backend `OpenShiftRequest` requires `Guid StoreId`. The old call sent an empty body → 400 Bad Request.

**Fix:**
- Added `getStores()` function calling `GET /stores`
- Added `StoreOption` interface (`{ id: string; name: string }`)
- `openShift(storeId: string)` now POSTs `{ storeId }` in body
- `useOpenShift` mutation accepts `storeId: string` parameter
- `handleOpenShift` in `pos/index.tsx`:
  - Fetches stores list first
  - If 0 stores → shows error alert
  - If 1 store → opens automatically
  - If multiple stores → shows `Alert.alert` picker with one button per store

## Additional Checks

- `scanner.tsx` — no status/role bugs found; uses only camera/barcode scan logic
- `payment.tsx` — no status bugs; no shift status checks present
- `receipt.tsx` — uses `FiscalStatus` only (snake_case from backend, matches existing `FiscalBadge` CONFIG keys)
- `FiscalBadge.tsx` — snake_case keys match backend output, no change needed

## Verification

`npx tsc --noEmit` in `/mobile` — clean, zero errors.
