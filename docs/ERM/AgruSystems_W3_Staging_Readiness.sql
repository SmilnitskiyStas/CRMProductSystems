-- Read-only acceptance of the complete local staging inventory, including empty zero balances.
-- Run with psql -X -v ON_ERROR_STOP=1 -At -f <this file>; keep detailed JSON in private evidence.
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL search_path=pg_catalog,public;

DO $acceptance$
DECLARE names text[] := ARRAY['business_documents','document_lines','document_postings','document_links','document_approvals',
  'inventory_ledger_entries','inventory_balance_projections','inventory_reservations','inventory_reconciliations','outbox_events','inbox_deduplication'];
BEGIN
  IF current_database() NOT IN ('shelfguard_staging','erm_stage_rehearsal') THEN
    RAISE EXCEPTION 'Explicit staging/rehearsal database required';
  END IF;
  IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId"='20261024220000_AddErpParallelSchema') THEN
    RAISE EXCEPTION 'W2 migration is missing';
  END IF;
  IF (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
      WHERE n.nspname='public' AND c.relname=ANY(names) AND c.relrowsecurity AND c.relforcerowsecurity)<>11 THEN
    RAISE EXCEPTION 'ERP tables must all enable and force RLS';
  END IF;
  IF EXISTS(SELECT 1 FROM unnest(names) t(name) WHERE
      (SELECT count(*) FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=t.name)<>2 OR
      NOT EXISTS(SELECT 1 FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=t.name
        AND p.policyname='tenant_isolation' AND p.qual ILIKE '%NULLIF%') OR
      NOT EXISTS(SELECT 1 FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=t.name AND p.policyname='worker_bypass')) THEN
    RAISE EXCEPTION 'ERP tenant/worker policy profile differs from W2';
  END IF;
  IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='shelfguard_staging_app' AND NOT rolsuper AND NOT rolbypassrls) THEN
    RAISE EXCEPTION 'Staging application role must not bypass RLS';
  END IF;
  IF EXISTS(SELECT 1 FROM unnest(names) t(name) WHERE NOT has_table_privilege('shelfguard_staging_app','public.'||t.name,'SELECT')
      OR NOT has_table_privilege('shelfguard_staging_app','public.'||t.name,'INSERT')
      OR NOT has_table_privilege('shelfguard_staging_app','public.'||t.name,'UPDATE')) THEN
    RAISE EXCEPTION 'Staging ERP maintenance grants are incomplete';
  END IF;
  IF EXISTS(SELECT 1 FROM unnest(ARRAY['cash_registers','register_sessions','promotion_rules','promotion_rule_versions','promotion_rule_applications',
      'shelf_items','shelf_scans','item_images','promo_no_sales_alerts','iot_message_receipts']) t(name)
    WHERE NOT has_table_privilege('shelfguard_staging_app','public.'||t.name,'SELECT')
      OR NOT has_table_privilege('shelfguard_staging_app','public.'||t.name,'INSERT')
      OR NOT has_table_privilege('shelfguard_staging_app','public.'||t.name,'UPDATE')
      OR NOT has_table_privilege('shelfguard_staging_app','public.'||t.name,'DELETE')) THEN
    RAISE EXCEPTION 'Runtime grants for additive legacy tables are incomplete';
  END IF;
  IF (SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public'
      AND p.proname IN ('erp_v1_immutable','erp_v1_document_guard','erp_v1_line_guard','erp_v1_posting_guard',
        'erp_v1_ledger_guard','erp_v1_outbox_guard','erp_v1_check_posting'))<>7 THEN
    RAISE EXCEPTION 'W2 guard functions are missing';
  END IF;
END $acceptance$;

