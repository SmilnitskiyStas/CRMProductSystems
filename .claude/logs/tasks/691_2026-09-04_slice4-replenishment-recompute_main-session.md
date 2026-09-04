# TASK-691 — Слайс 4: авто-буфери з продажів

**Date:** 2026-09-04 · **Agent:** main session · **Plan:** `.claude/plans/catalog-form-buffers-promo.md` (Слайс 4)
**State:** done · not yet deployed at time of writing

## Що зроблено

### 4a + 4b — нічний worker-джоб `replenishment-recompute.job.ts` (новий)
- Cron `20 0 * * *` (00:20 — після stock-snapshot 00:10, задовго до ai-order 05:00).
- Чистий SQL через `db` pool, `SET app.role='worker'` першим стейтментом, усі тенанти —
  патерн `stock-snapshot.job.ts` / `supplier-metrics-recompute.job.ts` (НЕ callback-in-API:
  worker-акаунт single-tenant, до всіх тенантів через API не дотягнутися).
- **Phase 1** — `pos_transactions`+`pos_transaction_items` за останні 3 дні → `daily_sales`
  (`Source='pos'`). Статуси `fiscalized`+`pending_fiscalization`. `IsPromoDay` з активної
  promo/campaign знижки на день. `ON CONFLICT (LocationId,ProductId,Date) DO UPDATE …
  WHERE daily_sales."Source"='pos'` — ручні/import-рядки НЕ перезаписуються.
- **Phase 2** — `daily_sales` → `product_adu`: SQL-порт `AduRepository.GetEligibleProductIdsAsync`
  + `AduCalculator.Compute` (вікна 30/60/90, valid-day = не promo/anomaly/(sold>0||eod>0)/date<today,
  групування за щільністю даних 30d≥20→3, 60d≥15→2, 90d≥10→1).
- **Phase 3** — `product_adu` → `product_buffer`: SQL-порт `CdaBufferCalculator`
  (green=ADU·(LT+OC), yellow=ADU·OC·CV, red=ADU·LT·1.0; LT/OC зі `supply_schedules`;
  CV = `stddev_pop/avg` valid-day sales у вікні групи, clamp [0.2,1.5], <2 семпли→0.2).
- Parity-коментарі додано в `AduService.cs` + `BufferService.cs` (⚠️ PARITY: тримати синхронно).
  Прийняті розбіжності (задокументовані в джобі): режим округлення (PG half-away vs C# banker's,
  <0.01 ефект), zero-sales продукти (C# пише all-null рядок, SQL — жодного; downstream-еквівалент).

### 4c — `suggestedMin/Max/SafetyBuffer` у формі товару
- `IItemRepository.GetBufferSuggestionsAsync(productIds)` → `ItemBufferSuggestion` (MAX по магазинах:
  min=red+yellow, max=total, safety=red, +aduEffective, +calculatedAt). Реалізація в `ItemRepository`
  (2 запити, group-in-memory як `GetPromoStatesAsync`). Порожньо коли немає `product_buffer`.
- `ItemDto` += `SuggestedMinStock/MaxStock/SafetyBuffer/AduEffective/BufferCalculatedAt` (nullable,
  тільки пейджед/list — як promo-поля). `ItemService.LoadBufferSuggestionsAsync` + `ToDto` overload.
- 2 hand-fake `IItemRepository` (Pos/Fiscalization) + новий тест `GetPagedAsync_MapsBufferSuggestionIntoDto`.
- `ProductForm.tsx` — секція «Управління запасами», edit-mode, коли є `bufferCalculatedAt`:
  бокс «Система пропонує за продажами (оновлено DD.MM) — мін N · макс M · буфер безпеки K» +
  кнопка «Застосувати» (`setValue` min/max/safety, `shouldDirty`). **Не перезаписує автоматично.**

### 4d — примітка про акцію у формі
- `ProductForm.tsx` — та ж секція: якщо `product.promoState` → «🏷 Активна акція −N% / Акція за N дн —
  обсяг замовлення (буде) збільшено автоматично». Дані з promo-полів Slice 3.

### i18n
uk+en: `Dashboard.inventory.form.bufferSuggestionTitle/Values/Apply`, `promoNoteActive/Upcoming`.

## Верифікація
- `dotnet build` clean; `dotnet test` **2337/2337**.
- worker `tsc --noEmit` clean; frontend `tsc`/eslint(нові файли чисто)/i18n parity **4970==4970**.
- **SQL перевірено на dev-БД проти C#-формули** (контрольований 30-денний seed, store f9022458):
  - const 10/день → ADU30=10.0000, group 3, vd30=30 · buffer g55 y7 r20 t82 (LT2 OC3.5)
  - alt 5/15 → ADU30=10.0000 · CV=0.5 · buffer g55 y17.50 r20 t92.50
  - обидва точно збіглися з ручним розрахунком за `CdaBufferCalculator`.
  - Phase 1 проти 4736 реальних pos_transactions: агрегати збіглися з plain GROUP BY,
    ідемпотентність (re-run → 0 нових рядків), ручний рядок пережив re-run.
- **E2E форма** (dev, seeded 1 buffer+promo): бокс пропозиції + кнопка «Застосувати» заповнює
  min/max; примітка акції рендериться. Console без нових помилок. Весь seed прибрано з dev-БД.

## Схема
Без міграції. Пише лише в наявні `daily_sales` / `product_adu` / `product_buffer`.
