# Database Schema

**Owner:** database-engineer
**Updated:** 2026-08-31
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

**Documented exceptions — keep the fail-open branch on these two, do not "fix" them:**
| Table | Why it must stay fail-open |
|---|---|
| `users` | Login must find a user by email before the caller's tenant is known. |
| `refresh_tokens` | Token refresh must find the token/user before the caller's tenant is known (same shape, via `EXISTS` through `users`). |

`password_reset_tokens` briefly held a third slot on this list (TASK-455, same "pre-auth lookup"
shape via `EXISTS` through `users`) but the table itself was dropped by TASK-464 — the link/token-
based reset flow it backed was redesigned into a temporary-password flow that needs no pre-auth
token lookup at all. See `## TASK-455` (superseded) and `## TASK-464` below.

`notification_settings` previously held a slot on this list under the same "pre-auth lookup"
assumption, but TASK-360 (Block 9 audit, 2026-07-15) found it has no actual pre-auth access path
(both its reads/writes sit behind `[Authorize]`) — its fail-open branch was a real cross-tenant
leak, not a necessary one, and was removed by `20260715120000_FixNotificationSettingsRlsFailOpen`.
Do not re-add it here without re-verifying that finding still holds.

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
(no table outside the two exceptions above may have the fail-open branch) plus
`ProductStock_FullyResetSession_ReturnsZeroRows_NotEveryRow` (direct live reproduction of the
exact RESET-state scenario that was vulnerable).

