# TASK-343: worker_bypass RLS policy hotfix

**Agent:** database-engineer
**Status:** done
**Type:** critical hotfix (prod incident, RLS blocking worker writes)

## Problem

Worker cron jobs (`worker/src/jobs/*.job.ts`) run `SET app.role = 'worker'` then raw SQL
against RLS-protected tenant tables. No RLS policy anywhere in the project recognizes
`app.role = 'worker'` — only `provider_bypass` (`app.role = 'provider'`) exists. Combined
with `FORCE ROW LEVEL SECURITY` (migration `20260628000000_ForceRlsOnAllTenantTables`),
every worker write against a table whose `tenant_isolation` policy has no permissive
fallback is silently blocked by Postgres (no error — rows are just filtered/rejected).

Confirmed in prod via direct SQL: `stock_status_snapshots` had 0 rows despite the nightly
worker job running without errors.

## Audit — tables with FORCE ROW LEVEL SECURITY

Walked every migration under `backend/ShelfGuard.Infrastructure/Migrations/` (26 files
with `ENABLE ROW LEVEL SECURITY`, cross-checked against `AppDbContextModelSnapshot.cs`
`ToTable(...)` calls for current table names post-rename). Result: **73 of 82** tables
tracked by `AppDbContext` have FORCE RLS and now got `worker_bypass`.

The 9 tables with **no RLS at all** (excluded, no policy needed):
`Products` (legacy/unused entity from `InitialCreate`), `chat_messages`,
`support_messages`, `production_order_consumptions`, `provider_roles`,
`provider_schedule_slots`, `recipe_ingredients`, `landing_leads`, `tenants`.

Full list of the 73 fixed tables is in the migration file itself
(`20260712175141_AddWorkerBypassRlsPolicy.cs`, `TableArray` constant) — not duplicated
here per token-efficiency convention.

Renames applied when resolving historical `ENABLE ROW LEVEL SECURITY` statements to
current table names: `stores`→`locations`, `store_zones`→`location_zones`,
`catalog_products`→`items` (per `V4LocationsRename` / `V4ItemsRename`). Child tables
(`product_stock`, `product_adu`, `product_buffer`, `product_supplier_settings`,
`product_segments`) kept their original `product_*` names despite the `Item` entity
rename — confirmed against the model snapshot, not guessed.

## Migration

`backend/ShelfGuard.Infrastructure/Migrations/20260712175141_AddWorkerBypassRlsPolicy.cs`

- `Up()`: `DO $$` block, `FOREACH t IN ARRAY [...73 tables...]`, per table:
  `DROP POLICY IF EXISTS worker_bypass` then
  `CREATE POLICY worker_bypass ON <t> USING (current_setting('app.role', true) = 'worker')`
  — same shape as the existing `provider_bypass`, non-throwing `current_setting(..., true)`.
- `Down()`: same loop, `DROP POLICY IF EXISTS worker_bypass` only.
- Additive only — no changes to existing `tenant_isolation`/`provider_bypass` policies,
  no data migration, no downtime.

## Verification

- `dotnet build` (full solution): **0 errors**, 1 pre-existing unrelated warning in
  `ShelfGuard.Tests/Marketplace/MarketplaceServiceTests.cs`.
- `dotnet ef migrations script 20260712170225_AddUserPermissionGrants
  20260712175141_AddWorkerBypassRlsPolicy` — generated SQL inspected: all 73 table names
  present exactly once in the `ARRAY[...]` literal, no duplicates, no typos.

## Deploy note

Not yet deployed — this is a migration-only change. Needs `dotnet ef database update`
(or the standard deploy.sh path that runs pending migrations) against prod after
security-review sign-off, per the hotfix instructions.

## Separate issue found (not fixed here, handed off)

While auditing, found `notification_queue`'s `tenant_isolation` policy already has a
permissive `NULLIF(...) IS NULL OR ...` fallback (predates this task) — worker already
writes through it by accident. Also found the *same* permissive-fallback pattern on
`chat_sessions` (`20260621161638_AddChatFeature.cs`) and on 5 supplier tables
(`suppliers`, `supplier_profiles`, `supplier_items`, `supplier_metrics`,
`supplier_reviews` — widened by `20260702192126_V41SupplierSelfService.cs`). See
handoff `343-to-344_security-reviewer.md`.
