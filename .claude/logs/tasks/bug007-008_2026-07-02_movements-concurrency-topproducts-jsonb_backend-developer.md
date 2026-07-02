# BUG-007 + BUG-008 — movements DbContext concurrency · top-products jsonb translation

**Date:** 2026-07-02
**Agent:** backend-developer
**Status:** done
**Origin:** store_manager role QA (follow-up to BUG-006 — DateTime Kind fix)

---

## BUG-007 — /api/movements returns 500 (DbContext concurrency)

### Symptom
`GET /api/movements` повертав 500 на prod на кожен виклик (verified: 5/5 requests fail),
незалежно від фільтрів.

### Root cause
`MovementService.GetAsync` (`backend/ShelfGuard.Application/Features/Movements/MovementService.cs`)
запускав два EF Core запити паралельно на одному scoped `AppDbContext`:

```csharp
var itemsTask = _repo.GetAsync(...);
var countTask = _repo.CountAsync(...);
await Task.WhenAll(itemsTask, countTask);
```

DbContext не thread-safe → `InvalidOperationException: "A second operation was started
on this context instance before a previous operation completed"` → 500.

### Fix
Обидва запити виконуються послідовно:

```csharp
var items = await _repo.GetAsync(tenantId, productId, storeId, type, from, to, page, pageSize, ct);
var total = await _repo.CountAsync(tenantId, productId, storeId, type, from, to, ct);
```

### Sweep
Grep `Task\.WhenAll` по всьому `backend/` (Application + Infrastructure + Api + Domain):
це було єдине входження. Інших місць з паралельними запитами на одному DbContext немає.

---

## BUG-008 — /api/analytics/pos/top-products returns 500 (jsonb Barcodes not translatable)

### Symptom
`GET /api/analytics/pos/top-products` повертав 500 на prod навіть після фіксу
DateTime Kind (BUG-006).

### Root cause
`AnalyticsRepository.GetPosTopProductsAsync`
(`backend/ShelfGuard.Infrastructure/Data/Repositories/AnalyticsRepository.cs`, ~line 410)
проєктував усередині SQL-запиту:

```csharp
Barcode = i.Product!.Barcodes.Count > 0 ? i.Product.Barcodes[0] : null,
```

`Item.Barcodes` — `List<string>` mapped to `jsonb` (AppDbContext Item config). Npgsql
не транслює `.Count` та індексер `[0]` над jsonb-mapped списком у SQL → runtime
translation exception → 500.

### Fix
У серверній проєкції вибирається весь список:

```csharp
Barcodes = i.Product!.Barcodes,
```

а перший штрихкод береться client-side після `ToListAsync` (елементи вже матеріалізовані
й групуються в пам'яті одразу нижче):

```csharp
Barcode: g.First().Barcodes?.FirstOrDefault() ?? string.Empty,
```

Референс-патерн: `DailySalesRepository.cs:50-54` (вибирає весь `p.Barcodes`, обробка client-side).

### Sweep
Grep `Barcodes\.Count|Barcodes\[0\]` по backend: ще 10 входжень
(AiOrderService, StockService, PosService ×5, DailySalesService, ReceiptService,
OrderCalcService) — усі в Application-сервісах над уже матеріалізованими entity
(client-side LINQ-to-Objects), не всередині IQueryable-проєкцій. Не зачеплені.

---

## Files changed
- `backend/ShelfGuard.Application/Features/Movements/MovementService.cs` — sequential await замість Task.WhenAll
- `backend/ShelfGuard.Infrastructure/Data/Repositories/AnalyticsRepository.cs` — Barcodes selected whole, first taken client-side
- `.claude/tasks/current.md` — BUG-007, BUG-008 entries (done)

## Verification
- `dotnet build` — green, 0 warnings, 0 errors
- `dotnet test` — 459/459 passed

## Next
- Deploy to prod (main session handles git/deploy)
- Re-run store_manager QA pass: `/api/movements`, `/api/analytics/pos/top-products`
