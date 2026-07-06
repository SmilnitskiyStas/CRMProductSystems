# Database Schema

**Owner:** database-engineer
**Updated:** 2026-06-04
**Source:** v1-spec.md section 4

## Multi-Tenancy
Row Level Security (RLS) on every tenant table.
`app.tenant_id` set per-connection by `TenantConnectionInterceptor`.
`app.role = 'provider'` bypasses all tenant isolation via `provider_bypass` policy.

## RLS Template
All column names are double-quoted to match EF Core PascalCase naming.
```sql
ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON {table}
  USING ("TenantId" = current_setting('app.tenant_id', true)::uuid);
CREATE POLICY provider_bypass ON {table}
  USING (current_setting('app.role', true) = 'provider');
```
Child tables without direct TenantId use EXISTS subquery through parent.

## Migration History
| Migration | Date | Description |
|---|---|---|
| InitialCreate | 2026-06-01 | POC Products table |
| AddAuth | 2026-06-03 | tenants, users, refresh_tokens + RLS |
| FullSchema | 2026-06-04 | Full v1 schema (19 new tables) |
| FixRlsAndForeignKeys | 2026-06-04 | RLS on notification_settings + FK constraints + movement indexes |
| AddIntegrationConfigs | 2026-06-06 | integration_configs table + RLS + unique index (TenantId, Service) |

## Tables

### Auth (existing)
| Table | RLS | Notes |
|---|---|---|
| tenants | — | Root entity, no RLS needed |
| users | ✅ | TenantId IS NULL for provider users |
| refresh_tokens | ✅ | Via user sub-select |

### POC (deprecated, kept for catalog API compat)
| Table | Notes |
|---|---|
| Products | No tenant_id. Will be removed when catalog API migrates to catalog_products |

### Structure
| Table | RLS | Notes |
|---|---|---|
| stores | ✅ | TenantId direct |
| store_zones | ✅ | Via stores.TenantId subquery |
| categories | ✅ | TenantId direct, self-referencing parent_id |
| product_segments | ✅ | TenantId direct |
| suppliers | ✅ | TenantId direct |

### Products (v1)
| Table | RLS | Notes |
|---|---|---|
| catalog_products | ✅ | Tenant-aware, maps to "catalog_products" table |
| product_supplier_settings | ✅ | Unique(product_id, supplier_id, tenant_id) |

### Stock
| Table | RLS | Notes |
|---|---|---|
| product_stock | ✅ | Core FEFO table. ExpiryDate DATE NOT NULL |
| stock_movements | ✅ | Audit log of all quantity changes |
| stock_events | ✅ | IoT/sensor events placeholder (v3) |

### Documents
| Table | RLS | Notes |
|---|---|---|
| stock_receipts | ✅ | TenantId direct |
| stock_receipt_items | ✅ | Via stock_receipts subquery |
| stock_transfers | ✅ | TenantId direct |
| stock_transfer_items | ✅ | Via stock_transfers subquery. ExpiryDate + BatchNumber COPIED, never changed |
| write_offs | ✅ | TenantId direct |
| write_off_items | ✅ | Via write_offs subquery |
| discounts | ✅ | TenantId direct |

### Notifications
| Table | RLS | Notes |
|---|---|---|
| notification_settings | ✅ | Via users.TenantId EXISTS sub-select |
| notification_queue | ✅ | TenantId nullable (system messages have NULL) |

### Integrations
| Table | RLS | Notes |
|---|---|---|
| integration_configs | ✅ | TenantId direct. UNIQUE(TenantId, Service). Config JSONB encrypted at app layer. Supported services: telegram, resend, webhook, prro, iot |

### Logs
| Table | RLS | Notes |
|---|---|---|
| activity_logs | ✅ | TenantId nullable (provider actions have NULL) |

## Key Indexes
```sql
-- FEFO: always consume nearest expiry first
idx_stock_expiry_active ON product_stock("TenantId", "StoreId", "ProductId", "ExpiryDate")
  WHERE "Quantity" > 0 AND "Status" NOT IN ('sold_out', 'archived')

-- Fast store dashboard queries
idx_stock_tenant_store ON product_stock("TenantId", "StoreId")

-- stock_movements filter support (GET /movements)
idx_movements_tenant_type   ON stock_movements("TenantId", "MovementType")
idx_movements_tenant_store  ON stock_movements("TenantId", "FromStoreId", "ToStoreId")
idx_movements_product       ON stock_movements("TenantId", "ProductId")
idx_movements_created_at    ON stock_movements("TenantId", "CreatedAt" DESC)
```

