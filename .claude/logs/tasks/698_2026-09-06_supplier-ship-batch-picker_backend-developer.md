# TASK-698 — Supplier ship-flow: pick / split / add batches when shipping a marketplace order

**Date:** 2026-09-06 · **Agent:** backend-developer (main session) · **Status:** review (не комічено, не запушено)

Розширення Phase 3 ship-flow. Без міграції, без RLS-змін. HEAD на старті `02c1a5a6`.

## Проблема

`MarketplaceOrderService.GetShipSuggestionAsync` віддавав лише FEFO-**обрані** партії
(`if (remaining <= 0) break;`) — модалка постачальника ніколи не показувала інші наявні партії,
тож supplier не міг обрати іншу партію / додати другу / підмінити щойно прийняту з іншим терміном.
Бекенд-запис (`ApplyExplicitAllocationsAsync`) вже приймає будь-яку партію того ж supplier-item
на тому ж складі — прогалина була суто в suggestion-ендпоінті + UI.

## Зміни

### Backend

- `MarketplaceOrderService.GetShipSuggestionAsync` (`Features/Marketplace/MarketplaceOrderService.cs`)
  — цикл `foreach (var batch in batches)` більше не `break`-ає на `remaining <= 0`. Для кожної
  партії з `GetFefoOrderedAsync` (вже фільтрує `Quantity>0`, виключає sold_out/archived, сортує за
  терміном): `available = batch.Quantity - claimed[batch.Id]`, skip якщо `<= 0`; якщо `remaining > 0`
  — FEFO-префіл як раніше (`take`, оновлення `claimed`/`remaining`); якщо `remaining <= 0` — `take = 0`.
  Завжди `allocations.Add(...)`. `covered` / `remaining` / shortfall-warning рахуються з FEFO-префілу
  без змін. Гвардія двох рядків з однаковим `SupplierItemId` (`claimed` dict) працює як раніше —
  повністю заклеймлена партія має `available <= 0` і пропускається.
- `IMarketplaceOrderService.GetShipSuggestionAsync` — XML-doc оновлено (тепер повертає повну палітру
  партій; FEFO-обрані з `Qty > 0`, решта `Qty = 0`).
- `Dtos/CooperationDtos.cs` — оновлено XML-doc на `ShipSuggestionAllocationDto` (+ на полі `Qty`) і
  `ShipSuggestionLineDto.Allocations`. **Форма DTO без змін** — поля
  `(SupplierStockId, ExpiryDate, BatchNumber, Available, Qty)`.

### Frontend (`features/supplier-cabinet/components/ShipOrderModal.tsx`, `BatchShipForm`)

- Новий per-line стан `expandedLines: Record<string, boolean>` (скидається в `useEffect` разом із `qty`).
- Гелпер `isActiveAlloc(orderItemId, a)` — партія «активна» коли FEFO префілив (`a.qty > 0`) АБО
  користувач ввів додатну к-сть у це поле; решта — «додаткові».
- Рендер рядків партій: `visibleAllocs` = активні (згорнуто) / усі (розгорнуто); `extraAllocs` —
  приховані. Кнопка (subtle text-button, `#60A5FA`, `fontSize 11`) `+ додати партію (N)` /
  `згорнути` показується лише коли `extraAllocs.length > 0`. Розкриття просто показує вже
  завантажені рядки — жодного вкладеного модала / дропдауна. `line.allocations.length === 0` →
  наявний `shipModalNoBatches` (без змін). Payload-білдер `onShip` вже `.filter(a => a.qty > 0)` —
  введення к-сті в додатковий рядок «просто працює». Shortfall-chip рахується з введених
  користувачем к-стей (`lineCovered`) — без змін.
- `features/supplier-cabinet/types.ts` — без змін (нова палітра лягає в наявну форму).

### i18n (`messages/uk.json`, `messages/en.json`)

- `Dashboard.supplierCabinet.ordersTab.shipModalAddBatch` = `+ додати партію ({count})` / `+ add batch ({count})`
- `Dashboard.supplierCabinet.ordersTab.shipModalHideBatches` = `згорнути` / `collapse`
- parity: 5029 = 5029.

## Тести

- `backend/ShelfGuard.Tests/Marketplace/MarketplaceOrderServiceTests.cs` +2:
  - `GetShipSuggestion_OffersUnpickedBatchesWithZeroQty` — FEFO-потреба покрита 1-ю партією,
    2-га все одно в списку з `qty 0`; `covered`/`shortfall`/`warnings` без змін.
  - `GetShipSuggestion_ListsEveryBatchForTheItem_FullyPartiallyAndNotPicked` — 3 партії →
    3 рядки (повністю / частково / `qty 0`).
  - наявний `GetShipSuggestion_ProposesFefoSplitAndReportsShortfall` проходить без правок.

## Верифікація

- `dotnet build -c Release` — **успішно**, 0 errors.
- `dotnet test -c Release --filter "FullyQualifiedName~MarketplaceOrder|FullyQualifiedName~ShipSuggestion|FullyQualifiedName~SupplierStock"`
  — **171/171 passed**.
- `cd frontend && npx tsc --noEmit` — чисто.
- `npx next lint --file components/ShipOrderModal.tsx` — `✔ No ESLint warnings or errors`.
- `npx next build` — **успішно** (exit 0, усі маршрути зібрані).
- i18n parity 5029 = 5029.

## Не в скоупі / pending

- `openapi.json` regen — **не потрібен**: форма `ShipSuggestionDto` не змінилася (лише семантика
  `allocations` — тепер повна палітра). Якщо головна сесія все одно регенерує — не завадить.
- Не торкався `mobile/`, `worker/`, міграцій, RLS.
- Не комічено (за інструкцією).
