# Database Schema

**Owner:** database-engineer
**Updated:** 2026-07-26
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

## TASK-392 Stage 1 — store-scoped user↔location assignment schema (2026-07-19)

New `user_locations` table (`FixUserLocationColumnMapping` + `AddUserLocations` migrations) —
schema only, no enforcement wired yet. Design (confirmed with product owner, project-architect):

- `enterprise_admin` sees every location in the tenant unconditionally via an `app.role` check —
  **no rows** in `user_locations` for this rank.
- Every other rank (network_manager, store_manager, merchandiser, storekeeper, cashier, staff) —
  **including single-location roles** — gets **exactly one row per assigned location**. One
  enforcement mechanism for all restricted ranks, not a shortcut through `User.StoreId` for the
  common single-store case.
- Columns: `Id`, `TenantId` (direct column, not EXISTS-derived — Stage 3's RLS will EXISTS-subquery
  into this table from other tables, so it needs its own leading index), `UserId` (FK→users,
  Cascade), `LocationId` (FK→locations, Cascade), `AssignedByUserId` (FK→users, SetNull, nullable
  audit field), `CreatedAt`. No soft-delete — pure leaf assignment table, hard DELETE to revoke.
- Indexes: unique `(TenantId, UserId, LocationId)` (dedupe + the "does user X have location Y"
  lookup), secondary `(TenantId, LocationId)` (reverse "who covers location X" lookup).
- RLS: standard tenant_isolation/provider_bypass/worker_bypass triad only — **not** a store_scope
  RESTRICTIVE policy. Nothing queries this table for access control yet.

Also fixed in the same pass: `User.StoreId` (existed unmapped since `AddAuth`, 2026-06-03 — no
`HasColumnName`/FK/index, unlike the ~19 other pre-v4 entities renamed in `V4LocationsRename`) now
correctly maps to the physical `LocationId` column with a `SetNull` FK to `locations`. C# property
name intentionally stays `StoreId` (matches the established pattern on `ProductStock`/`WriteOff`/
`PosShift`/etc). This is a UI/invite-time "default home location" hint only — never read by
access-control enforcement.

**Explicitly deferred:**
- Stage 3 — RESTRICTIVE store_scope RLS policies on `product_stock`/`daily_sales`/`pos_shifts`/etc
  that actually read `user_locations` to filter query results. Separate future database-engineer
  task.
- `app.user_id` session variable in `TenantConnectionInterceptor` (needed before any RLS policy can
  EXISTS-subquery "does the current user have a row for this location") — backend-developer task.
- `UserService`/API logic to write `user_locations` rows — backend-developer task.

**Local-dev gotcha worth knowing for Stage 3:** applying a migration that adds a FK to an
already-populated column referencing an RLS-protected table (e.g. `locations`) fails with a
false-positive `23503` FK violation when run through the app's own non-superuser
`shelfguard_app_dev` role — `FORCE ROW LEVEL SECURITY` hides every row from that role during the
migration's FK validation step (no `app.tenant_id`/`app.role` session vars exist outside a request
context), so Postgres thinks every existing FK value is orphaned even when none are. Apply
migrations locally via the `crm` superuser connection instead (`rolbypassrls=true` — exactly what
that role is documented for in `appsettings.Development.json`).

## TASK-393 Stage 3 — store-scoped RLS enforcement cutover (2026-07-19)

`AddLocationStoreScopeRlsPolicies` migration. Adds a **RESTRICTIVE** `store_scope` policy (name
literal, same convention as `tenant_isolation`/`provider_bypass`/`worker_bypass`) to the 9 tables
that carry per-location operational data:

| Table | Shape | Store/location column(s) |
|---|---|---|
| `product_stock` | one-sided | `"LocationId"` |
| `daily_sales` | one-sided | `"LocationId"` |
| `pos_shifts` | one-sided | `"LocationId"` |
| `pos_transactions` | one-sided | `"LocationId"` |
| `write_offs` | one-sided | `"LocationId"` |
| `discounts` | one-sided | `"LocationId"` |
| `stock_receipts` | one-sided | `"DestinationLocationId"` |
| `stock_movements` | two-sided (both nullable) | `"FromLocationId"`, `"ToLocationId"` |
| `stock_transfers` | two-sided (both NOT NULL) | `"FromLocationId"`, `"ToLocationId"` |

