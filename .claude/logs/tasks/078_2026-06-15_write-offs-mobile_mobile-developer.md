# TASK-078 — Mobile: Write-offs screen
**Agent:** mobile-developer
**Date:** 2026-06-15
**Status:** done

## Що зроблено

### Нові файли

**Feature layer:**
- `mobile/features/write-offs/types.ts` — WriteOff, WriteOffItem, WriteOffStatus, WriteOffReason + label/color maps
- `mobile/features/write-offs/api/writeOffApi.ts` — getWriteOffs, getWriteOff, createWriteOff, approveWriteOff, rejectWriteOff
- `mobile/features/write-offs/hooks/useWriteOffs.ts` — React Query hooks (useWriteOffs, useWriteOff, useCreateWriteOff, useApproveWriteOff, useRejectWriteOff)
- `mobile/features/write-offs/components/WriteOffCard.tsx` — картка списання для FlatList

**Screens:**
- `mobile/app/(app)/write-offs/index.tsx` — список списань (GET /api/write-offs?store_id=), FAB «+»
- `mobile/app/(app)/write-offs/[id].tsx` — деталі: позиції, метадані, approve/reject для менеджерів (Alert confirmation)
- `mobile/app/(app)/write-offs/create.tsx` — форма: CameraView скан → додає товар до draft items, qty stepper ±, reason picker modal, submit

### Оновлені файли
- `mobile/app/(app)/_layout.tsx` — hidden tabs для write-offs/index, write-offs/[id], write-offs/create
- `mobile/app/(app)/index.tsx` — секція «Швидкі дії» (Списання, Прийомка, Нове списання)
- `mobile/app/(app)/scan.tsx` — кнопка «Списати товар» у result bottom sheet (поряд із «Переглянути залишки»)

## Flows

**Worker flow:** Dashboard → «Нове списання» / Скан → «Списати товар» → create.tsx → scan barcode → qty ± → вибрати причину → «Створити списання» → API POST /api/write-offs → статус pending_approval

**Manager flow:** Dashboard → «Списання» → список → detail → «Затвердити» / «Відхилити» → Alert confirm → API PUT /api/write-offs/{id}/approve|reject → stock deducted

## API endpoints used
- GET /api/write-offs?store_id= (CanViewStock)
- GET /api/write-offs/{id}
- POST /api/write-offs (CanReceiveStock)
- PUT /api/write-offs/{id}/approve (AtLeastStoreManager)
- PUT /api/write-offs/{id}/reject (AtLeastStoreManager)

## Verify
- `npx tsc --noEmit` — ✅ 0 errors
