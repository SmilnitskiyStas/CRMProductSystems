# TASK-693 — Supplier portal "Phase 7" follow-ups (3 changes)

Agent: backend-developer. HEAD before/after: `eea1df6b`. Status: review — NOT committed.
Backend + `.claude/docs` only; no `mobile/` / `frontend/` / `worker/`.

## 1 — supplier_inventory + supplier_workforce ON by default (reverses 2026-09-02 default-off)

- `Tenant.DefaultModulesForBusinessType("supplier")` → `["marketplace_supplier",
  "supplier_inventory", "supplier_workforce"]`.
- **Migration `20260905144200_BackfillSupplierModules`** — data-only, EF-generated empty then
  hand-written SQL (style copied from `20260616200319_V4ModulesBackfill`; documented no-op `Down`).
  `Modules` is a `jsonb` array column. SQL used:
  ```sql
  UPDATE tenants
  SET "Modules" = (
      SELECT jsonb_agg(DISTINCT m)
      FROM jsonb_array_elements_text(
          "Modules" || '["supplier_inventory", "supplier_workforce"]'::jsonb
      ) AS m
  )
  WHERE "BusinessType" = 'supplier'
    AND NOT ("Modules" ? 'supplier_inventory' AND "Modules" ? 'supplier_workforce');
  ```
  Idempotent (jsonb_agg DISTINCT dedupes; WHERE guard skips rows already carrying both).
- **Applied to dev DB `:5435/crm`** via `dotnet ef migrations script AddPlatformCategoryItemDefaults
  BackfillSupplierModules --idempotent` → `docker exec … psql -v ON_ERROR_STOP=1`. NOT prod.
  Verified: all 11 real supplier tenants now `["marketplace_supplier","supplier_inventory",
  "supplier_workforce"]`; 4 throwaway "Conflicts RLS Supplier" test rows that had `[]` → got
  `["supplier_inventory","supplier_workforce"]` (expected — WHERE is business_type only).

## 2 — client can cancel until it ships (request #1)

- `MarketplaceOrderService.CancelOrderAsync` gate `Status != New` →
  `Status is not (New or Confirmed)`. No stock reversal (supplier `SupplierStock` only touched at
  ship, Phase 3; a confirmed order consumed nothing — same path as a New cancel).
- `OnlyNewCancellableError` string → `"Скасувати замовлення можна лише до його відвантаження."`
  (const name kept — referenced in service + tests only; no frontend copy — grep clean).
- Supplier-side `UpdateOrderStatusAsync` `AllowedTransitions` unchanged (already had
  `confirmed→cancelled`).
- Doc comments updated: `IMarketplaceOrderService.CancelOrderAsync`, `MarketplaceCooperationController`.

## 3 — track which SUPPLIER employee confirmed / shipped (request #2)

- **Migration `20260905143911_AddMarketplaceOrderSupplierActors`** (EF-generated): `marketplace_orders`
  += `ConfirmedByUserId uuid NULL`, `ConfirmedByUserName varchar(255) NULL`, `ShippedByUserId uuid
  NULL`, `ShippedByUserName varchar(255) NULL`. Plain columns, **no FK / no index** (denormalized
  snapshot pattern — name is the display path, row outlives staff; matches `CreatedByUserName`
  precedent). No RLS change. Applied to dev DB in the same script as #1; 4 columns verified present.
  Snapshot regenerated (diff = only the 4 `b.Property` lines).
- `MarketplaceOrder` entity += 4 props (public set). `AppDbContext`: `HasMaxLength(255)` on the two
  name props.
- **`IMarketplaceOrderService.UpdateOrderStatusAsync` new signature:**
  `Task<(MarketplaceOrderDto? Order, string? Error)> UpdateOrderStatusAsync(Guid supplierTenantId,
  Guid orderId, UpdateMarketplaceOrderStatusDto request, Guid actingUserId, CancellationToken ct = default)`
  — `actingUserId` inserted after `request`.
  - `confirmed` transition: `ConfirmedByUserId = actingUserId`, `ConfirmedByUserName =
    (await _users.GetByIdAsync(actingUserId, ct))?.FullName` (own-tenant read, no override). Guarded
    `actingUserId != Guid.Empty`.
  - legacy `confirmed→shipped` branch now forwards `performedByUserId: actingUserId` into
    `ShipOrderAsync` (was `Guid.Empty`).
- `ShipOrderAsync` — on the actual ship (module-on and legacy path both), when `performedByUserId
  != Guid.Empty`: `ShippedByUserId` + `ShippedByUserName` from `_users.GetByIdAsync(...).FullName`.
- `SupplierCabinetCooperationController.UpdateOrderStatus` — now resolves `ResolveUserId()`
  (null → `Forbid()`) and passes it through.
- `MarketplaceOrderDto` (`CooperationDtos.cs`) += `confirmedByUserId`, `confirmedByUserName`,
  `shippedByUserId`, `shippedByUserName` (after `createdByUserName`, before `Items`). `ToDto` maps
  all 4. Only constructor call site is `MarketplaceOrderService.ToDto`.
- `.claude/docs/api-contracts.md` — `MarketplaceOrderDto` section + `/status` + `/cancel` lines.

## Verification

- `dotnet build -c Release` — clean (1 pre-existing unrelated nullable warning in MarketplaceServiceTests).
- `dotnet test -c Release` (full) — **2350 passed, 0 failed** (integration vs dev Postgres w/ both
  migrations applied). `--filter "MarketplaceOrder|Tenant|Marketplace|RlsCrossTenant"` = 665/665;
  all 69 `~Rls` tests green (RLS audit inclusive).
- New/changed tests (`MarketplaceOrderServiceTests`): `_supplierUserId` fixture + stub; all 8
  `UpdateOrderStatusAsync` calls carry it; cancel theory split into `CancelOrder_BeforeShipping`
  (new+confirmed → success) / `CancelOrder_ShippedOrLater` (error); +3 actor tests
  (`UpdateOrderStatus_Confirm_SnapshotsConfirmingSupplierUser`,
  `UpdateOrderStatus_Ship_LegacyPath_SnapshotsShippingSupplierUser`,
  `ShipOrder_SnapshotsShippingSupplierUser`). `TenantTests` + `TenantAdminServiceTests` supplier
  module assertions updated to the 3-key set.

## Not done / follow-ups
- **Frontend** (separate agent): cancel button now shows for `status ∈ {new, confirmed}` (was
  `new` only); new DTO fields `confirmedByUserName` / `shippedByUserName` for "Підтвердив" /
  "Відвантажив" columns; changed error string
  `"Скасувати замовлення можна лише до його відвантаження."`.
- Prod DB migration — auto-runs on merge/deploy.
- openapi.json regen — shared deferred debt.
- NOT committed (per brief).