## Foreign Key Constraints (added via FixRlsAndForeignKeys migration)
| Table | Column | References |
|---|---|---|
| stock_movements | ProductId | catalog_products.Id ON DELETE RESTRICT |
| stock_movements | FromStoreId | stores.Id ON DELETE RESTRICT (nullable) |
| stock_movements | ToStoreId | stores.Id ON DELETE RESTRICT (nullable) |
| write_offs | StoreId | stores.Id ON DELETE RESTRICT |
| discounts | ProductId | catalog_products.Id ON DELETE RESTRICT |
| discounts | StoreId | stores.Id ON DELETE RESTRICT |
| discounts | ProductStockId | product_stock.Id ON DELETE SET NULL (nullable) |

> Note: These FK constraints exist in the DB but NOT in EF Core's model snapshot (pure SQL migration). If navigation properties are added to entities later, the corresponding HasForeignKey() calls will conflict — drop and re-add the constraint in the migration. See ADR-009 for rationale.

## v2 — Auto Order Data Foundation (V2DataFoundation migration, 2026-06-11)

| Table | Purpose | Key constraints |
|---|---|---|
| `daily_sales` | Per-day sales per product+store; ADU source data | UNIQUE(StoreId, ProductId, Date); FK → catalog_products, stores (CASCADE); idx (TenantId, Date) |
| `product_adu` | Cached ADU 30/60/90d + effective + product group (1-3) | UNIQUE(StoreId, ProductId); FK CASCADE |
| `supply_schedules` | Supplier delivery weekdays (`DayOfWeek integer[]`) + order lead days | idx (StoreId, SupplierId); FK CASCADE |

All three: RLS enabled — `tenant_isolation` (strict, no IS-NULL branch) + `provider_bypass`.
`daily_sales.Source`: manual / pos / import. `IsAnomaly=true` rows are excluded from ADU.
Entities: `ShelfGuard.Domain/Entities/{DailySale,ProductAdu,SupplySchedule}.cs`.

## v3 — IoT Foundation (V3IotFoundation migration, 2026-06-12)

| Table | Purpose | Key constraints |
|---|---|---|
| `iot_devices` | Registered sensors/cameras per store/zone | UNIQUE(TenantId, DeviceId); FK stores RESTRICT, store_zones SET NULL; Config jsonb |
| `temperature_readings` | Temp/humidity stream from temp_sensors | FK iot_devices CASCADE; idx (DeviceId, RecordedAt DESC) |
| `weight_readings` | Weight deltas from shelf sensors | FK iot_devices CASCADE; idx (DeviceId, RecordedAt DESC) + partial idx Processed=false |

RLS: `iot_devices` — standard tenant_isolation + provider_bypass (TenantId direct).
Readings tables — tenant via `EXISTS (SELECT 1 FROM iot_devices d WHERE d."Id" = "DeviceId" AND d."TenantId" = …)` + provider_bypass.
Entities: `ShelfGuard.Domain/Entities/{IotDevice,TemperatureReading,WeightReading}.cs`.
`iot_devices.Config` (jsonb): temp sensors `{profile: fridge|freezer, alert_above?}`; weight sensors `{product_id, unit_weight_grams}`.

## v4.1 — Supplier tenant migration + roles/tasks (TASK-305, 2026-07-05)

`MigrateOrphanSuppliersToTenants` — data-only migration. Every `Supplier` previously
attached to the `platform-marketplace` system tenant (ADR-016 compromise) gets its own
real, active tenant (`BusinessType='supplier'`, `Modules=["marketplace_supplier"]`),
`supplier_profiles.IsOwnerManaged` set `true`. Idempotent (no-op if the system tenant
or its suppliers are gone). `supplier_reviews.TenantId` untouched (it's the reviewing
client, not the owner).

`AddSupplierRolesAndTasks`:

| Table | Purpose | Key constraints |
|---|---|---|
| `supplier_roles` | Custom staff roles, scoped per supplier tenant (unlike global `provider_roles`) | `TenantId`, `Permissions text[]`; idx(TenantId); RLS tenant_isolation (NULLIF guard) + provider_bypass, FORCE RLS |
| `supplier_tasks` | Supplier task board (new standalone entity) | FK → suppliers (CASCADE), tenants as ClientTenantId (SET NULL), users as AssignedToUserId/CreatedByUserId (SET NULL); idx (TenantId, SupplierId, AssignedToUserId, ClientTenantId, Status); RLS tenant_isolation + provider_bypass, FORCE RLS |

`users.SupplierRoleId` (nullable FK → supplier_roles, ON DELETE SET NULL) mirrors
`ProviderRoleId`. Entities: `ShelfGuard.Domain/Entities/{SupplierRole,SupplierTask}.cs`,
constants: `ShelfGuard.Domain/Constants/SupplierPermissions.cs`.

## Architecture Rules
- `expiry_date` and `batch_number` are NEVER modified on transfer — copied as-is to `stock_transfer_items`
- All soft deletes via `is_active`, never hard DELETE on business data
- UUID PKs with `gen_random_uuid()` default
- All timestamps in UTC (`TIMESTAMPTZ`)
