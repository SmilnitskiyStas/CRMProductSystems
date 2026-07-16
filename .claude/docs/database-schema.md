# Database Schema

**Owner:** database-engineer
**Updated:** 2026-06-04
**Source:** v1-spec.md section 4

## Multi-Tenancy
Row Level Security (RLS) on every tenant table.
`app.tenant_id` set per-connection by `TenantConnectionInterceptor`.
`app.role = 'provider'` bypasses all tenant isolation via `provider_bypass` policy.

## RLS Template (canonical — corrected 2026-07-14, Block 2 pre-launch audit, fail-open P0 fix)
All column names are double-quoted to match EF Core PascalCase naming.
```sql
ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
ALTER TABLE {table} FORCE ROW LEVEL SECURITY;

-- NULLIF guard (still required): current_setting('app.tenant_id', true) returns '' (empty
-- string, not NULL) after RESET — casting '' straight to uuid throws "invalid input syntax for
-- type uuid" and 500s the request. NULLIF converts '' to NULL first, and a plain `=` comparison
-- against NULL is simply falsy (0 rows) — no crash, no fail-open branch needed.
CREATE POLICY tenant_isolation ON {table}
  USING ("TenantId" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);

CREATE POLICY provider_bypass ON {table}
  USING (current_setting('app.role', true) = 'provider');

-- REQUIRED: without this, worker cron jobs (worker/src/jobs/*) that run raw SQL after
-- `SET app.role = 'worker'` are silently blocked by FORCE ROW LEVEL SECURITY — no error,
-- rows just never get written (see 20260712175141_AddWorkerBypassRlsPolicy, TASK-343).
CREATE POLICY worker_bypass ON {table}
  USING (current_setting('app.role', true) = 'worker');
```
Child tables without direct TenantId use EXISTS subquery through the parent — same NULLIF
guard, still no fail-open branch:
```sql
CREATE POLICY tenant_isolation ON {child_table}
  USING (
    EXISTS (
      SELECT 1 FROM {parent_table} p
      WHERE p."Id" = {child_table}."ParentId"
        AND p."TenantId" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
    )
  );
```

**⚠️ VULNERABILITY, fixed 2026-07-14 — do not reintroduce.** From 2026-06-29 to 2026-07-14 this
doc (and the actual applied migrations) used the pattern below, which is **fail-open, not
fail-closed**:
```sql
-- DO NOT USE — allows full cross-tenant read when app.tenant_id is unset.
CREATE POLICY tenant_isolation ON {table}
  USING (
    NULLIF(current_setting('app.tenant_id', true), '') IS NULL   -- ⚠️ always TRUE on RESET
    OR "TenantId" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
  );
```
The `IS NULL OR` branch is true for *every row* whenever `app.tenant_id` is unset — which is the
state of every unauthenticated connection (`TenantConnectionInterceptor` does
`RESET app.tenant_id; RESET app.role;` for them) and of any raw, non-superuser Postgres session
before it explicitly sets the session var. **Reproduced live** 2026-07-14: a real
`NOSUPERUSER NOBYPASSRLS` role, `RESET app.tenant_id; RESET app.role;`, then
`SELECT count(*) FROM product_stock` returned every row across all tenants instead of zero.
This shape was introduced by the 2026-06-29 bulk fixes
(`20260629000000_FixUsersRlsNullIfEmptyString`, `20260629010000_FixAllRlsPoliciesNullIfEmptyString`)
— which correctly fixed a `uuid`-cast crash but over-generalized the `users` table's deliberate
"allow lookup before tenant is known" exception (needed for login) to every other tenant table —
and was still being copied as "the" canonical pattern as recently as
`20260714100000_FixMissingRlsGuardsAndProviderBypass` (same day, earlier in this same audit).
Fixed on 57 of 60 affected tables by `20260714180000_FixFailOpenTenantIsolationOnReset`.

**Documented exceptions — keep the fail-open branch on these three, do not "fix" them:**
| Table | Why it must stay fail-open |
|---|---|
| `users` | Login must find a user by email before the caller's tenant is known. |
| `refresh_tokens` | Token refresh must find the token/user before the caller's tenant is known (same shape, via `EXISTS` through `users`). |
| `notification_settings` | Same `EXISTS`-through-`users` pre-auth lookup shape as `refresh_tokens`. |