WITH ledger AS (
  SELECT "TenantId","BatchId",count(*) AS entries,sum("QuantityDelta") AS quantity
  FROM public.inventory_ledger_entries GROUP BY "TenantId","BatchId"
), observations AS (
  SELECT s."Id" AS batch,s."TenantId" AS tenant,s."LocationId" AS location,s."ProductId" AS item,s."Quantity" AS legacy,
    l.quantity AS ledger,coalesce(l.entries,0) AS entries,p."Quantity" AS projected,p."ReservedQuantity" AS reserved,
    (s."Quantity"=0 AND l.entries IS NULL AND p."Id" IS NULL) AS zero_no_posting,
    (s."Quantity">=0 AND s."QuantityInitial">=0 AND i."TenantId"=s."TenantId" AND loc."TenantId"=s."TenantId"
      AND (s."ZoneId" IS NULL OR z."LocationId"=s."LocationId")) AS source_valid,
    (s."Quantity"=coalesce(l.quantity,0) AND (
       s."Quantity"=0 AND l.entries IS NULL AND p."Id" IS NULL OR
       l.entries IS NOT NULL AND p."Quantity"=l.quantity AND p."ReservedQuantity"=0
       AND p."LastLedgerEntryId" IS NOT NULL)) AS quantity_matches
  FROM public.product_stock s LEFT JOIN ledger l ON l."TenantId"=s."TenantId" AND l."BatchId"=s."Id"
    LEFT JOIN public.inventory_balance_projections p ON p."TenantId"=s."TenantId" AND p."BatchId"=s."Id"
    LEFT JOIN public.items i ON i."Id"=s."ProductId" LEFT JOIN public.locations loc ON loc."Id"=s."LocationId"
    LEFT JOIN public.location_zones z ON z."Id"=s."ZoneId"
), checks AS (
  SELECT count(*) AS batches,count(*) FILTER(WHERE source_valid AND quantity_matches) AS matched,
    count(*) FILTER(WHERE zero_no_posting AND source_valid) AS zero_no_posting,
    count(*) FILTER(WHERE NOT coalesce(source_valid AND quantity_matches,false)) AS blocked FROM observations
)
SELECT jsonb_build_object(
  'schemaVersion',1,'database',current_database(),'capturedAt',CURRENT_TIMESTAMP,
  'batches',c.batches,'matchedIncludingExplicitEmptyZero',c.matched,'zeroNoPosting',c.zero_no_posting,'blocked',c.blocked,
  'activeReservations',(SELECT count(*) FROM public.inventory_reservations WHERE "Status"='active'),
  'historicalEvents',(SELECT count(*) FROM public.outbox_events WHERE "DeliveryScope"='historical' AND "Status"='suppressed'),
  'liveBackfillEvents',(SELECT count(*) FROM public.outbox_events o JOIN public.document_postings p ON p."Id"=o."PostingId" AND p."TenantId"=o."TenantId"
    JOIN public.business_documents d ON d."Id"=p."DocumentId" AND d."TenantId"=p."TenantId"
    WHERE d."SourceSystem"='legacy.control_balance' AND (o."DeliveryScope"<>'historical' OR o."Status"<>'suppressed')),
  'guardDefinitionDigest',(SELECT md5(string_agg(p.proname||pg_get_functiondef(p.oid),'' ORDER BY p.proname))
    FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' AND p.proname LIKE 'erp_v1_%'),
  'sourceQuantityDigest',(SELECT md5(string_agg(jsonb_build_array(s."Id",s."TenantId",s."ProductId",s."LocationId",s."ZoneId",s."Quantity",s."QuantityInitial",s."ExpiryDate",s."BatchNumber")::text,'' ORDER BY s."Id")) FROM public.product_stock s),
  'observations',(SELECT jsonb_agg(jsonb_build_object('tenantId',tenant,'locationId',location,'itemId',item,'batchId',batch,
    'legacyQuantity',legacy::text,'ledgerQuantity',ledger::text,'entryCount',entries,'projectionQuantity',projected::text,
    'decision',CASE WHEN zero_no_posting AND source_valid THEN 'verified_empty_zero_no_posting'
      WHEN source_valid AND quantity_matches THEN 'matched_control_quantity' ELSE 'blocked' END) ORDER BY tenant,location,batch) FROM observations)
) FROM checks c;
COMMIT;