**Production status — VERIFIED FAIL-CLOSED on 2026-08-30 (TASK-642).** The line that used to
stand here ("as of 2026-07-14 this fix is applied to the dev database only; production still runs
the fail-open policy shape") was written *before* the pre-launch audit shipped and went stale the
moment it did — it was still being read as current a month and a half later and nearly caused a
redundant production migration. Live read-only check against the prod DB
(`docker exec shelfguard_postgres psql -U shelfguard -d shelfguard`, `pg_policy` /
`pg_get_expr(polqual, polrelid)`):

- `20260714180000_FixFailOpenTenantIsolationOnReset` **is** present in prod's
  `__EFMigrationsHistory`, as are `20260714100000_FixMissingRlsGuardsAndProviderBypass`,
  `20260714150000_ExpandProviderBypassToProviderAdmin` and
  `20260715120000_FixNotificationSettingsRlsFailOpen`. Migrations auto-apply on deploy
  (`Program.cs` → `MigrateAsync`); the audit deployed 2026-07-16 (commit `84c48061`).
- Prod has **107** `tenant_isolation` policies. Exactly **two** — `users` and `refresh_tokens` —
  still carry the session-level fail-open branch (`NULLIF(current_setting('app.tenant_id', true), '') IS NULL OR …`),
  i.e. precisely the two documented pre-auth exceptions in the table above and nothing else.
  (`activity_logs`, `notification_queue` and `notification_settings` match a naive `%IS NULL%`
  search but only via the *row-level* `"TenantId" IS NULL` / `u."TenantId" IS NULL` Group-B
  provider-row clause, which is intentional and not a session-level fail-open branch.)
- Spot-checked fail-closed on prod: `items`, `suppliers`, `supplier_items`,
  `supplier_item_barcodes`, `supplier_item_images`, `supplier_metrics`, `supplier_reviews`,
  `product_stock`, `categories`, `product_segments`, `product_supplier_settings` (Group A) and
  `location_zones`, `pos_transaction_items`, `stock_receipt_items`, `write_off_items` (Group C
  EXISTS-through-parent). All read
  `("TenantId" = (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid)` with no
  `WITH CHECK` override.
- RLS is **not** inert on prod (pre-launch blocker 3 / KI-027 is closed): the API connects as
  `shelfguard_app` (`rolsuper=f`, `rolbypassrls=f`), `items` is owned by `shelfguard_app`, and
  `items`/`suppliers`/`supplier_items` all have `relrowsecurity=t` **and** `relforcerowsecurity=t`.

No production migration is outstanding for this issue. Re-verify with the queries above rather
than trusting this paragraph if the date drifts far from today.

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

## TASK-419 — Price segment settings schema (`AddPriceSegmentSettings`, 2026-07-27)

One new tenant-settings table (Фаза 2 of the `docs/uployal/` plan, `deep-cooking-nygaard.md`) —
direct analogy to `loyalty_program_settings` (TASK-404). Segments/audiences/customer metrics
themselves are **not** persisted anywhere — computed live from `pos_transactions`/`customers` on
every request (`PriceSegmentsRepository.cs`, raw SQL, `PERCENTILE_CONT`-based — see `decisions.md`
ADR-023 addendum for why not `NTILE`). This is the only new table for all of Фаза 2.

| Table | RLS | Purpose | Key fields |
|---|---|---|---|
| `price_segment_settings` | canonical triad only (no `consumer_self_access`) | One row per tenant — Фаза 2 configuration | `TenantId` (unique), `DefaultFrequencyDeclineThresholdPercent` (default 30.0), `MinReceiptsForBoundaries` (nullable int — **validated/persisted/returned but not yet read** by `GetBoundariesAsync`; flagged by security-reviewer TASK-422 as an inert functional gap, not a security one), `UpdatedAt` |

Staff-only, same posture as `loyalty_program_settings` — no consumer-facing read path exists to
this table at all (unlike `loyalty_memberships`/`loyalty_ledger_entries`), so it carries only the
canonical fail-closed `tenant_isolation` (NULLIF-guarded) / `provider_bypass`
(`IN ('provider','provider_admin')`) / `worker_bypass` triad, no identity-based policy.

Applied via the app's own non-superuser `shelfguard_app_dev` connection first, not the `crm`
superuser escape hatch — a brand-new, empty FK column doesn't trigger the FK-validation-under-RLS
false-positive documented under TASK-404/411 above, so it applied cleanly with correct table
ownership from the start; no `FixLoyaltyTableGrants`-style companion migration was needed here.
Live-verified against the real app role (positive path, fail-closed, cross-tenant isolation, bypass
roles, policy/flag byte-check) — see task log for detail.

## TASK-428 — `items.Name` trigram index (`AddItemNameTrigramIndex`, 2026-07-27)

`idx_items_name_trgm` — GIN trigram index (`gin_trgm_ops`) on `items."Name"`, added ahead of
Фаза 3's AudienceBuilder text-search feature, same shape as the pre-existing
`idx_notification_queue_title_trgm` (`ExtendNotificationQueueFiltering`). `pg_trgm` was already
enabled by that earlier migration — this one only adds the new index, no new extension.

```sql
CREATE INDEX idx_items_name_trgm ON items USING gin ("Name" gin_trgm_ops);
```

**⚠️ Known v1 limitation, not a bug — the index exists but the planner cannot use it on the real,
RLS-protected app connection. Do not "fix" this by re-tuning the index itself; the blocker is a
general Postgres/RLS rule, not this migration's DDL.**

`items` carries the canonical RLS triad + `FORCE ROW LEVEL SECURITY`. `ILIKE` compiles to
Postgres's `texticlike` function, confirmed `proleakproof = false` directly against `pg_proc`.
Under `FORCE ROW LEVEL SECURITY`, any predicate built from a non-`LEAKPROOF` function can only be
evaluated as a post-scan `Filter` — never pushed into an index condition — even for the table
owner, once `FORCE` is set. Live-verified (seeded 500k synthetic rows in a transaction, rolled
back afterward, dev DB confirmed clean): the real app-role connection produces a `Seq Scan`
(~1085ms) on `"Name" ILIKE '%term%'`, even with `enable_seqscan=off` (proof no index-based plan
exists at all, not merely a deprioritized one). The identical query as a superuser
(`rolbypassrls=true`, RLS never applies) produces `Bitmap Index Scan on idx_items_name_trgm`
(~2ms) — same index, same data. Full measurement in
`.claude/logs/tasks/428_2026-07-27_item-name-trigram-index_database-engineer.md`.

**Not new to this index** — the identical live test against the pre-existing
`idx_notification_queue_title_trgm` (`notification_queue."Title"`) shows the same `Filter`-not-
`Index Cond` behavior under RLS. That index has, as far as this session could tell, never actually
accelerated a real tenant-scoped keyword search in production either — flagged as a separate
background task, not fixed as part of TASK-428.

**Accepted for v1**: at realistic per-tenant catalog sizes (thousands of SKUs — this is a "type a
term, press Enter" field, not a live-autocomplete search), the Seq Scan cost is judged acceptable.
Every `i."Name" ILIKE` call site in `AudienceBuilderRepository.cs` carries a doc comment pointing
back to this note (see `IAudienceBuilderRepository`'s class remarks, TASK-429) rather than
silently assuming the index helps; every AudienceBuilder query also carries its own redundant,
explicit `TenantId = {0}` filter on top of RLS regardless (defense-in-depth — the non-leakproof
`ILIKE` only blocks the index path, it never disables tenant scoping itself). Real fixes — marking
`texticlike` `LEAKPROOF` after a dedicated security review, or a `SECURITY DEFINER` search function
that re-applies its own hardcoded tenant guard — are each a cross-cutting security-posture change,
out of scope for an indexing task. See `decisions.md` ADR-023 addendum (Фаза 3) for the three-option
tradeoff and why the conservative "accept it" option was picked for v1.

## TASK-455 — Password reset tokens schema (`AddPasswordResetTokens`, 2026-07-30)

**⚠️ Superseded by TASK-464 (2026-08-04).** The link/token-based design documented in this
section was replaced with a temporary-password design (no link, no separate reset step — the
temp password itself becomes the user's real password immediately). `password_reset_tokens`,
`PasswordResetToken`, and `IPasswordResetTokenRepository` were all dropped/deleted; the 3rd
fail-open exception this table held is gone too (back to 2 — see "Documented exceptions" above).
Kept below as historical context only — do not build against anything in this section.

New `password_reset_tokens` table for the forgot/reset-password flow — schema only, no
service/controller yet (TASK-456, backend-developer, next). Same "single active token per user"
shape as `telegram_link_codes`, but the entity is styled like `RefreshToken` (private setters,
`Create()` factory, computed `IsActive`, `MarkUsed()`) rather than `TelegramLinkCode`'s anemic style.

| Table | RLS | Purpose | Key fields |
|---|---|---|---|
| `password_reset_tokens` | fail-open triad — 3rd documented exception, see above | Single-use, time-boxed token for `POST /api/auth/forgot-password` → `reset-password` | `UserId` (FK→users, Cascade), `TokenHash` (unique), `ExpiresAt`, `UsedAt` (nullable), `CreatedAt` |

No own `TenantId` column — tenant is derived transitively through `UserId → users.TenantId`, same
as `refresh_tokens`/`telegram_link_codes`. `tenant_isolation` therefore joins through `users` via
`EXISTS`, with the same `NULLIF(...) IS NULL OR ...` fail-open branch as `refresh_tokens`'s current
live policy (verified byte-for-byte against `20260629010000_FixAllRlsPoliciesNullIfEmptyString`'s
`refresh_tokens` policy before writing this one) — an anonymous forgot/reset-password request has
no `app.tenant_id` yet, since `TenantConnectionInterceptor` only ever `RESET`s session vars for
unauthenticated connections rather than setting them. `provider_bypass` is written as
`IN ('provider', 'provider_admin')` from day one (current convention since
`20260714150000_ExpandProviderBypassToProviderAdmin`), not the single-value form still shown in
this file's own `RLS Template` section above — that template predates the `provider_admin`
expansion and has not been revisited since; treat the `AddPriceSegmentSettings`/`AddLoyaltyProgram`/
this migration's actual SQL as the current source of truth for `provider_bypass`, not that template.

Repository: `IPasswordResetTokenRepository` (`InvalidateActiveTokensAsync` — bulk
`ExecuteUpdateAsync`, same pattern as `ITelegramLinkRepository.InvalidateActiveCodesAsync`;
`AddAsync`, `GetActiveByHashAsync`, `SaveChangesAsync`), deliberately kept separate from
`IUserRepository`/`IRefreshTokenRepository` rather than one more method bolted onto either.

## TASK-464 — Temp-password redesign: drop `password_reset_tokens`, add `users.TempPasswordExpiresAt`
(`DropPasswordResetTokensAddTempPasswordExpiry`, 2026-08-04)

Redesigns the forgot/reset-password flow from TASK-455/456's one-time link/token (live on prod
since commit `647bde4c`, 2026-07-30) to a temporary password the user receives and can log in
with directly — no link, no separate "click link, enter new password" step. Product-owner
decision, not a bug fix.

**Dropped entirely**, schema and code alike (see `## TASK-455` above for what this replaces):
`password_reset_tokens` table (RLS policies went with it — `DROP TABLE` takes a table's policies
with it, no separate `DROP POLICY` needed), `PasswordResetToken` entity,
`IPasswordResetTokenRepository` + its EF Core repository, the DbSet/fluent config in
`AppDbContext`, and the DI registration. The table's fail-open `tenant_isolation` exception is
retired along with it — `TenantIsolationPolicies_HaveNoFailOpenBranch_ExceptDocumentedPreAuthLookups`
(`RlsCrossTenantIntegrationTests.cs`) is back to exactly 2 allowed exceptions (`users`,
`refresh_tokens`), same as before TASK-455.

**Added:** `users.TempPasswordExpiresAt` (nullable `timestamptz`, plain column — no FK, no
index; a single-user, single-row lookup that's already reached via `users`' own PK/email index).
Entity-level (`ShelfGuard.Domain/Entities/User.cs`), styled directly after the pre-existing
`LockoutUntil`/`IsLockedOut` pair (TASK-329) per project convention — private setter, no public
setter, dedicated methods instead of exposing the field directly:

```csharp
public DateTime? TempPasswordExpiresAt { get; private set; }   // private setter

public bool HasActiveTempPassword =>                            // computed, mirrors IsLockedOut
    TempPasswordExpiresAt.HasValue && TempPasswordExpiresAt.Value > DateTime.UtcNow;

public void SetTempPasswordExpiry(DateTime expiresAt) =>        // caller must already have set
    TempPasswordExpiresAt = expiresAt;                           // PasswordHash via ChangePassword

public void ClearTempPasswordExpiry() => TempPasswordExpiresAt = null;
```

No background job expires it — lazily checked (e.g. at login), same pattern as `LockoutUntil`.
`ChangePassword(string newHash)` (pre-existing) is deliberately untouched/does not auto-clear
this field — it's called from both "issue a temp password" (needs to SET the expiry alongside)
and "user sets their own password" (needs to CLEAR it) flows, which want opposite outcomes on the
same call; TASK-465 (backend-developer, next) is expected to call `SetTempPasswordExpiry`/
`ClearTempPasswordExpiry` explicitly alongside `ChangePassword` at each call site rather than
folding the behavior into `ChangePassword` itself. The actual temp-password generation/hashing,
the 3-hour expiry window value, and the login-time enforcement of `HasActiveTempPassword` are
TASK-465's job — this migration only adds the column and the entity-level get/set/clear surface.

**Build note for whoever picks up TASK-465:** deleting `IPasswordResetTokenRepository`/
`PasswordResetToken` leaves `ShelfGuard.Application/Features/Auth/AuthService.cs` (constructor
field + `ForgotPasswordAsync`/`ResetPasswordAsync` bodies, added by TASK-456/460) and four files
under `ShelfGuard.Tests/Auth/` (`AuthServiceTests.cs`, `AuthServiceCapabilitiesTests.cs`,
`TwoFactorAuthTests.cs`, `AuthServiceTabsTests.cs` — the latter three only via a
`Substitute.For<IPasswordResetTokenRepository>()` constructor-injection field, unrelated to their
actual test subjects) not compiling — confirmed by a real `dotnet build ShelfGuard.sln` after
this task's deletions, 2 errors, both `CS0246` on `AuthService.cs:38`/`:52`. Rewriting
`AuthService`'s forgot/reset-password methods for the temp-password design (and fixing the two
`IAuthService` signatures — `ResetPasswordAsync(string rawToken, ...)` no longer makes sense once
there's no token) is TASK-465's actual scope, not a pre-existing bug — this note exists so
TASK-465 doesn't waste time rediscovering it. The EF migration and the `users` schema change
above do not depend on `AuthService` and are unaffected by this — verified by generating and
live-applying the migration through a temporary, fully-reverted stub of `AuthService.cs` (net
diff zero — confirmed via `git diff`/`git status` showing no changes to that file) purely so
`dotnet ef migrations add`/`database update` had a compiling `ShelfGuard.Api` startup graph to
build against; the same technique TASK-465 may find useful if it needs partial compiles mid-work.

## TASK-471 — Post-campaign segment schema (`AddPostCampaignSegmentSchema`, 2026-08-05)

Two new tables (Фаза 4 of the `docs/uployal/` plan, `deep-cooking-nygaard.md`) — the first
**persisted** entity in the whole marketing-analytics initiative. Фаза 1-3 are fully stateless
(everything computed live from `pos_transactions`/`items`/`customers` on every request); Фаза 4
must persist an externally-sourced uploaded customer list, its import-validation results, and the
frozen before/after date windows — see `decisions.md` ADR-023 addendum (Фаза 4) for why.

| Table | RLS | Purpose | Key fields |
|---|---|---|---|
| `post_campaign_segments` | canonical triad only (no `consumer_self_access`) | One row per uploaded/analyzed audience | `TenantId`, `CreatedByUserId` (FK→users, **Restrict**, non-nullable — mirrors `UserPermissionGrant.GrantedByUserId`, not the more common nullable+SetNull `CreatedBy` shape, since a segment always has an owner), `Name?`, `UploadedCount`/`MatchedCount`/`DuplicateCount`/`UnknownCount`/`InvalidCount`, `UnknownTokensSample`/`InvalidTokensSample` (`List<string>` as `jsonb` + `'[]'::jsonb` default, same pattern as `Item.Barcodes`), `AfterStart`/`AfterEnd`/`BeforeStart`/`BeforeEnd` (`DateOnly?` — see below), `SegmentHash`, `CreatedAt`/`AnalyzedAt?` |
| `post_campaign_segment_members` | canonical triad only (no `consumer_self_access`) | One row per matched customer within a segment (unknown/invalid tokens are never materialized here — only counted + sampled on the parent) | `TenantId` (plain denormalized column, **no** separate FK to `tenants` — same treatment `loyalty_ledger_entries.TenantId` already gets), `SegmentId` (FK→post_campaign_segments, **Cascade**), `CustomerId` (FK→customers, **Cascade**). Unique `(SegmentId, CustomerId)` |

Staff-only, same posture as `price_segment_settings` (TASK-419) — no consumer-facing read path
exists to either table at all, so both carry only the canonical fail-closed `tenant_isolation`
(NULLIF-guarded) / `provider_bypass` (`IN ('provider','provider_admin')`) / `worker_bypass` triad,
no identity-based policy (unlike `loyalty_memberships`'s `consumer_self_access` — not applicable
here; this is a staff-only marketing tool, no `ConsumerAccount` session ever touches it).

**The nullability of `AfterStart`/`AfterEnd`/`BeforeStart`/`BeforeEnd` IS the draft-vs-analyzed
state — no separate boolean/enum column.** All four null (together with a null `AnalyzedAt`) means
"imported but not yet analyzed" (draft); all four set means "frozen, computed snapshot" that every
report tab (`summary`/`daily-turnover`/`rfm-activity`/`customers`/`migration`) reads without
re-validating the uploaded list. `POST .../segments/{id}/analyze` is the only place these four
columns are ever written, and re-running it on an already-analyzed segment overwrites all four plus
`SegmentHash`/`AnalyzedAt` in place — it does not create a new segment row. See `glossary.md`
"Draft vs. analyzed segment" and `decisions.md` ADR-023 addendum (Фаза 4) for the full rationale.

Indexes: `idx_post_campaign_segments_tenant_creator` (TenantId, CreatedByUserId),
`idx_post_campaign_segment_members_tenant_segment` (TenantId, SegmentId),
`uq_post_campaign_segment_members_segment_customer` (unique, SegmentId+CustomerId — the hard
backstop against a customer appearing twice in one segment), plus the two EF-default FK indexes
(`CreatedByUserId` on the parent, `CustomerId` on the member table).

Neither table had any pre-existing data to validate a new FK against — both are brand-new
`CREATE TABLE`s, not an `ALTER TABLE ... ADD CONSTRAINT` against an already-populated column — so
the TASK-392/KI-029 FK-validation-under-RLS false positive did not apply here. Applied cleanly via
the app's own non-superuser `shelfguard_app_dev` connection, correct table ownership from the
start, no `FixLoyaltyTableGrants`-style companion migration needed. Live-verified against the real
app role: ownership, policy/flag byte-check (3 policies each, correct qual text), positive path
(insert/select/update, rolled back), unique-constraint backstop, fail-closed with no session vars,
cross-tenant isolation, provider/provider_admin/worker bypass, and cascade-delete (deleting the
parent segment correctly removes its member rows). No new xUnit file — already covered by the 2
existing dynamic RLS audits in `RlsCrossTenantIntegrationTests.cs` that enumerate every FORCE-RLS
table at query time.

## TASK-479 — `pos_transaction_items` product-covering index (`AddPosTransactionItemProductCoveringIndex`, 2026-08-07)

Ahead of TASK-482 (per-product sales trend endpoint, part of the `/analytics`+`/analytics/pos`
interactive-drill-down plan, `iterative-purring-sifakis.md`), added the index that endpoint's query
needs: "filter by `ProductId`, need `Quantity`/`PriceFinal`, need to join to `PosTransactions` for
the date." None of `pos_transaction_items`'s existing indexes covered that shape — it previously had
only a plain `IX_..._ProductId`, a plain `IX_..._ProductStockId`, and a `TransactionId`-leading
covering index (`idx_pos_transaction_items_txn_covering`, added by `20260618153017_
AddPerformanceIndexes`, INCLUDE `ProductId`/`PriceFinal`/`Quantity` — receipt-line lookup by
transaction, not product).

```sql
CREATE INDEX idx_pos_transaction_items_product_covering
  ON pos_transaction_items ("ProductId", "TransactionId")
  INCLUDE ("Quantity", "PriceFinal");
```

**Old plain `IX_pos_transaction_items_ProductId` dropped in the same migration** — same precedent
`AddPerformanceIndexes` already set for the old plain `TransactionId` index once its covering
replacement existed. Confirmed safe three ways, not just asserted:
- **EF Core generated the drop itself.** Adding the new composite `HasIndex(i => new {
  i.ProductId, i.TransactionId })` to `PosTransactionItem`'s fluent config in `AppDbContext.cs` and
  running `dotnet ef migrations add` produced a `DropIndex("IX_pos_transaction_items_ProductId")` +
  `CreateIndex(...)` diff automatically, with no hand-editing — EF's `ForeignKeyIndexConvention`
  recognizes that `ProductId` (the FK to `Item`) is already the *leading* column of the new
  composite index and stops requiring its own single-column index. This is the same mechanism that
  made the old `e.HasIndex(i => i.TransactionId)` calls collapse into one physical index rather than
  two — not a special case written for this task.
- **No live query path relies on a standalone `ProductId` lookup.** Grepped every call site
  touching `pos_transaction_items`/`PosTransactionItems` (`AnalyticsRepository.cs`,
  `MarketingAnalyticsRepository.cs`, `AudienceBuilderRepository.cs`, `PosService.cs`): every one
  either filters by `TransactionId` first (`.Where(i => txQuery.Select(t => t.Id).Contains(i.
  TransactionId))`) or joins `pos_transactions t ON t."Id" = ti."TransactionId"` before ever
  touching `ti."ProductId"` (as a join/`IN` condition, not a leading `WHERE`). No existing query
  would have used a standalone `ProductId` index as its access path anyway.
- **The FK-RESTRICT delete-check use case is still served.** `PosTransactionItem.ProductId → Item`
  is `OnDelete(DeleteBehavior.Restrict)`, so Postgres needs *some* index leading on `ProductId` to
  check "any transaction lines reference this item?" efficiently on delete. The new composite index
  keeps `ProductId` as its first column, so it serves this exactly as well as the old plain index
  did. (In practice this path is rarely exercised — per Architecture Rules below, items are
  soft-deleted via `IsActive`, never hard-deleted — but the index still covers it either way.)

`IX_pos_transaction_items_ProductStockId` was left untouched — out of this task's scope, and it
serves a different (nullable, `SetNull`) FK with no relationship to the new query shape.

**EXPLAIN ANALYZE not run against real volume**: local dev Postgres was reachable and the migration
was live-applied and verified there (`pg_indexes` confirms the exact index shape above, old index
gone), but `pos_transactions`/`items`/`pos_transaction_items` are all currently empty in that
database (0 rows) — a planner will correctly prefer a Seq Scan over any index on a 0-row table
regardless of which indexes exist, so running EXPLAIN ANALYZE now would not produce a meaningful
signal either way. Real plan verification (Index Scan, not Seq Scan) is deferred to TASK-482's own
verification step once the actual repository query exists and can run against realistic data, per
the plan doc's own verification checklist — consistent with how `AddPerformanceIndexes` and
TASK-428's trigram index were verified against seeded/synthetic volume, not an empty table.

Applied via the app's own non-superuser `shelfguard_app_dev` connection (pure `DROP INDEX`/
`CREATE INDEX`, no new FK against populated data, no RLS interaction) — correct table ownership
was never in question here.

## TASK-531 — Mobile Configuration domain schema (`AddMobileConfigurationDomain`, 2026-08-17)

First schema for the multi-tenant consumer app-builder initiative's Stage B (CLAUDE CODE SPEC
ЕТАП 3, `docs/architecture/TARGET_ARCHITECTURE.md` §2). Three new tables — no controllers,
validation service, or API endpoint yet (TASK-532/533/534, separate future tasks).

| Table | RLS | Purpose | Key fields |
|---|---|---|---|
| `mobile_configurations` | canonical triad only | Root/pointer record — one row per tenant | `TenantId` (unique), `PublishedVersionId?` (FK→mobile_configuration_versions, **Restrict**), `DraftVersionId?` (FK→mobile_configuration_versions, **Restrict**), `CreatedAt`, `UpdatedAt` |
| `mobile_configuration_versions` | canonical triad only | Immutable-once-published snapshot of the config document | `MobileConfigurationId` (FK→mobile_configurations, **Cascade** — the owning direction), `TenantId` (denormalized, direct column — same treatment as `LoyaltyLedgerEntry.TenantId`), `Version` (int, unique per config, never reused), `SchemaVersion` (int), `Status` (draft\|published\|archived), `ConfigurationJson` (jsonb, default `'{}'`), `CreatedBy?` (FK→users, SetNull), `CreatedAt`, `PublishedAt?` |
| `mobile_themes` | canonical triad only | Typed, whitelist-validated theme — one row per `MobileConfiguration` (i.e. per tenant, not per version) | `MobileConfigurationId` (FK→mobile_configurations, Cascade, **unique** — enforces one-per-config), `TenantId` (denormalized), `LogoUrl?`, `PrimaryColor`/`SecondaryColor`/`BackgroundColor`/`SurfaceColor`/`TextPrimaryColor`/`TextSecondaryColor` (hex strings), `ButtonRadius`/`CardRadius` (int), `SpacingPreset` (string), `UpdatedAt` |

**Circular FK, resolved the way EF Core/PostgreSQL expect:** `mobile_configurations` points at
`mobile_configuration_versions` twice (`PublishedVersionId`/`DraftVersionId`), and
`mobile_configuration_versions` points back at `mobile_configurations`
(`MobileConfigurationId`) — a genuine table-level cycle. Not a problem here because only one
direction cascades: `MobileConfigurationId → mobile_configurations` is `ON DELETE CASCADE` (the
owning direction — deleting the root config deletes all its versions), while both pointer FKs on
`mobile_configurations` are `ON DELETE RESTRICT` (a version can never be deleted out from under
an active pointer; the app must null the pointer first). `dotnet ef migrations add` resolved the
creation order itself with no hand-editing needed: it emits `CREATE TABLE
mobile_configuration_versions` first (without its FK to `mobile_configurations`, which doesn't
exist yet), then `CREATE TABLE mobile_configurations` (with both Restrict FKs to the
now-existing versions table), then a trailing `ALTER TABLE mobile_configuration_versions ADD
CONSTRAINT ... FOREIGN KEY (MobileConfigurationId) REFERENCES mobile_configurations ... CASCADE`
once both tables exist. This "multiple cascade paths" shape is a hard error under SQL Server but
not under PostgreSQL — verified live (see below), not just assumed.

**`MobileTheme` scoping decision (the task's one open design call):** CLAUDE CODE SPEC ЕТАП 3
lists `MobileTheme` as its own domain entity, separate from the generic `ConfigurationJson` blob,
even though MASTER SPEC §11's example API response also nests a `theme` object inside the same
document. Resolved by scoping `MobileTheme` **per `MobileConfiguration` (per tenant), not per
version** — it is the single, directly-editable working record the future Theme Editor
(TASK-537) reads/writes, enforced by a real DB-level unique constraint
(`uq_mobile_themes_config`) rather than a convention. `MobileConfigurationVersion.
ConfigurationJson` remains the serialized, immutable snapshot: at publish time, the current
`MobileTheme` row (plus future page/block/navigation tables) gets serialized into the new
version's `ConfigurationJson.theme`. This mirrors how a future `MobilePage`/`MobileNavigationItem`
table would relate to the same version snapshot. See `.claude/docs/domain-model.md`'s
`MobileTheme` entry for the same rationale from the domain-model side.

Applied via the app's own non-superuser `shelfguard_app_dev` connection (all three tables are
brand-new, empty `CREATE TABLE`s — no FK-validation-under-RLS false positive, same as TASK-471).
Table ownership confirmed correct from the start (`shelfguard_app_dev` on all three, no
`FixLoyaltyTableGrants`-style companion migration needed). Live-verified: `\d+` on all three
tables shows the exact FK/RESTRICT/CASCADE shape above plus all three RLS policies
(`tenant_isolation`, `provider_bypass` as `IN ('provider','provider_admin')`, `worker_bypass`)
under `FORCE ROW LEVEL SECURITY`. Migration `Down()` round-tripped cleanly through the real
non-superuser connection: rolled back to the prior migration (all three tables gone, confirmed via
`pg_tables`), then reapplied (all three tables and policies back). Full `dotnet test` — including
the dynamic `RlsCrossTenantIntegrationTests` suite that enumerates every `FORCE ROW LEVEL SECURITY`
table at query time — passed at 1411/1411 both before and after, with no new failures introduced.

## TASK-592 — `demand_event_stores`: event↔specific-stores join (`AddDemandEventStores`, 2026-08-22)

New third `demand_events.Scope` value `"stores"` (several specific stores), alongside the
existing `"network"` (all stores) / `"store"` (single `demand_events.LocationId`). Schema
layer only — repository/service query logic, scope validation, CRUD endpoints, and
frontend are a follow-up wave (see `.claude/logs/handoffs/592-to-backend_database-engineer.md`).

| Table | RLS | Purpose | Key fields |
|---|---|---|---|
| `demand_event_stores` | tenant_isolation (EXISTS→`demand_events`) + provider_bypass + worker_bypass, FORCE RLS | Many-to-many: which specific stores an event targets when `Scope == "stores"` | `EventId` (FK→demand_events, Cascade), `StoreId` (FK→locations, Cascade); UNIQUE(EventId, StoreId) |

Same shape as `demand_event_coefficients` (no own `TenantId`, tenant derived via `EXISTS`
into `demand_events`), but with a `WITH CHECK` matching `USING` (this table is written to
directly, unlike coefficients which the older policy only read) and a unique composite
index — `demand_event_coefficients` deliberately allows duplicate scope rows, this table
doesn't (it only ever answers "is store X targeted", so a duplicate is meaningless).
`provider_bypass` written as the current `IN ('provider','provider_admin')` form; the
`demand_event_coefficients`/`demand_events` policies from `V2EventsWeather`
(2026-06-11) predate that convention and were not backfilled here — out of scope.

Physical FK target for "store" is `locations`, not `stores` — `V2EventsWeather`'s original
`demand_events.StoreId → stores` FK predates the v4 Store→Location table rename; the C#
navigation property is still named `Store`/`StoreId` (mapped to column `LocationId`) but
the physical table has been `locations` since v4.

## TASK-613 — Customer/loyalty domain expansion: profile-change audit, tier ladder, support
tickets, purchase reviews (`AddConsumerAccountProfileChanges`/`AddLoyaltyTierLadder`/
`AddConsumerSupportTickets`/`AddPurchaseReviews`/`AddPosTransactionCashRegisterId`, 2026-08-24)

Six new/extended pieces of schema for the CRM/loyalty expansion (plan `goofy-bubbling-naur.md`,
TASK-613..622 — see `decisions.md` ADR-034 for the judgment calls, `domain-model.md` for the
entity relationships). Five separate migrations, generated one at a time behind temporary `#if`
staging guards later removed — the shipped code has zero preprocessor directives, each migration
just landed as its own reviewable unit.

| Table | RLS | Purpose | Key fields |
|---|---|---|---|
| `consumer_account_profile_changes` | **none** — same precedent as `consumer_accounts` itself (see TASK-404 above) | Append-only audit trail of self-service name/email/phone edits | `ConsumerAccountId`, `FieldName` (`ConsumerAccountProfileChangeField`: `phone`/`email`/`full_name`), `OldValue`, `NewValue`, `ChangedAt` — all `init`-only |
| `loyalty_tier_definitions` | tenant_isolation / provider_bypass / worker_bypass | Per-tenant tier ladder rung, admin-configured | `TenantId`, `Name`, `SortOrder`, `MinCompositeScore`, `AccrualMultiplier` (default 1.0), `DiscountPercent` (default 0), `CreatedAt`/`UpdatedAt`. Unique `(TenantId, SortOrder)` |
| `loyalty_tier_change_history` | + `consumer_self_access` (EXISTS via membership) | Append-only tier-progression audit, written only by the nightly recompute worker job (TASK-619) | `TenantId`, `MembershipId`, `FromTierId?`/`ToTierId?`, `FromScore`, `ToScore`, `ChangedAt` — all `init`, mirrors `LoyaltyLedgerEntry`'s discipline |
| `consumer_support_tickets` | + `consumer_self_access` (direct `ConsumerAccountId` column) | Consumer↔tenant support ticket, mirrors `SupplierSupportTicket` | `TenantId`, `ConsumerAccountId`, `CustomerId?` (auto-link target, nullable, never force-created), `Subject`, `Status` (`ConsumerSupportTicketStatus`: open/in_progress/resolved/closed), `CreatedAt`/`UpdatedAt`, nav `Messages` |
| `consumer_support_ticket_messages` | tenant_isolation + `consumer_self_access`, both via EXISTS-through-`TicketId` | One message in a ticket thread | `TicketId`, `SenderConsumerAccountId?`/`SenderUserId?` (**exactly one set per row**), `Body`, `IsRead`, `CreatedAt` |
| `purchase_reviews` | + `consumer_self_access` (direct `ConsumerAccountId` column) | One review per completed purchase, mirrors `SupplierReview` | `TenantId`, `ConsumerAccountId`, `PosTransactionId` (**unique** — one review per purchase), `Rating` (short, 1-5), `Comment`, `CreatedAt`, `ReplyText`/`RepliedAt`/`RepliedByUserId` (one reply, enforced app-side) |

**`LoyaltyMembership` extension** (same migration wave as `loyalty_tier_definitions`):
`CurrentTierId` (Guid?, FK→`loyalty_tier_definitions`, **SetNull**), `CompositeScore` (decimal,
default 0), `TierScoreUpdatedAt` (DateTimeOffset?), nav `CurrentTier`. **Nothing writes these
three columns except the nightly `loyalty-tier-recompute.job.ts` worker job (TASK-619)** —
request-time code (`PosService`, `LoyaltyService`) must never touch them, by design: they'd
otherwise conflict with the `xmin` optimistic-concurrency token TASK-414 put on `Balance`. See
`decisions.md` ADR-034 and `domain-model.md`'s `LoyaltyMembership` entry.

**`PosTransaction.CashRegisterId`** (same wave, its own migration): nullable `Guid`, **no FK, no
business logic** — schema-ready only, register hardware doesn't exist in this codebase yet. Do
not wire this up without a fresh task; it is deliberately inert.

All 6 tenant-scoped tables got `worker_bypass` from creation (past-incident lesson, see the
`worker_bypass` note in the RLS Template above); `provider_bypass` written as
`IN ('provider', 'provider_admin')` (current convention).

**EF phantom-FK bug caught during migration generation** — worth knowing for any future entity
with both a scalar FK id and its own navigation property: writing
`e.HasOne<ConsumerAccount>().WithMany().HasForeignKey(x => x.ConsumerAccountId)` on an entity that
*also* declares a `ConsumerAccount` navigation property makes EF Core create a second, phantom
relationship — a shadow `ConsumerAccountId1` FK/column appears in the generated migration
alongside the real one. Hit on both `ConsumerAccountProfileChange` and `PurchaseReview`; fixed by
using `e.HasOne(x => x.ConsumerAccount).WithMany()` (binding the FK to the actual nav property)
instead. Regenerated migrations have no shadow properties.

Live-verified (dev DB, non-superuser `shelfguard_app_dev` role, inside rolled-back transactions):
`pg_class.relrowsecurity/relforcerowsecurity` — `f/f` on `consumer_account_profile_changes`,
`t/t` on the other 5; tenant isolation and `worker_bypass` on `loyalty_tier_definitions`;
`consumer_self_access` on `purchase_reviews` (wrong `app.consumer_account_id` → 0 rows, owning
consumer → 1 row). Independently re-verified by QA (TASK-622): consumer-A/consumer-B isolation,
staff sees all tenant rows regardless of owner, cross-tenant staff sees 0 rows, `worker_bypass`
sees everything — and, worth remembering if this class of result ever looks like a data-loss false
alarm again, an unscoped query with **no** session vars set returns 0 rows on every policy
(fail-closed default), which briefly looked like wiped dev data during TASK-622's own verification
pass before the RLS explanation was confirmed.

## TASK-649 — Supplier performance data (`AddSupplierPerformanceData`, 2026-08-31)

Pure additive DDL for the marketplace supplier delivery-coverage + performance-metrics feature
(plan `eventual-whistling-rabbit.md`, TASK-648..661 — see `decisions.md` ADR-036 for the design
calls, `domain-model.md` for the entity relationships). Previous migration:
`20260831060145_AddCustomerMessageDeliveryLifecycle`.

**No new tables. No RLS policy changes.** The four target tables already carry
`tenant_isolation` + `provider_bypass` + `worker_bypass` under `FORCE ROW LEVEL SECURITY`, and the
new columns inherit them — the migration's class-level XML doc states this explicitly. Live-verified
(dev, non-superuser `shelfguard_app_dev` role): `pg_policies` on the four tables identical
before/after (3 policies each), `relrowsecurity`/`relforcerowsecurity` = `t/t`, table ownership
unchanged. The `AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass`
audit ran and passed unchanged (no new FORCE-RLS table).

### Columns added (all nullable, no FK, no default)

| Table | Column | Type | Purpose |
|---|---|---|---|
| `locations` | `RegionCode` | `varchar(20)` | Location's Ukraine region code (`UkraineRegions`); set via the location form, NULL for all existing rows (not backfilled) |
| `marketplace_orders` | `DestinationRegionCode` | `varchar(20)` | **Point-in-time snapshot** of the destination `Location.RegionCode` at order creation (ADR-036 Decision 2) — not a live join; NULL for all pre-migration orders |
| `supplier_profiles` | `DeliveryCoverage` | `jsonb` | Structured supplier-declared coverage (`served`/`notServed`/`note`); supersedes `DeliveryRegions` (now `[Obsolete]`, column kept, drop later) |
| `supplier_metrics` | `DeliveryByRegion` | `jsonb` | Worker-computed `[{regionCode, avgDeliveryDays, sampleSize}]` |
| `supplier_metrics` | `DeliverySampleSize` | `integer` | Worker-computed overall delivered-order count (365d window) |
| `supplier_metrics` | `ResponseSampleSize` | `integer` | Worker-computed answered-chat-session count (180d window) |
| `supplier_metrics` | `AggregatesComputedAt` | `timestamptz` | Last `supplier-metrics-recompute` run |

> `varchar(20)`, not the plan's `varchar(12)` — a city code (`UA-XX-LONGTRANSLIT`, e.g.
> `UA-12-KRYVYI-RIH`) is ~15 chars. `AppDbContext` uses `HasMaxLength(20)` on both region-code
> columns; `DeliveryCoverage`/`DeliveryByRegion` use `HasColumnType("jsonb")` like `Item.Categories`.
> `SupplierProfile.DeliveryRegions` mapping wrapped in `#pragma warning disable CS0618`.

