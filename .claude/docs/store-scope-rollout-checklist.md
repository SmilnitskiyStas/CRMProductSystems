# Store-Scope RLS Enforcement — Production Rollout Checklist

**Owner:** database-engineer (TASK-393, Stage 3 of the user-locations initiative)
**Migration this gates:** `AddLocationStoreScopeRlsPolicies`
(`backend/ShelfGuard.Infrastructure/Migrations/20260719193545_AddLocationStoreScopeRlsPolicies.cs`)
**Status as of 2026-07-19:** migration written, tested, and committed **locally on `main` only —
NOT pushed, NOT deployed anywhere.** This checklist is for the product owner to work through
later, whenever Stage 3 is scheduled to actually go live.

---

## ⚠️⚠️ READ THIS BEFORE DOING ANYTHING ELSE ⚠️⚠️

**This migration must NEVER be applied to production until every step below is checked off.**

The moment this migration runs against a database, every `network_manager` / `store_manager` /
`merchandiser` / `storekeeper` / `cashier` / `staff` user who does **not** already have at least
one row in `user_locations` will **instantly see zero rows** on `product_stock`, `daily_sales`,
`pos_shifts`, `pos_transactions`, `write_offs`, `discounts`, `stock_receipts`,
`stock_movements`, and `stock_transfers`.

This is **not** a gradual degradation and **not** a partial-functionality issue. It is a
**complete, immediate functional outage** for that user across stock visibility, sales entry,
POS, write-offs, transfers — effectively their entire job — the instant the migration commits,
with no warning and no grace period. Every affected user hits this at the same moment, across
every tenant, the second the migration applies. There is no safe way to "try it and see" in
production; the only safe order is: **backfill first, verify zero gap, then migrate.**

(`enterprise_admin` is unaffected — unconditional bypass, no `user_locations` rows needed.
`provider`/`provider_admin`/`worker` are also unaffected — same bypass mechanism.)

---

## Step-by-step

### 1. Confirm Stage 1 + Stage 2 (API/UI layer) are deployed to production

The `user_locations` table, the `PUT/GET /api/users/{id}/locations` endpoints, and the
store-scoped assignment UI (TASK-392, TASK-392b, TASK-392c) must already be live in production
— admins need a working way to assign locations before anyone can close the coverage gap below.
If these are not yet deployed, stop here.

### 2. Run the coverage-gap report against production (read-only)

This is a **read-only SELECT** — safe to run any time, does not require a maintenance window.
Run it via `psql`/any DB client connected to the production database:

```sql
-- Coverage gap report: active users in a store-scoped role with ZERO user_locations rows,
-- grouped by tenant + role. Every row returned here is a user who will see EMPTY data on
-- product_stock/daily_sales/pos_shifts/pos_transactions/write_offs/discounts/stock_receipts/
-- stock_movements/stock_transfers the moment AddLocationStoreScopeRlsPolicies is applied.
SELECT
  t."Id"   AS tenant_id,
  t."Name" AS tenant_name,
  u."Role" AS role,
  count(*) AS users_missing_location_assignment
FROM users u
JOIN tenants t ON t."Id" = u."TenantId"
WHERE u."IsActive" = true
  AND u."Role" IN ('network_manager', 'store_manager', 'merchandiser', 'storekeeper', 'cashier', 'staff')
  AND NOT EXISTS (
    SELECT 1 FROM user_locations ul WHERE ul."UserId" = u."Id"
  )
GROUP BY t."Id", t."Name", u."Role"
ORDER BY t."Name", u."Role";
```

Quick total-only variant, if a single yes/no number is more useful for a go/no-go check:

```sql
SELECT count(*) AS total_users_missing_location_assignment
FROM users u
WHERE u."IsActive" = true
  AND u."Role" IN ('network_manager', 'store_manager', 'merchandiser', 'storekeeper', 'cashier', 'staff')
  AND NOT EXISTS (SELECT 1 FROM user_locations ul WHERE ul."UserId" = u."Id");
```

Both queries were run and confirmed working against the local dev database while writing this
checklist (2026-07-19) — dev currently has 8 affected users across 5 roles/1 tenant (expected:
dev has never been backfilled, since Stage 3 hasn't shipped anywhere yet). **Production's real
numbers must be pulled fresh** — do not reuse dev's numbers for any go/no-go decision.

### 3. Backfill — assign locations to every listed user

For each `(tenant, role, user)` surfaced by the report above, an admin (`enterprise_admin` or
`network_manager`, per the permission model already shipped in TASK-392b/392c) assigns the
correct location(s) via the UI, or `PUT /api/users/{id}/locations`. This is inherently a manual,
per-tenant, per-user business decision (which real-world store each person actually works at) —
not something to script/guess programmatically.

### 4. Re-run the report — repeat until it returns zero rows, for every tenant

Do not proceed on a partial pass. A tenant with even one remaining gapped user will have that one
user's access break completely the moment the migration runs. Re-run step 2's query as many
times as needed after each round of assignments.

### 5. Only now — apply the migration to production

```bash
# Standard deploy path — MigrateAsync() in Program.cs applies pending migrations on container
# startup, same as every other migration. No special flag needed once the gap is confirmed zero.
```

Recommended: apply during a low-traffic window regardless, and have step 2's report ready to
re-run immediately after, as a fast confirmation that nothing new slipped through between the
last check and the deploy (e.g. a user invited in the gap between step 4 and step 5).

### 6. Post-deploy smoke check

Log in as (or ask a real) `store_manager`/`cashier` and confirm they still see their store's
stock/POS/sales as expected. Confirm an `enterprise_admin` still sees the full tenant. This is
the fast, cheap confirmation that the backfill data and the new policy actually line up in
practice, not just in the coverage report's count.

---

## Emergency rollback

If something goes wrong after this migration has already applied in production, the migration's
`Down()` removes all 9 `store_scope` policies in one shot (tested locally — clean rollback and
reapply round-trip, see `.claude/logs/tasks/393_2026-07-19_store-scope-rls-enforcement_database-engineer.md`):

```bash
dotnet ef database update AddUserLocations --project ShelfGuard.Infrastructure --startup-project ShelfGuard.Api
```

This immediately reverts every scoped table to tenant-wide visibility for all tenant-staff roles
(the pre-Stage-3 behavior) — safe as a fast unblock, but re-opens the same access model Stage 3
was meant to close, so treat it as a temporary escape hatch while investigating, not a resolution.
