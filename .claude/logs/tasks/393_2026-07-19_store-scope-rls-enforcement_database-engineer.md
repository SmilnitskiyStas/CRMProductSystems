# TASK-393: Store-scoped data visibility — RLS enforcement cutover (Stage 3, Feature 2)

**Agent:** database-engineer
**Date:** 2026-07-19
**Status:** done — migration written, live-verified locally, committed **locally only, NOT pushed**

## ⚠️⚠️ CRITICAL — DO NOT APPLY THIS MIGRATION TO PRODUCTION YET ⚠️⚠️

`AddLocationStoreScopeRlsPolicies` must not run against production until Stage 2's manual
backfill (every network_manager/store_manager/merchandiser/storekeeper/cashier/staff user gets
at least one `user_locations` row) reaches **zero gap**, verified via the coverage-gap report in
`.claude/docs/store-scope-rollout-checklist.md`. The instant this migration applies, any affected
user with zero `user_locations` rows gets **zero visible rows** on all 9 scoped tables —
immediate, total functional outage for that user (stock/sales/POS/write-offs/everything), not a
gradual degradation. Full checklist + coverage-gap SQL is in that doc.

## Зроблено

1. **Migration `AddLocationStoreScopeRlsPolicies`**
   (`backend/ShelfGuard.Infrastructure/Migrations/20260719193545_AddLocationStoreScopeRlsPolicies.{cs,Designer.cs}`)
   — RESTRICTIVE `store_scope` policy on 9 tables: `product_stock`, `daily_sales`, `pos_shifts`,
   `pos_transactions`, `write_offs`, `discounts`, `stock_receipts` (one-sided), `stock_movements`,
   `stock_transfers` (two-sided). Generated via `dotnet ef migrations add` (empty diff, as
   expected — RLS policies aren't tracked by EF's model snapshot), SQL hand-written into `Up()`/
   `Down()`, matching how `AddSupplierChat`/`AddUserLocations` handled RLS SQL before it.

2. **Column names/shapes verified against the live EF model** before writing SQL (not assumed
   from the brief) — found and corrected two mismatches vs. the brief's guess:
   - `stock_receipts` — brief assumed two-sided; actual entity has only **one** store column,
     `DestinationLocationId` (a receipt comes from a supplier, not another store).
   - `stock_movements` — brief assumed one-sided; actual entity has **two** nullable columns,
     `FromLocationId`/`ToLocationId` (e.g. a receipt-origin movement populates only `To`).
   All other columns are `"LocationId"` (physical) even though the C# property is `StoreId`
   (pre-v4 naming convention, `HasColumnName` mapping — same as `ProductStock`/`WriteOff` etc.).

3. **Bypass roles: `provider`, `provider_admin`, `worker`, `enterprise_admin`.** The brief's exact
   list was `provider`/`worker`/`enterprise_admin`. I added `provider_admin` — flagged as a
   deliberate deviation, not a re-litigation of the access model: `20260714150000_
   ExpandProviderBypassToProviderAdmin` already gives `provider_admin` full bypass parity with
   `provider` on all 9 of these tables via the existing permissive `provider_bypass` policy.
   Without adding it here, this migration would have silently revoked that already-established,
   already-audited access the instant it applied — an unrelated regression, not something the
   brief asked for. Live-verified `provider_admin` still bypasses correctly (Theory test, see
   below). `provider_agent` was correctly left out — it never had bypass on these 9 tables.