### Indexes added

| Index | Table | Shape | Why |
|---|---|---|---|
| `IX_supplier_chat_messages_SessionId_SenderTenantId_CreatedAt` | `supplier_chat_messages` | plain composite, EF-tracked | The `supplier-metrics-recompute` job's first-reply-latency query filters `(SessionId, SenderTenantId)` and orders by `CreatedAt`; the existing single-column `SessionId`/`CreatedAt` indexes didn't cover it |
| `ix_marketplace_orders_metrics` | `marketplace_orders` | **partial**: `("SupplierTenantId","DeliveredAt") WHERE "Status" = 'delivered'` | The same job scans one supplier's delivered orders in a rolling window. Hand-written via `migrationBuilder.Sql(...)` (EF does not emit the `WHERE` filter) — not tracked in the model snapshot, same treatment as the project's other raw-SQL indexes/policies |

`Down()` reverses everything symmetrically (partial index via `DROP INDEX IF EXISTS`, then EF
`DropIndex` + 7 `DropColumn`); round-trip verified on the dev DB.

**Worker write-boundary on `supplier_metrics` (load-bearing — see ADR-036 Decision 4):** the nightly
`supplier-metrics-recompute.job.ts` writes only `AvgDeliveryDays`/`DeliverySampleSize`/
`DeliveryByRegion`/`ResponseTimeHours`/`ResponseSampleSize`/`CancellationRate`/`OrderAccuracy`/
`AggregatesComputedAt` (+ `SupplierId`/`TenantId` on INSERT). It must **never** write `Rating`
(owned by the synchronous `MarketplaceRepository.UpsertMetricsRatingAsync`, ADR-035) or
`QualityScore`. `supplier_metrics` has **no `xmin`** — the two writers are safe only because they
touch disjoint columns via separate `UPDATE` statements; any future "upsert all metrics" path needs
an explicit concurrency token first.

