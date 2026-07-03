# TASK-283 + TASK-284 + TASK-285 — Supplier Self-Service backend (v4.1, ADR-016)

**Date:** 2026-07-02 · **Agent:** backend-developer · **Status:** done
**Depends on:** TASK-282 (migration `V41SupplierSelfService`, `IsOwnerManaged`, business_type `supplier`) — done.

## TASK-283 — роль supplier_admin + онбординг supplier-tenant

- `ShelfGuard.Domain/Constants/AppRoles.cs`: `SupplierAdmin = "supplier_admin"` (+ в `All`).
- `ShelfGuard.Infrastructure/Authorization/AppPolicies.cs`: нова policy `SupplierCabinet`
  (роль тільки `supplier_admin`); supplier_admin НЕ доданий до жодної tenant-staff policy —
  отримує 403 на /api/stock, /api/pos тощо (закріплено тестами).
- `TenantAdminService.CreateTenantAsync`: hook — при `business_type = "supplier"`:
  перший user отримує роль `supplier_admin`; автоматично створюються `Supplier`
  (`TenantId` = new tenant id, `Name` = tenant name) + `SupplierProfile`
  (`IsOwnerManaged = true`, `IsPublic = false`). Все в одному `SaveChangesAsync`
  (одна транзакція). Дефолтні модулі `["marketplace_supplier"]` — вже з TASK-282.
- `ITenantAdminRepository` / `TenantAdminRepository`: `AddSupplierAsync`, `AddSupplierProfileAsync`.

## TASK-284 — SupplierCabinetController

Новий `SupplierCabinetController` (`/api/supplier-cabinet`),
`[Authorize(Policy = SupplierCabinet)]` + `[RequireModule("marketplace_supplier")]`:

| Endpoint | Дія |
|---|---|
| GET /profile | власний профіль + метрики |
| PUT /profile | patch: region, categories, website, delivery_regions, working_hours, payment_terms (IsPublic/Plan не редагуються тут) |
| POST /profile/publish | toggle `IsPublic` |
| GET /items | всі товари (вкл. недоступні) |
| POST /items | створити (реюз `AdminAddSupplierItemAsync`) |
| PUT /items/{id} | patch (новий `AdminUpdateSupplierItemAsync` у MarketplaceService) |
| DELETE /items/{id} | видалити (реюз `AdminDeleteSupplierItemAsync`) |
| GET /reviews | read-only, paginated |
| GET /metrics | read-only |

- Новий `ISupplierCabinetService` / `SupplierCabinetService` (Features/Marketplace):
  кожна операція resolve-ить «мій Supplier» через `GetOwnerManagedProfileAsync(tenantId)`
  (`IsOwnerManaged = true`, tenant RLS) — supplierId ніколи не приймається від клієнта;
  provider-created suppliers (`TenantId = Guid.Empty`) недосяжні (нема owner-managed профілю).
- Items CRUD делегує в `MarketplaceService.Admin*` параметризовані resolved supplierId.
- DI: `Application/DependencyInjection.cs`.

## TASK-285 — reviews hardening + публічні відгуки + rating recalc

- `MarketplaceService.CreateReviewAsync` guards (до дубль-перевірки):
  - supplier не знайдено → 404 (`MarketplaceController` мапить "Supplier not found.");
  - self-review: `supplier.TenantId == reviewer tenant` → 400;
  - reviewer tenant `business_type == "supplier"` → 400;
  - дубль → 409 (як було).
- Після збереження відгуку — синхронний перерахунок `SupplierMetrics.Rating` =
  AVG усіх ratings (округлення 2 знаки); metrics-рядок створюється, якщо відсутній
  (`TenantId` = supplier.TenantId).
- Новий публічний `GET /api/marketplace/suppliers/{id}/reviews` (`[AllowAnonymous]`,
  paginated `PagedResult<PublicSupplierReviewDto>`): rating, comment, created_at,
  `ReviewerName` = tenant display name (без tenant id).
- `MarketplaceRepository`: нові методи — `GetOwnerManagedProfileAsync`,
  `GetSupplierItemsForOwnerAsync`, `GetTenantBusinessTypeAsync`, `GetReviewRatingsAsync`,
  `GetReviewsBySupplierAsync` (+Count), `GetMetricsBySupplierIdAsync` (tracked), `AddMetricsAsync`.
  Cross-tenant читання (ratings/reviews/metrics/supplier lookup) — через існуючий
  `SetProviderRoleAsync` (RLS `provider_bypass`); `GetSupplierByRawIdAsync` тепер теж
  ставить provider role (потрібно reviewer-tenant-у для self-review guard).

## Тести

Нові/оновлені (494/494 green):
- `Marketplace/MarketplaceServiceTests.cs`: guards (self-review, supplier-tenant reviewer,
  not found), recalc (create metrics row 4.00; update existing 4.50), public reviews mapping,
  `AdminUpdateSupplierItemAsync` patch + scope-404.
- `Marketplace/SupplierCabinetServiceTests.cs` (новий): cabinet unavailable без
  owner-managed профілю; делегація items CRUD з resolved supplierId; patch профілю
  не чіпає IsPublic/Plan; publish toggle; metrics fallback.
- `Admin/TenantAdminServiceTests.cs`: supplier-онбординг (Supplier + Profile + supplier_admin,
  один SaveChanges), retail не створює supplier-пару.
- `Authorization/AppPoliciesTests.cs`: SupplierCabinet = тільки supplier_admin;
  supplier_admin відсутній у всіх 11 tenant-staff policies.

## Verify
`dotnet build` — 0 warnings/errors. `dotnet test` — 494/494 passed.

## Next
TASK-286 (frontend supplier cabinet), TASK-287 (marketplace reviews UI) — обидва розблоковані.
