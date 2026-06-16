# TASK-203 — Mobile: stores → locations rename
**Date:** 2026-06-16
**Agent:** mobile-developer
**Status:** done

## Summary

Completed the stores→locations rename in the mobile app. The previous agent session had partially updated the files, so this task audited all remaining references and fixed them.

## Changes Made

### Files updated by previous agent (verified correct):
- `mobile/features/auth/types.ts` — `storeId` → `locationId` in `AuthUser`
- `mobile/features/dashboard/types.ts` — `storeId/storeName` → `locationId/locationName` in `AiOrderListItem` and `RecentMovement`
- `mobile/features/pos/api/posApi.ts` — `StoreOption` → `LocationOption`; `getStores` → `getLocations`; `openShift` param renamed
- `mobile/features/pos/hooks/usePosApi.ts` — `openShift` mutation param renamed to `locationId`
- `mobile/features/receipt/types.ts` — `destinationStoreId/Name` → `destinationLocationId/Name`
- `mobile/features/stock/api/stockApi.ts` — `storeId` → `locationId` in query params
- `mobile/features/stock/hooks/useStock.ts` — `storeId` → `locationId` in hook params
- `mobile/features/stock/types.ts` — `storeId` → `locationId` in `StockBatch` and `CreateStockBatchRequest`
- `mobile/features/transfers/api/transferApi.ts` — `getStores` → `getLocations`; `store_id` → `location_id` param
- `mobile/features/transfers/hooks/useTransfers.ts` — `useStores` → `useLocations`; param renamed
- `mobile/features/transfers/types.ts` — `fromStoreId/Name` + `toStoreId/Name` → `fromLocationId/Name` + `toLocationId/Name`; `StoreOption` → `LocationOption`
- `mobile/features/write-offs/types.ts` — `storeId/storeName` → `locationId/locationName`
- `mobile/app/(app)/transfers/[id].tsx` — display fields updated
- `mobile/app/(app)/transfers/create.tsx` — all store refs renamed
- `mobile/app/(app)/transfers/index.tsx` — `user?.storeId` → `user?.locationId`

### Files fixed in this session (remaining issues):
- `mobile/features/write-offs/api/writeOffApi.ts` — `storeId` → `locationId`; `store_id` → `location_id` param
- `mobile/features/write-offs/hooks/useWriteOffs.ts` — `storeId` → `locationId`
- `mobile/features/write-offs/components/WriteOffCard.tsx` — `item.storeName` → `item.locationName`
- `mobile/features/transfers/components/TransferCard.tsx` — `fromStoreName/toStoreName` → `fromLocationName/toLocationName`
- `mobile/app/(app)/index.tsx` — `toStoreName/fromStoreName` → `toLocationName/fromLocationName` in `MovementRow`
- `mobile/app/(app)/write-offs/index.tsx` — `user?.storeId` → `user?.locationId`
- `mobile/app/(app)/write-offs/[id].tsx` — `data.storeName` → `data.locationName`
- `mobile/app/(app)/write-offs/create.tsx` — `user?.storeId` → `user?.locationId`; `storeId` → `locationId` in payload
- `mobile/app/(app)/receipt/index.tsx` — `destinationStoreName` → `destinationLocationName`
- `mobile/app/(app)/receipt/[id].tsx` — `destinationStoreName` → `destinationLocationName`
- `mobile/app/(app)/transfers/[id].tsx` — `user?.storeId === data.toStoreId` → `user?.locationId === data.toLocationId`; `fromStoreName/toStoreName` → `fromLocationName/toLocationName`
- `mobile/app/(app)/pos/index.tsx` — `getStores` → `getLocations`; UI strings updated

## Acceptance Criteria — PASSED
- `npx tsc --noEmit` — green (no errors)
- No remaining `/api/stores` calls in mobile/
- No remaining `storeId` / `fromStoreId` / `toStoreId` in type definitions
