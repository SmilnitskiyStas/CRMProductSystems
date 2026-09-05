# TASK-695 — Supplier portal Phase 8: team performance + per-employee buyer ratings (backend)

**Agent:** backend-developer · **Status:** review · не комічено · не задеплоєно

## Що зроблено

### Міграції (застосовано на dev `:5435/crm`, НЕ prod)
- `20260905165934_AddMarketplaceOrderConfirmedAt` — `marketplace_orders.ConfirmedAt timestamptz?`.
  `MarketplaceOrderService.UpdateOrderStatusAsync` стемпить його разом із `ConfirmedByUserId` на
  переході new→confirmed. Без backfill (історичні confirmed-замовлення → NULL; rollup вікнує їх
  за `CreatedAt`, лише з timing-середніх вони випадають). `MarketplaceOrderDto` НЕ чіпав.
- `20260905170057_AddSupplierEmployeeReviews` — таблиця `supplier_employee_reviews`
  (`SupplierTenantId, ClientTenantId, SupplierUserId, SupplierUserName snapshot, RatedByUserId,
  RatedByName snapshot, Rating short, Comment varchar(2000)?, Source varchar(10), OrderId? FK SET
  NULL, ChatSessionId? FK SET NULL, Created/UpdatedAt`). Partial unique
  `(SupplierUserId, ClientTenantId, OrderId) WHERE OrderId IS NOT NULL` та те саме для
  `ChatSessionId`; index `(SupplierTenantId, SupplierUserId, CreatedAt)`.
  **RLS — ADR-033 split:** `tenant_isolation` USING/WITH CHECK `ClientTenantId = app.tenant_id
  AND ClientTenantId <> SupplierTenantId` (buyer пише; `<>`-guard закриває self-rating постачальником),
  `supplier_read` FOR SELECT `SupplierTenantId = app.tenant_id`, + `provider_bypass`/`worker_bypass`.
  Triad-audit підхоплює таблицю автоматично (green).

### Ratings (buyer side)
- `SupplierEmployeeReview` entity + EF config; `ISupplierEmployeeReviewRepository` +
  `SupplierEmployeeReviewRepository`.
- `ISupplierEmployeeReviewService` + impl (новий сервіс, не в `MarketplaceOrderService`):
  - `RateOrderManagerAsync` — upsert рейтингу `order.ConfirmedByUserId`; вимагає order.delivered +
    ConfirmedByUserId != null; buyer пише на власній RLS (WITH CHECK на ClientTenantId).
  - `RateChatParticipantAsync` — upsert; єдина валідація `supplierUserId` = він реально надіслав
    ≥1 повідомлення в треді з `SenderTenantId == supplierTenantId` (ім'я — snapshot звідти).
  - `GetOrderManagerRatingAsync`, `GetMyChatParticipantRatingsAsync` (для «вже оцінено ★★★★»).
- 4 endpoint-и на `MarketplaceCooperationController` (`api/marketplace`, `[RequireModule("marketplace")]`).

### Team performance (supplier side)
- `ISupplierTeamPerformanceRepository` + impl (in-memory rollup, як `SupplierAnalyticsRepository`):
  broad-pull orders / finalized-receipt flags / chat messages / employee reviews на supplier-сесії.
- `ISupplierTeamPerformanceService` + impl — рядок на staff-user (`GetStaffAsync`), повний набір
  KPI + `PeriodMetricDto` дельти (ordersShipped, onTimeDeliveryRate, avgBuyerRating) vs попереднє
  рівне вікно. Вікно cap 366д, default 30д (у контролері).
- `SupplierCabinetTeamPerformanceController` — `GET team-performance?from=&to=` + `GET team/{userId}/reviews`.
  Gate: `SupplierCabinet` + `[RequireModule("marketplace_supplier")]` + `SupplierPermissions.StaffManagement`.
- DI: Application + Infrastructure `DependencyInjection.cs`.

### Discrepancy-signal
`MarketplaceOrderReceiptItem.DiscrepancyNotes` (non-null, non-whitespace). «Clean» receipt =
finalized (`Status == "received"`) і жодного item з непорожнім `DiscrepancyNotes` — те саме, що
відкриває auto-ticket у `MarketplaceOrderReceiptService` (TASK-599).

## Тести
- `SupplierEmployeeReviewServiceTests` (11) — rate-manager happy/upsert/non-delivered/no-manager/
  foreign/range; chat rate happy/участь-не-в-треді/no-thread/forge-from-own-message.
- `SupplierTeamPerformanceServiceTests` (10) — per-actor counts, timing means (+null), on-time
  rate, discrepancy-free rate, chat counts+median, buyer-rating avg+delta, ordersShipped delta,
  staff-without-activity, 366д clamp.
- `SupplierEmployeeReviewRlsIntegrationTests` (real Postgres) — buyer пише+читає; supplier читає
  але INSERT (both attributions) → 42501, UPDATE/DELETE → 0 rows; unrelated tenant + RESET → 0;
  policy-shape.
- `MarketplaceOrderServiceTests` — додав `ConfirmedAt` assert у confirm-snapshot тест.

**Build:** `dotnet build -c Release` чисто (0 warn).
**Tests:** `dotnet test -c Release --filter "…SupplierEmployee|…TeamPerformance|…MarketplaceOrder|…Marketplace|…RlsCrossTenant"` → **436/436**, RLS-audit green.

## Далі (не в цьому таску)
Фронт — окремим агентом: buyer rate-manager (після delivered) + chat rate-participant + supplier
team-performance view. Docs: `api-contracts.md` оновлено. openapi.json regen. ADR-039 нотатка в
`decisions.md`.