```sql
-- One-sided shape:
CREATE POLICY store_scope ON {table} AS RESTRICTIVE
  USING (
    current_setting('app.role', true) IN ('provider', 'provider_admin', 'worker', 'enterprise_admin')
    OR EXISTS (
         SELECT 1 FROM user_locations ul
         WHERE ul."UserId" = NULLIF(current_setting('app.user_id', true), '')::uuid
           AND ul."LocationId" = {table}."{column}"
       )
  );

-- Two-sided shape (OR across both columns — same pattern as supplier_chat_sessions'
-- SupplierTenantId/ClientTenantId):
CREATE POLICY store_scope ON {table} AS RESTRICTIVE
  USING (
    current_setting('app.role', true) IN ('provider', 'provider_admin', 'worker', 'enterprise_admin')
    OR EXISTS (
         SELECT 1 FROM user_locations ul
         WHERE ul."UserId" = NULLIF(current_setting('app.user_id', true), '')::uuid
           AND (ul."LocationId" = {table}."{FromColumn}" OR ul."LocationId" = {table}."{ToColumn}")
       )
  );
```

**Why RESTRICTIVE, not PERMISSIVE:** PERMISSIVE policies OR together, so a PERMISSIVE
`store_scope` would be silently defeated by the existing permissive `tenant_isolation` policy
(any tenant match alone would already satisfy the OR, regardless of location). RESTRICTIVE ANDs
on top of the whole permissive set instead — the correct semantics for "narrow what
`tenant_isolation`/`provider_bypass`/`worker_bypass` already allowed", not "grant an additional
way in".

**Bypass roles: `provider`, `provider_admin`, `worker`, `enterprise_admin`.** `provider`/
`worker`/`enterprise_admin` come from the project-architect's design. `provider_admin` is an
addition made during implementation, not literally in the original brief text — flagged
explicitly because `20260714150000_ExpandProviderBypassToProviderAdmin` already gives
`provider_admin` full parity with `provider` on all 9 of these tables via the pre-existing
permissive `provider_bypass` policy; omitting it from `store_scope`'s bypass condition would have
silently revoked that already-established access the moment this migration applied. Verified live
(see below) that `provider_admin` still sees all rows with zero `user_locations` rows for the
acting user. `provider_agent` is deliberately excluded — it never had bypass on these 9 tables to
begin with (only `support_tickets`/`ticket_comments`/`chat_sessions`), so no regression risk.