Any other table needing a similar "look this up before we know the tenant" flow should get its
own narrowly-scoped policy (or, better, explicitly `SET app.role = 'worker'`/resolve the tenant
via a different lookup first) rather than reusing this fail-open shape — it took two live
regressions to find the two non-cron worker code paths that had come to depend on it by accident
(see task log `.claude/logs/tasks/352_2026-07-14_db-cross-tenant-audit_database-engineer.md` —
`telegram-listener.ts`'s `/start <code>` account-linking flow and `notification-dispatch.job.ts`'s
outbox dispatch both silently relied on `product_stock`/`notification_queue`-style tables' old
fail-open branch instead of explicitly setting `app.role = 'worker'` like every other worker job;
both fixed in the same pass).

Two regression tests in `ShelfGuard.Tests.Infrastructure.RlsCrossTenantIntegrationTests` guard
this class of bug going forward (run locally against `docker compose up -d postgres`; no
Postgres service in CI today, see `.github/workflows/ci.yml`):
`AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass` (policy
presence/naming) and `TenantIsolationPolicies_HaveNoFailOpenBranch_ExceptDocumentedPreAuthLookups`
(no table outside the three exceptions above may have the fail-open branch) plus
`ProductStock_FullyResetSession_ReturnsZeroRows_NotEveryRow` (direct live reproduction of the
exact RESET-state scenario that was vulnerable).

**Production status (as of 2026-07-14): this fix is applied to the dev database only.**
Production still runs the fail-open policy shape described above — deploying this fix to
production is a separate decision (urgency/rollout method) for the user to make, not something
done as part of this audit.

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

> Note: These FK constraints exist in the DB but NOT in EF Core's model snapshot (pure SQL
> migration, `AppDbContext`'s fluent config for `StockMovement` has no `HasOne`/`HasForeignKey`
> calls at all — reconfirmed 2026-07-15, Block 16. `Discount` is a mixed case: `TenantId`/
> `CreatedBy`/`ApprovedBy` now have proper fluent `HasForeignKey` config, but its `ProductId`/
> `StoreId`/`ProductStockId` FKs — all three listed above — remain raw-SQL-only, still invisible
> to EF). Practical risk today is low: EF only
> diffs against what it knows, so `dotnet ef migrations add` won't touch these FKs or generate
> a false "missing constraint" migration. The risk is if navigation properties +
> `HasForeignKey()` are added to these entities later without checking the DB first — EF would
> add a second, differently-named FK on the same column, which is redundant but not fatal
> (Postgres allows multiple FKs on one column). **Correction:** the previous version of this
> note cited "ADR-009" — that ADR number is actually "IAnalyticsRepository in Application
> layer" (unrelated); the closest existing ADR is ADR-008 ("RLS column names must be
> double-quoted"), which covers quoting, not this snapshot-drift concern specifically. No ADR
> currently documents this exact rationale — treat this note as the source of truth instead of
> chasing an ADR number.
>
> The same "not tracked by EF" property applies to every RLS policy in this document — `CREATE
> POLICY` has no EF Core fluent API equivalent, so **no CI or tooling check currently verifies**
> that a newly added tenant table gets `tenant_isolation`/`provider_bypass`/`worker_bypass`. That
> gap is exactly how the six-table RLS omission below was introduced and went unnoticed for two
> weeks (2026-06-29 → 2026-07-14) — the `AllForceRlsTables_HaveTenantIsolationNullifGuard_
> ProviderBypass_AndWorkerBypass` regression test (added 2026-07-14) is the first automated
> guard against it, but it only runs locally today (no Postgres service in CI, see
> `.github/workflows/ci.yml`).

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

## v4.2 — Supplier ↔ Client chat (TASK-312, 2026-07-06)

