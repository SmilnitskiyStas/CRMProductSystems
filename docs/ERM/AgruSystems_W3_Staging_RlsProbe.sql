-- Explicit psql variables: -v tenant=<uuid> -v operator=<assigned-user-uuid>.
-- The caller must be allowed to SET ROLE; this transaction cannot change application rows.
BEGIN READ ONLY;
SET LOCAL ROLE shelfguard_staging_app;
SELECT set_config('app.role','',true),set_config('app.tenant_id',:'tenant',true),set_config('app.user_id',:'operator',true),set_config('app.consumer_account_id','',true);
SELECT jsonb_build_object('probe','assigned_tenant','database',current_database(),'runtimeRole',current_user,
  'nonBypass',(SELECT NOT rolsuper AND NOT rolbypassrls FROM pg_roles WHERE rolname=current_user),
  'sourceRlsActive',row_security_active('public.product_stock'::regclass),
  'erpRlsActive',row_security_active('public.inventory_ledger_entries'::regclass),
  'batches',(SELECT count(*) FROM public.product_stock),
  'documents',(SELECT count(*) FROM public.business_documents),
  'ledgerEntries',(SELECT count(*) FROM public.inventory_ledger_entries),
  'outboxEvents',(SELECT count(*) FROM public.outbox_events));
SELECT set_config('app.tenant_id','99999999-9999-4999-8999-999999999999',true);
SELECT jsonb_build_object('probe','foreign_tenant',
  'batches',(SELECT count(*) FROM public.product_stock),
  'documents',(SELECT count(*) FROM public.business_documents),
  'ledgerEntries',(SELECT count(*) FROM public.inventory_ledger_entries),
  'outboxEvents',(SELECT count(*) FROM public.outbox_events));
SELECT set_config('app.tenant_id','',true),set_config('app.role','provider',true);
SELECT jsonb_build_object('probe','provider_without_tenant_has_no_erp_bypass',
  'documents',(SELECT count(*) FROM public.business_documents),
  'ledgerEntries',(SELECT count(*) FROM public.inventory_ledger_entries),
  'outboxEvents',(SELECT count(*) FROM public.outbox_events));
ROLLBACK;
