# TASK-763 backend: production overview / plan / shipments / warehouse read endpoints

- New: ProductionInsightsController (api/production/{overview,plan,shipments,stock}; [Authorize]+[RequireModule("production")], same as ProductionController), IProductionInsightsService/ProductionInsightsService (+Dtos/ProductionInsightsDtos.cs), IProductionInsightsRepository + ProductionInsightsRepository (read-only, RLS-scoped), DI in both layers.
- TenantRoleTabs: item keys /production/{overview,plan,shipments,warehouse}; Production group order Огляд, Заявки, План і потреба, Виробничі замовлення (renamed from Ордери), Відвантаження, Склад виробництва, Рецепти.
- Tests: ShelfGuard.Tests/Production/ProductionInsightsTests.cs (16). `dotnet test -c Release --filter Production|TenantRole` 204 passed. No migration, no writes.
- Formulas documented in ProductionInsightsService XML docs (toProduce = max(0, requested + forecast - finishedOnHand - openOrderQty); MRP = open orders + toProduce).
