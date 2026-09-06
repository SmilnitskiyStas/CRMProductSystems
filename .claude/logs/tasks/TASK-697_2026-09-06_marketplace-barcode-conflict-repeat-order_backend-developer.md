# TASK-697 — Marketplace: «Знайдено збіги штрихкодів» модалка не має зʼявлятися на кожне замовлення (BACKEND)

**Date:** 2026-09-06 · **Agent:** backend-developer · **Status:** review (не комічено, не запушено)
**Plan:** `.claude/plans/reactive-honking-wand.md` (sections 1, 2, 4, 5, 6 — frontend section 3 веде інший агент)

## Проблема

`MarketplaceOrderService.CheckCatalogConflictsAsync` / `PlanCatalogOutcomeAsync` позначали позицію
як конфлікт щоразу, коли supplier-item мав хоч один спільний штрихкод із будь-яким власним `Item`
клієнта — без перевірки `Item.SourceSupplierItemId == supplierItem.Id`. Наслідок: модалка
«Прив'язати / Все одно створити» на кожне повторне замовлення вже привʼязаного товару.

## Зміни

### Домен / DTO
- `ShelfGuard.Domain/Entities/MarketplaceOrderCatalogChange.cs` (новий) — persist-record
  `(ItemId, ItemName, IReadOnlyList<string> AddedBarcodes, bool PrimaryChanged, string? NewPrimaryBarcode)`.
- `MarketplaceOrder.CatalogChanges` — `List<MarketplaceOrderCatalogChange>?` (nullable jsonb).
- `CooperationDtos.cs` — `MarketplaceOrderCatalogChangeDto` (та сама форма); `MarketplaceOrderDto`
  += непозиційний обовʼязковий `IReadOnlyList<MarketplaceOrderCatalogChangeDto> CatalogChanges`
  (одразу після `Items`, перед trailing-defaulted-параметрами). Єдине місце конструювання — `ToDto`.

### Репозиторій
- `IItemRepository` / `ItemRepository` — новий `GetForBarcodeMergeAsync(Guid id)` — no-include
  single-row load для гілки «link» / merge (щоб `_items.Update(target)` не позначив
  Category/Segment/DefaultSupplier граф Modified — той самий cross-tenant write-vector, який
  стереже `CreateOrder_link_to_a_foreign_tenant_item_is_rejected...`).
- `GetByAnyBarcodeAsync` — прибрано 3 `.Include` (`Category`/`Segment`/`DefaultSupplier`);
  short-circuit на порожньому вході + `EF.Functions.JsonExistAny` лишилися. 2 prod call-sites
  (обидва в `MarketplaceOrderService`) не читають ці навігації.

### EF-мапінг
- `AppDbContext` MarketplaceOrder блок — `CatalogChanges` `.HasColumnType("jsonb")` +
  `HasConversion` (serialize/deserialize `List<MarketplaceOrderCatalogChange>`) + `ValueComparer`
  — дзеркально до `SupplierItem.Attributes` / `User.TotpRecoveryCodes` (працює під InMemory).

### Сервіс (`MarketplaceOrderService`)
- `CatalogPlan` — `bool IsLink` → `enum CatalogPlanKind { CreateNew, Link, Merge }` +
  `Item? TargetItem`.
- `CheckCatalogConflictsAsync` — `ownMatches.Any(m => m.SourceSupplierItemId == item.Id)` →
  `continue` (case 2, ніколи не конфлікт); інакше перший own-match → модалка (case 3).
- `PlanCatalogOutcomeAsync` — гілка «link» вантажить через `GetForBarcodeMergeAsync`; default-гілка:
  collision із `SourceSupplierItemId == supplierItem.Id` → `CatalogPlanKind.Merge`; collision із
  незвʼязаним Item + `action != "create_new"` → `BarcodeCollisionError`.
- `MergeBarcodes(IReadOnlyList<string> existing, IReadOnlyList<string> supplierAll, string? supplierPrimary)`
  — `internal static` (unit-тестований напряму через `InternalsVisibleTo`). trim/dedupe (ordinal,
  order preserved), append нових supplier-штрихкодів, move-to-front supplier primary; жоден наявний
  не видаляється; повертає `(Merged, Added, PrimaryChanged, Changed)`. «Нічого не змінилось» →
  `Changed == false`.
- `ExecuteCatalogPlanAsync` — `Task<string?>` → `Task<(MarketplaceOrderCatalogChange? Change, string? Error)>`.
  CreateNew без змін; Link+Merge — спільний блок: ownership-check `target.TenantId != clientTenantId`
  **на записі**, `MergeBarcodes`, `needsLinkWrite` (Link + ще не привʼязаний) → idempotent skip коли
  нема чого писати; Link виставляє `SourceSupplierItemId`; повертає `MarketplaceOrderCatalogChange`
  коли `Changed`.
- `CreateOrderAsync` pass-2 — збирає `catalogChanges`; `order.CatalogChanges = catalogChanges.Count > 0 ? … : null`.
- `ToDto` — мапінг `(o.CatalogChanges ?? []).Select(...).ToList()`.
- `IMarketplaceOrderService` — doc-коментарі оновлено.