4. **No child-table policies needed** (`stock_receipt_items`, `stock_transfer_items`,
   `write_off_items`, `pos_transaction_items`) — their existing `tenant_isolation` policies are
   `EXISTS`-into-the-parent, and Postgres re-applies a referenced table's RLS (RESTRICTIVE
   included) inside any subquery/join that reads it, so they inherit the new scoping for free
   (same mechanism `supplier_chat_messages` already relies on for its parent's tenant scoping).

5. **New permanent regression tests**
   (`backend/ShelfGuard.Tests/Infrastructure/StoreScopeRlsIntegrationTests.cs`, 9 tests) —
   same real-Postgres/throwaway-role pattern as the existing `RlsCrossTenantIntegrationTests`.
   Covers: schema completeness (all 9 tables have a RESTRICTIVE, not PERMISSIVE, `store_scope`
   policy); `enterprise_admin` bypass with zero `user_locations` rows; scoped user with one
   location sees only that location; scoped user with zero locations sees zero rows (fail-closed);
   two-sided OR-match on `stock_transfers`; `provider`/`provider_admin`/`worker` bypass (Theory,
   3 cases); store_scope ANDs on top of (does not replace) tenant_isolation.

6. **Docs**: `.claude/docs/database-schema.md` — new "TASK-393 Stage 3" section (policy shape,
   bypass-role rationale, the two column-name/shape corrections, live-verification summary).
   `.claude/docs/store-scope-rollout-checklist.md` (new) — step-by-step gate for the product
   owner, coverage-gap SQL (tested against dev, working), emergency rollback command.

## Верифікація (жива, не тільки "перевірено")

- `dotnet build` (full solution, `--no-incremental`) — 0 errors, 1 pre-existing warning
  (`MarketplaceServiceTests.cs:534`, predates this task).
- `dotnet test` — **901/901 passed** (baseline 892 from TASK-392b + 9 new).
- Migration applied to local dev Postgres (`crmproductsystems-postgres-1`, port 5435) **through
  the actual restricted `shelfguard_app_dev` role** (non-superuser, same connection
  `Program.cs`'s `MigrateAsync()` uses in production) — confirmed via `pg_policies`: all 9 tables
  have `store_scope`, `permissive = 'RESTRICTIVE'`.
- **Rollback/reapply round-trip tested**: `dotnet ef database update AddUserLocations` (rolls
  back this migration) → confirmed 0 `store_scope` policies remain → `dotnet ef database update`
  (reapply) → confirmed all 9 back. Clean both directions.
- **Manual live scenario script** (`psql`, throwaway `rls_audit_test_role` NOSUPERUSER/
  NOBYPASSRLS role, same fixture pattern as `RlsCrossTenantIntegrationTests`) — concrete results:
  - `enterprise_admin`, 0 `user_locations` rows → 2/2 product_stock rows visible (both locations).
  - `store_manager` assigned to Location X only → 1/2 rows visible, exactly the X row.
  - `store_manager`, 0 `user_locations` rows → 0/2 rows visible (fail-closed, expected per brief).
  - `stock_transfers` (two-sided): user assigned to X, transfers X→Y / Y→X / Y→Z seeded → sees
    exactly 2 (X→Y and Y→X), correctly excludes Y→Z.
  - `worker` role, 0 `user_locations` rows → 2/2 visible (bypass).
  - `provider_admin` role, 0 `user_locations` rows → 2/2 visible (bypass — confirms the added
    correction in item 3 above actually works).
  - Correctly-assigned user, but `app.tenant_id` set to an unrelated tenant → 0/2 visible
    (confirms RESTRICTIVE store_scope ANDs on top of tenant_isolation, doesn't replace it).
  - All test data cleaned up after (verified via DELETE row counts, no leftover rows).
- Coverage-gap SQL query (for the rollout checklist) test-run against local dev: returns 8
  affected users across 5 roles/1 tenant — expected, dev has never been backfilled since Stage 3
  hasn't shipped anywhere. Confirms the query itself is correct and the checklist's gate would
  correctly block a dev-state deploy.
- Git: local commit only, **no push** (explicit instruction, matches Stage 1/392b's same pause).

## Не в скоупі (свідомо)

- Actually running the backfill or the coverage-gap report against production — that's the
  product owner's call, whenever Stage 3 is scheduled. See the rollout checklist.
- CI does not run these Postgres integration tests today (no Postgres service in
  `.github/workflows/ci.yml`, same pre-existing limitation as `RlsCrossTenantIntegrationTests`).