## TASK-670 — Supplier metrics history (`AddSupplierMetricsHistory`, 2026-09-01)

New table `supplier_metrics_snapshots` — append-only daily copy of a supplier's aggregate
metrics, written by the nightly supplier-metrics worker job (idempotent upsert), feeding the
buyer-facing metric trend-chart detail page. Previous migration:
`20260831090731_AddSupplierPerformanceData`.

| Column | Type | Notes |
|---|---|---|
| `Id` | `uuid` PK | `gen_random_uuid()` default |
| `SupplierId` | `uuid` | FK → `suppliers.Id` **CASCADE** (matches `supplier_metrics`) |
| `TenantId` | `uuid` | FK → `tenants.Id` RESTRICT; the RLS tenant column |
| `SnapshotDate` | `date` | the calendar day this row represents |
| `AvgDeliveryDays` | `numeric(5,2)` NULL | column types mirror `supplier_metrics` exactly |
| `OrderAccuracy` | `numeric(5,4)` NULL | |
| `QualityScore` | `numeric(5,4)` NULL | |
| `Rating` | `numeric(3,2)` NULL | |
| `CancellationRate` | `numeric(5,4)` NULL | |
| `ResponseTimeHours` | `numeric(6,2)` NULL | |
| `DeliverySampleSize` | `integer` NULL | |
| `ResponseSampleSize` | `integer` NULL | |
| `CreatedAt` | `timestamptz` | `NOW()` default |