## Міграція

`dotnet ef migrations add AddMarketplaceOrderCatalogChanges -p ShelfGuard.Infrastructure -s ShelfGuard.Api`
→ `20260906133809_AddMarketplaceOrderCatalogChanges`. Емітований SQL — рівно один AddColumn + snapshot:

```csharp
migrationBuilder.AddColumn<string>(
    name: "CatalogChanges",
    table: "marketplace_orders",
    type: "jsonb",
    nullable: true);
```

Snapshot diff — лише `b.Property<string>("CatalogChanges").HasColumnType("jsonb");`. Жодного model drift.
RLS не потрібно — колонка на наявній `marketplace_orders` (tenant_isolation OR-based + provider_bypass
row-level уже покривають). Без backfill (null = «нічого не змінилось»).

**НЕ застосовано до dev DB** — `dotnet ef database update` заблокував auto-mode classifier
(схемозмінна не-тестова дія). Головна сесія має виконати `ef database update` на dev (і як deploy-крок
на prod).

## Верифікація

- `dotnet build ShelfGuard.sln` — **успішно**, 0 errors, 1 pre-existing warning.
- `dotnet test --filter MarketplaceOrderServiceTests` — **109/109 passed** (8 нових unit + 6 нових
  `MergeBarcodes_*` + оновлені стаби `GetByIdAsync`→`GetForBarcodeMergeAsync` у 5 link-тестах).
- `dotnet test --filter ItemRepositoryGetByAnyBarcode` — **3/3 passed** (live Postgres reachable).
- `dotnet test` (весь) — **2378 passed / 13 failed / 0 skipped**.
  **Усі 13 failed — той самий root cause:** `42703: column "CatalogChanges" of relation
  "marketplace_orders" does not exist` на INSERT (live-Postgres integration-тести, що вставляють
  `marketplace_orders` рядок: `MarketplaceOrderCatalogConflictsRlsIntegrationTests`×2,
  `MarketplaceOrderItemBatchRlsIntegrationTests`×4, `SupplierEmployeeReviewRlsIntegrationTests`×3,
  `SupplierAnalyticsRepositoryIntegrationTests`×3, `MarketplaceProviderBypassScopeRlsIntegrationTests`×1).
  Жодного логічного падіння. `ef database update` на dev → всі 13 зелені.
- Новий integration-тест `CreateOrder_repeat_of_an_already_linked_item_merges_barcodes_and_records_the_change`
  — теж падає лише через відсутню колонку; логіка (seed linked Item + 2-й supplier barcode, прогін
  під client-RLS, assert merge + `current_setting('app.role')=='store_manager'` + persist через
  `NewContext()`) готова.

## RLS / KI-036 дисципліна (plan section 6)

- `GetByAnyBarcodeAsync` — без app-рівневого `TenantId` фільтра; обидва call-sites лишаються в
  `.Where(i => i.TenantId == clientTenantId)`.
- `SourceSupplierItemId == supplierItem.Id` — додатковий фільтр поверх тенантного.
- `ExecuteCatalogPlanAsync` — `target.TenantId != clientTenantId` перевіряється **на записі** (pass 1
  і pass 2 розділені циклом).
- Ціль merge вантажиться без navigation includes (`GetForBarcodeMergeAsync`).
- `ITenantSessionOverride` не потрібен — запис у власні `items` клієнта (звичайний `tenant_isolation`).

## Файли

- `backend/ShelfGuard.Domain/Entities/MarketplaceOrderCatalogChange.cs` (новий)
- `backend/ShelfGuard.Domain/Entities/MarketplaceOrder.cs`
- `backend/ShelfGuard.Domain/Interfaces/IItemRepository.cs`
- `backend/ShelfGuard.Application/Features/Marketplace/MarketplaceOrderService.cs`
- `backend/ShelfGuard.Application/Features/Marketplace/IMarketplaceOrderService.cs`
- `backend/ShelfGuard.Application/Features/Marketplace/Dtos/CooperationDtos.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/ItemRepository.cs`
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs`
- `backend/ShelfGuard.Infrastructure/Migrations/20260906133809_AddMarketplaceOrderCatalogChanges*.cs` (новий)
- `backend/ShelfGuard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`
- `backend/ShelfGuard.Tests/Marketplace/MarketplaceOrderServiceTests.cs`
- `backend/ShelfGuard.Tests/Infrastructure/MarketplaceOrderCatalogConflictsRlsIntegrationTests.cs`
- `backend/ShelfGuard.Tests/Pos/PosServiceTests.cs`, `backend/ShelfGuard.Tests/Pos/FiscalizationRetryTests.cs`
  (fake `IItemRepository` += `GetForBarcodeMergeAsync`)

## Pending (не для цього агента)

- `dotnet ef database update` на dev (розблоковує 13 integration-тестів) + як deploy-крок на prod.
- Frontend (plan section 3): `types.ts`, тост у `SupplierOrderCart.tsx`, рядок «Зміни в каталозі» в
  `orders/page.tsx`, ~6 i18n-ключів × 2 локалі.
- `openapi.json` regen (`MarketplaceOrderDto` форма змінилася).
