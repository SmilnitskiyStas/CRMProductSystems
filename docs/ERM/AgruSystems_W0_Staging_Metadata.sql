-- W0 metadata inspection only. Run against the intended staging database.
-- No migrations, business writes, role changes or RLS bypass settings.
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL statement_timeout = '30s';
SET LOCAL lock_timeout = '2s';

SELECT current_database() AS database_name,
       current_user AS effective_role,
       session_user AS session_role,
       current_setting('server_version') AS server_version;

-- Inspect these flags for the actual API/worker login roles separately too.
SELECT rolname, rolsuper, rolbypassrls, rolinherit, rolcanlogin
FROM pg_roles
WHERE rolname IN (current_user, session_user);

-- Expected by this repository; absence means the migration baseline needs review.
SELECT "MigrationId", "ProductVersion"
FROM public."__EFMigrationsHistory"
ORDER BY "MigrationId";

-- Include every public table so unlisted equivalents remain visible.
SELECT n.nspname AS schema_name, c.relname AS table_name,
       pg_get_userbyid(c.relowner) AS owner_role,
       c.relrowsecurity AS rls_enabled,
       c.relforcerowsecurity AS rls_forced
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p')
ORDER BY c.relname;

SELECT schemaname, tablename, policyname, permissive, roles, cmd, qual, with_check
FROM pg_policies
WHERE schemaname = 'public'
ORDER BY tablename, policyname;

SELECT table_schema, table_name, ordinal_position, column_name, data_type,
       udt_name, is_nullable, column_default
FROM information_schema.columns
WHERE table_schema = 'public'
  AND table_name IN (
      'Products', 'items', 'locations', 'product_stock', 'stock_movements',
      'stock_events', 'stock_receipts', 'stock_receipt_items',
      'stock_transfers', 'stock_transfer_items', 'write_offs', 'write_off_items',
      'pos_transactions', 'pos_transaction_items', 'production_orders',
      'production_order_consumptions', 'notification_queue', 'integration_configs')
ORDER BY table_name, ordinal_position;

-- Raw SQL constraints can exist even when EF snapshot lists zero foreign keys.
SELECT n.nspname AS schema_name, t.relname AS table_name,
       con.conname AS constraint_name, con.contype AS constraint_type,
       con.convalidated AS validated,
       pg_get_constraintdef(con.oid, true) AS definition
FROM pg_constraint con
JOIN pg_class t ON t.oid = con.conrelid
JOIN pg_namespace n ON n.oid = t.relnamespace
WHERE n.nspname = 'public'
ORDER BY t.relname, con.conname;

SELECT schemaname, tablename, indexname, indexdef
FROM pg_indexes
WHERE schemaname = 'public'
ORDER BY tablename, indexname;

-- Trigger definitions only; inspect function bodies separately where necessary.
SELECT n.nspname AS schema_name, c.relname AS table_name,
       t.tgname AS trigger_name, t.tgenabled AS enabled,
       pg_get_triggerdef(t.oid, true) AS definition
FROM pg_trigger t
JOIN pg_class c ON c.oid = t.tgrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public' AND NOT t.tgisinternal
ORDER BY c.relname, t.tgname;

COMMIT;