**Indexes:**
- `idx_supplier_metrics_snapshots_supplier_date` — **UNIQUE** `(SupplierId, SnapshotDate)`.
  One row per supplier per day; the worker upserts `ON CONFLICT (SupplierId, SnapshotDate)`.
  Also serves the buyer history query (`WHERE SupplierId = ? ORDER BY SnapshotDate DESC`) via a
  backward b-tree index scan — **no dedicated `DESC` index added** (judged unnecessary).
- `IX_supplier_metrics_snapshots_TenantId` — leading index on the RLS tenant column (Block 16 rule).

**RLS — full triad added explicitly** (new tables don't auto-inherit — `feedback-rls-worker-bypass-missing`).
Policy SQL copied verbatim from the live `supplier_metrics` policies (no `WITH CHECK`):
```sql
ALTER TABLE supplier_metrics_snapshots ENABLE ROW LEVEL SECURITY;
ALTER TABLE supplier_metrics_snapshots FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON supplier_metrics_snapshots
  USING ("TenantId" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
CREATE POLICY provider_bypass ON supplier_metrics_snapshots
  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
CREATE POLICY worker_bypass ON supplier_metrics_snapshots
  USING (current_setting('app.role', true) = 'worker');
```
`Down()` drops the 3 policies + `DISABLE ROW LEVEL SECURITY` + `DropTable`, symmetric.

Applied to the dev DB via the non-superuser `shelfguard_app_dev` connection (brand-new empty FK
columns don't trip the FK-validation-under-RLS false positive — no `crm` superuser escape hatch
needed; table ends up owned by `shelfguard_app_dev`, correct grants). Live-verified: 3 policies
present, `relrowsecurity`/`relforcerowsecurity` = `t/t`, `Down()` round-trip clean (table + policies
gone, then fully restored). `AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass`
audit test passes with the new table present.

## Architecture Rules
- `expiry_date` and `batch_number` are NEVER modified on transfer — copied as-is to `stock_transfer_items`
- All soft deletes via `is_active`, never hard DELETE on business data
- UUID PKs with `gen_random_uuid()` default
- All timestamps in UTC (`TIMESTAMPTZ`)
