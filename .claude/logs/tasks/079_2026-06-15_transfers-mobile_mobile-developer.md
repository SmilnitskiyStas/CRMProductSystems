# TASK-079 — Mobile: Transfers screen
**Agent:** mobile-developer
**Date:** 2026-06-15
**Status:** done

## Що зроблено

### Нові файли

**Feature layer:**
- `mobile/features/transfers/types.ts` — Transfer, TransferItem, DraftTransferItem, StoreOption, статуси/кольори/лейбли
- `mobile/features/transfers/api/transferApi.ts` — getTransfers, getTransfer, createTransfer, confirmTransfer, cancelTransfer, getStores
- `mobile/features/transfers/hooks/useTransfers.ts` — React Query hooks (useTransfers, useTransfer, useStores, useCreateTransfer, useConfirmTransfer, useCancelTransfer)
- `mobile/features/transfers/components/TransferCard.tsx` — картка переміщення: звідки → куди, статус badge, позицій, дата

**Screens:**
- `mobile/app/(app)/transfers/index.tsx` — список переміщень (store_id з user.storeId), FAB «+»
- `mobile/app/(app)/transfers/[id].tsx` — деталі: маршрут, позиції, confirm/cancel дії
- `mobile/app/(app)/transfers/create.tsx` — повний flow: scan → batch picker → qty stepper (з max = available) → store picker → notes → submit

### Оновлені файли
- `mobile/app/(app)/_layout.tsx` — hidden tabs для transfers/index, transfers/[id], transfers/create
- `mobile/app/(app)/index.tsx` — quick action «Переміщення» (замінює «Прийомка» в quick actions)

## Create flow (детально)
1. Вибір destination store (store picker modal, GET /api/stores, фільтрує поточний магазин)
2. Scan barcode → GET /api/products/by-barcode/{barcode} → GET /api/stock?storeId= (клієнтський фільтр по productId)
3. Batch picker modal: список доступних партій з кількістю → вибір партії
4. Qty stepper: ± з обмеженням availableQty (не можна передати більше ніж є)
5. Можна додавати кілька товарів (scan знову)
6. Submit → POST /api/transfers → stock одразу списується з fromStore, статус in_transit

## Confirm flow
- Видно кнопку «Підтвердити отримання» якщо user.storeId === data.toStoreId і статус in_transit
- Confirm → POST /api/transfers/{id}/confirm → новий ProductStock з'являється в toStore

## API endpoints used
- GET /api/transfers?store_id=
- GET /api/transfers/{id}
- POST /api/transfers
- PUT /api/transfers/{id}/confirm (CanReceiveStock)
- PUT /api/transfers/{id}/cancel (AtLeastStoreManager)
- GET /api/stores
- GET /api/stock?storeId=
- GET /api/products/by-barcode/{barcode}

## Verify
- `npx tsc --noEmit` — ✅ 0 errors