**Column-name/shape corrections vs. the original brief** (verified against the live EF model
before writing SQL, per the task's own instruction to check rather than assume):
- `stock_receipts` was assumed two-sided ("FromStoreId/ToStoreId or similar") — actually
  **one-sided**, single column `DestinationLocationId` (a receipt's origin is a supplier, not
  another store; there is no second side to OR against).
- `stock_movements` was assumed one-sided — actually **two-sided**, `FromLocationId`/
  `ToLocationId`, both **nullable** (e.g. a receipt-origin movement only populates `To`, a
  write-off-origin movement only populates `From`). A plain equality against a NULL column is
  simply unmatched under standard SQL NULL semantics — no extra CASE/guard needed. A movement row
  where neither column is populated is only visible to bypass roles — deliberate fail-closed
  default for that edge case, not a bug.

**No child-table policies needed** (`stock_receipt_items`, `stock_transfer_items`,
`write_off_items`, `pos_transaction_items`): Postgres re-applies a referenced table's RLS
policies — RESTRICTIVE included — inside any EXISTS/subquery/join that reads it. Since these
child tables' existing `tenant_isolation` policies are already `EXISTS`-into-the-parent, they
inherit `store_scope`'s narrowing automatically, the same way `supplier_chat_messages` already
inherits its tenant scoping transitively from `supplier_chat_sessions`.

**Live-verified locally** (applied through the actual restricted `shelfguard_app_dev` role, not
the `crm` superuser — same non-superuser path `Program.cs`'s `MigrateAsync()` uses in
production): `enterprise_admin` with zero `user_locations` rows sees all locations' stock;
a `store_manager` assigned to exactly one location sees only that location's rows (product_stock)
and only transfers touching that location on either side (`stock_transfers`); a `store_manager`
with zero `user_locations` rows sees zero rows (confirmed fail-closed, not fail-open); a
correctly-assigned user querying under the wrong `app.tenant_id` still sees zero rows
(`store_scope` ANDs on top of `tenant_isolation`, does not replace it); `provider`/
`provider_admin`/`worker` all bypass regardless of `user_locations` state. Migration `Down()`
(drops all 9 `store_scope` policies) round-tripped cleanly — rolled back and reapplied through
the non-superuser role, policies gone then fully restored. Permanent regression coverage:
`ShelfGuard.Tests.Infrastructure.StoreScopeRlsIntegrationTests` (9 tests, mirrors every scenario
above).

**⚠️ PRODUCTION ROLLOUT GATE — see `.claude/docs/store-scope-rollout-checklist.md` before ever
applying this migration outside local dev.** Every network_manager/store_manager/merchandiser/
storekeeper/cashier/staff user with zero `user_locations` rows goes to **zero visible rows** on
all 9 tables the instant this migration runs — immediate, total functional outage for that user
across stock/sales/POS/write-offs/etc, not a gradual degradation. Safe only after Stage 2's
manual backfill (product owner assigning locations to every affected user) reaches 100% coverage.

## TASK-404/411/414 — Loyalty program schema (`AddLoyaltyProgram`, 2026-07-26)

Four new tables (Фаза 0 of the `docs/uployal/` RFM+loyalty plan, `deep-cooking-nygaard.md`). First
schema in this project with a genuinely new, identity-based RLS policy shape, and the first table
in the project with **no RLS at all**.

| Table | RLS | Purpose | Key fields |
|---|---|---|---|
| `consumer_accounts` | **none** (see below) | Global, cross-tenant identity of an end customer (phone+password login) | `Phone` (globally unique, normalized `+380XXXXXXXXX`), `PasswordHash`, `FullName`, `Email?`, `FailedLoginAttempts`/`LockoutUntil` (TASK-329-shaped lockout), `IsActive` |
| `loyalty_memberships` | canonical triad + `consumer_self_access` | One `ConsumerAccount`'s enrollment in one tenant's bonus program | `TenantId`, `ConsumerAccountId` (FK→consumer_accounts, Restrict), `CustomerId` (FK→customers, SetNull — auto-linked/auto-created by phone), `LinkedUserId` (FK→users, SetNull — "staff joined their own employer's program" case), `TotpSecret`, `LastRedeemedTimestep` (anti-replay), `Balance`, `Status` (active\|blocked). Unique `(TenantId, ConsumerAccountId)`. `xmin`/`IsRowVersion()` optimistic-concurrency token added by `AddLoyaltyMembershipConcurrencyToken` (TASK-414 security fix — see below) |
| `loyalty_ledger_entries` | canonical triad + `consumer_self_access` (EXISTS via membership) | Append-only bonus audit trail behind `LoyaltyMembership.Balance` | `TenantId`, `MembershipId` (FK, Restrict), `EntryType` (accrual\|redemption\|manual_adjustment\|expiry), `Amount` (signed), `BalanceAfter`, `PosTransactionId` (FK→pos_transactions, SetNull), `CreatedByUserId` (FK→users, SetNull). Every property `init`-only in the C# entity — rows are never updated/deleted, only inserted alongside a `LoyaltyMembership.Balance` write in the same `SaveChangesAsync()` |
| `loyalty_program_settings` | canonical triad only | One row per tenant — bonus program configuration | `TenantId` (unique), `IsEnabled`, `AccrualRatePercent` (default 3.0), `RedemptionCapPercent` (default 50.0), `MinRedemptionBalance`, `CodeTtlSeconds` (default 30) |

### `consumer_self_access` — first identity-based (not role-based) RLS policy in this repo

Every other RLS policy in this file scopes on `app.tenant_id` (role/tenant identity). A
`ConsumerAccount` session is cross-tenant by design — its JWT never carries `tenant_id` at all,
only `consumer_account_id` — so `tenant_isolation` can never match it. `TenantConnectionInterceptor`
now also sets `app.consumer_account_id` (same always-set/null-uuid-fallback discipline as every
other session var; the unauthenticated RESET branch clears it too) and whitelists role `"consumer"`.

```sql
-- loyalty_memberships: direct column comparison
CREATE POLICY consumer_self_access ON loyalty_memberships
  USING ("ConsumerAccountId" = (NULLIF(current_setting('app.consumer_account_id', true), ''))::uuid);

-- loyalty_ledger_entries: EXISTS through the parent membership (no ConsumerAccountId column of its own)
CREATE POLICY consumer_self_access ON loyalty_ledger_entries
  USING (
    EXISTS (
      SELECT 1 FROM loyalty_memberships m
      WHERE m."Id" = loyalty_ledger_entries."MembershipId"
        AND m."ConsumerAccountId" = (NULLIF(current_setting('app.consumer_account_id', true), ''))::uuid
    )
  );
```
Postgres ORs multiple PERMISSIVE policies together, so this is **additive** on top of
`tenant_isolation` (which a consumer session never satisfies anyway), not a replacement — a staff
session's tenant-scoped visibility is completely unaffected by this policy's existence. Not added
to `loyalty_program_settings` — consumers never read that table (staff/enterprise_admin
configuration only).

### `consumer_accounts` has NO RLS at all — deliberate, not an oversight

Same precedent as `tenants`: globally readable at the database level, protected only by
application code that never hands a generic `GetById` to a non-owner (verified end-to-end by
security-reviewer, TASK-412 — every call site resolves the id from the JWT `consumer_account_id`
claim or from the caller's own phone, never from a route/body parameter supplied by someone else).
**This is a project convention now, not a one-off exception** — if a future agent finds this table
has no RLS, that is by design; do not "fix" it by adding a policy without first re-reading this
note and the `AddLoyaltyProgram` migration's own class-level doc comment.

### Lesson from TASK-411 — table ownership, not a GRANT script, is what gives the app role access

This codebase has no bootstrap script and no `ALTER DEFAULT PRIVILEGES` anywhere. Every table's
access for the real app role (`shelfguard_app_dev` in dev, etc.) comes purely from **table
ownership** — established once for pre-existing tables by TASK-372/KI-027's bulk `ALTER TABLE ...
OWNER TO` loop, and inherited automatically by every table created since, because the migration
that creates it normally runs through the app's own (already-owning) connection.

`AddLoyaltyProgram` broke this silently: it was applied to dev via the `crm` **superuser**
connection (to route around the FK-validation-under-RLS gotcha documented above under "Local-dev
gotcha worth knowing for Stage 3" — inserting a FK against an RLS-protected parent through the
restricted role can false-positive `23503`). Net effect: all 4 loyalty tables ended up owned by
`crm`, with **zero grants** to the app role — `dotnet test` stayed green throughout (the
live-Postgres RLS tests connect as a separate test role with its own explicit `GRANT ALL`, not the
real app connection string), but the real running API 500'd with Postgres `42501 permission
denied` the moment any endpoint touched these tables. Fixed by `FixLoyaltyTableGrants`
(20260726154747) — a `DO $$ ... ALTER TABLE {each of the 4} OWNER TO %I` block that resolves the
target role **dynamically** from whichever role currently owns `tenants`, rather than hardcoding a
per-environment role name.

**Actionable takeaway for any future migration applied outside the normal `MigrateAsync()` path**
(superuser escape hatch for the FK-under-RLS gotcha, manual `dotnet ef database update
--connection`, etc.): explicitly verify the new table(s)' owner afterward
(`SELECT tablename, tableowner FROM pg_tables WHERE schemaname='public' AND tableowner <>
'<app role>'` should return zero rows). A migration applying cleanly and `dotnet test` staying
green are **not** sufficient evidence that the app's real connection can use the table.
`ALTER TABLE ... OWNER TO` itself requires the executing role to be a superuser or the table's
*current* owner, so this class of fix must always run via a superuser connection too, never the
automatic boot-time migration path.

## Architecture Rules
- `expiry_date` and `batch_number` are NEVER modified on transfer — copied as-is to `stock_transfer_items`
- All soft deletes via `is_active`, never hard DELETE on business data
- UUID PKs with `gen_random_uuid()` default
- All timestamps in UTC (`TIMESTAMPTZ`)