`AddSupplierChat` — schema only (no service/controller yet; see handoff for TASK-312).
Standalone messaging subsystem, distinct from the existing `chat_sessions`/`chat_messages`
(Client↔Provider, single-tenant-per-session model that doesn't fit two-tenant threads).

| Table | Purpose | Key constraints |
|---|---|---|
| `supplier_chat_sessions` | One persistent thread per (supplier tenant, client tenant) pair — no Status/close, messages just accumulate | UNIQUE(SupplierTenantId, ClientTenantId); FK SupplierTenantId → tenants CASCADE, ClientTenantId → tenants RESTRICT (avoids multi-cascade-path conflict); idx(SupplierTenantId), idx(ClientTenantId); RLS tenant_isolation (`SupplierTenantId = ... OR ClientTenantId = ...`, NULLIF guard) + provider_bypass, FORCE RLS |
| `supplier_chat_messages` | Messages within a session. `SenderTenantId` lets the frontend derive "mine vs. theirs" via `SenderTenantId == myTenantId` regardless of which side is viewing | FK SessionId → supplier_chat_sessions CASCADE; idx(SessionId), idx(CreatedAt); RLS tenant_isolation via `EXISTS` subquery on the parent session's tenant pair (pattern: `notification_settings`) + provider_bypass, FORCE RLS |

Entities: `ShelfGuard.Domain/Entities/{SupplierChatSession,SupplierChatMessage}.cs`.
`provider` role bypasses both tables' RLS like every other `supplier_*` table.

## Block 16 — Pre-launch DB-performance audit (2026-07-15)

Systematic pass over all 76 `FORCE ROW LEVEL SECURITY` tables: every RLS policy unconditionally
ANDs `"TenantId" = current_setting(...)` (or an `OR`-pair / `EXISTS` join for two-tenant and
child tables) onto every query, regardless of what the application code filters by — a table
with no index leading on that tenant column becomes a full-table-scan-across-every-tenant on
every read, not just an occasional slow query.

**Method:** for each FORCE RLS table, checked (a) does it have its own tenant column, or does its
RLS policy join to a parent via `EXISTS` (child tables — `pos_transaction_items`, `write_off_items`,
etc. — verified fine, the join always uses the already-indexed parent FK, e.g. `TransactionId`/
`WriteOffId`); (b) for tables with their own tenant column, does at least one index lead on it;
(c) for the ones that didn't, read the actual repository query methods to see whether app code
*also* always filters on something else globally unique per tenant (e.g. `StoreId`/`WorkOrderId`/
`DiscountId` — a Guid FK to a table that's itself one-tenant-only, which Postgres can use as an
efficient index scan before RLS's extra filter ever matters) — only flagged as a real gap when a
live code path has **no** selective filter besides RLS's own `TenantId`.

**Fixed (`AddChatSessionsAndSupplySchedulesTenantIndexes` migration, 2026-07-15):**
| Table | Index added | Why |
|---|---|---|
| `chat_sessions` | `idx_chat_sessions_tenant_updated` (TenantId, UpdatedAt DESC) | `ChatService.GetSessionsAsync` (tenant chat inbox) does `WHERE TenantId == tenantId ORDER BY UpdatedAt DESC` directly — table had **zero** index besides the PK. Live chat is an actively growing feature; this was a real, present-day full scan on every inbox load. |
| `supply_schedules` | `idx_supply_schedules_tenant` (TenantId) | `SupplyScheduleRepository.GetAsync(storeId?, supplierId?)` — both filters optional; the Settings page's unfiltered list view has no other selective predicate to fall back on. |

**Checked, no fix needed (confirmed via actual query methods, not just schema inspection):**
`product_adu`/`product_buffer` (always `StoreId`-scoped, served by the existing
`(StoreId, ProductId)` unique index), `promo_cannibalization` (always `DiscountId`- or
`Discount.StoreId`-scoped), `product_supplier_settings` (always `ProductId`-scoped),
`as_work_order_lines` (always loaded via `.Include(w => w.Lines)` off `WorkOrderId`),
`ticket_comments` (always `.Include(t => t.Comments)` off `TicketId`, or `AuthorId` — already
indexed), `marketplace_order_items` (always `.Include(o => o.Items)` off `OrderId`),
`stock_events` (write-only audit trail today — `AddStockEventAsync` is the only call site
anywhere in the codebase; no read/list query exists yet to be slow. Flagged as **preventive**:
add a `(TenantId, CreatedAt DESC)` index if/when a "stock event history" read feature ships).
`supplier_chat_messages`/`supplier_support_ticket_messages` also looked like gaps by column name
(`SenderTenantId`) but their RLS policy doesn't reference that column at all — it's an `EXISTS`
join to the parent session/ticket on `SessionId`/`TicketId` (already indexed), same safe pattern
as the other child tables.

Every other FORCE RLS table with its own tenant column already had a leading index (most from
earlier per-block audits: TASK-352/353/354 etc.) — see `Key Indexes` above and the migration
history for the full list.

## Architecture Rules
- `expiry_date` and `batch_number` are NEVER modified on transfer — copied as-is to `stock_transfer_items`
- All soft deletes via `is_active`, never hard DELETE on business data
- UUID PKs with `gen_random_uuid()` default
- All timestamps in UTC (`TIMESTAMPTZ`)
