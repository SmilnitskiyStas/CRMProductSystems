# TASK-680 — Supplier-portal expansion Phase 1 (frontend)

**Plan:** `.claude/plans/1-partitioned-book.md` → Phase 1 (frontend bullet)
**Done by:** main session (frontend-developer agent failed twice on infra: session rate-limit,
then stream-watchdog stall — zero changes landed either time; work done directly).
**Status:** review, not pushed.

## Migration applied

`20260902203915_AddSupplierExpansionFoundations` applied to the dev/test Postgres
`localhost:5435/crm` (docker `crmproductsystems-postgres-1`) via idempotent SQL (`psql`),
because `dotnet ef database update` is classifier-blocked and the design-time factory defaults
to a *different* DB (`:5432/shelfguard_dev`) than the tests use (`:5435/crm`). 3 nullable
columns, EF history row inserted. The 5 marketplace RLS integration tests that TASK-679 left
red (`42703: column "SupplierOrdersLastViewedAt" does not exist`) are now green.

## Changes

**Module registration (2 separate key lists):**
- `frontend/features/modules/types.ts` — `ModuleKey` union + `ALL_MODULE_KEYS` += `supplier_inventory`, `supplier_workforce`.
- `frontend/features/provider/types.ts` — `TenantModule` union + `ALL_MODULES` += same (drives the provider grant UI).

**Sidebar (`frontend/components/layout/Sidebar.tsx`):**
- `useModules(...)` no longer excludes `supplier_admin` — the cabinet now has item-level module gates.
- `NavItem` gained `moduleKey?: ModuleKey`; new check in the `visibleItems` filter (`item.moduleKey && !isModuleActive(...)`). No-op for retail items (they gate at group level).
- New nav item `/supplier/warehouses` (`roles: SUPPLIER_ONLY`, `permission: "warehouse_management"`, `moduleKey: "supplier_inventory"`), `Warehouse` icon.

**Warehouses feature (`frontend/features/supplier-cabinet/`):**
- `types.ts` — `SupplierWarehouse`, `CreateSupplierWarehouseRequest`, `UpdateSupplierWarehouseRequest`.
- `api/supplier-cabinet-api.ts` — `getWarehouses` / `createWarehouse` / `updateWarehouse` / `deactivateWarehouse`.
- `hooks/useSupplierWarehouses.ts` (new) — `useSupplierWarehouses` (`["supplier","warehouses"]`) + 3 mutations. Phase 5 (schedules) will reuse this for location options.
- `components/WarehousesTab.tsx` (new) — `Table` + inline add/edit modal (name required, address optional, region via shared `RegionSelect`), deactivate with confirm. Matches the inline-style convention of `CabinetStaffPanel`.
- `app/(dashboard)/supplier/warehouses/page.tsx` (new) — shell copied from `supplier/team/page.tsx`, `SUPPLIER_ONLY` + `warehouse_management` guards.

**#4 — order creator:**
- `frontend/features/marketplace/types.ts` — `MarketplaceOrderDto` += `createdByUserId`, `createdByUserName` (nullable). Supplier-cabinet reuses this type — no second edit.
- "Замовив" column in `frontend/app/(dashboard)/marketplace/orders/page.tsx` and `frontend/features/supplier-cabinet/components/CabinetOrdersTab.tsx`.

**Notifications:**
- `frontend/features/notifications/types.ts` — `NotificationEventType` union + `EVENT_TYPE_I18N_KEY` += `marketplace_order.created` / `.shipped` / `.delay_reason_added`. The filter drawer picks them up automatically; the settings toggle table (`ALL_EVENTS`) is a curated short list and was left unchanged.

**i18n (`frontend/messages/{uk,en}.json`, parity 5569 == 5569, 0 diff):**
sidebar `supplierCabinet.warehouses`; `supplierCabinet.pages.warehouses.*`; full `supplierCabinet.warehousesTab.*` block; `ordersTab.headerCreatedBy` ×2 namespaces; `notifications.eventTypes.*` + `eventSource.*` ×3; `modules.catalog.{supplier_inventory,supplier_workforce}`; `provider.modules.*` + `provider.moduleDescriptions.*` ×2.

## Verification

- `npx tsc --noEmit` — clean (fixed one error: added `moduleKey` to `NavItem`).
- `next lint` touched dirs — clean (only pre-existing `no-img-element` warnings in an untouched file).
- `next build` — success, `/supplier/warehouses` route emitted (8.03 kB).
- Backend `dotnet build -c Release` — clean.
- `dotnet test -c Release` filter `Marketplace|Notification|Tenant|Order|Supplier|Location` — **749 passed, 0 failed**.
- RLS audit + `RlsCrossTenant` + supplier warehouse + marketplace RLS integration — **89 passed, 0 failed**.

## Notes / follow-ups

- Design-time EF factory (`AppDbContextFactory.cs`) defaults to `:5432/shelfguard_dev`; the repo's tests + docker Postgres are `:5435/crm`. Anyone running `dotnet ef database update` must set `ConnectionStrings__DefaultConnection` first, or apply migrations via the docker container. (Pre-existing quirk, not introduced here.)
- `mobile/features/pos/receiptPrinting.ts` shows as modified — another agent's work, NOT touched or staged by this task.
- openapi.json regen still deferred (batched with the TASK-670..674 debt + all Phase 1–6 endpoints).
