START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261026120000_AddErpReceiptPosting') THEN
    CREATE FUNCTION public.erp_w4_receipt_insert_check(s public.product_stock) RETURNS void LANGUAGE plpgsql
      SET search_path=pg_catalog,public AS $$
    DECLARE marker uuid;
    BEGIN
      BEGIN marker:=NULLIF(current_setting('app.erp_posting_id',true),'')::uuid;
      EXCEPTION WHEN invalid_text_representation THEN marker:=NULL; END;
      IF marker IS NULL OR s."Quantity"<=0 OR s."Quantity"<>s."QuantityInitial" OR s."SourceType" IS DISTINCT FROM 'receipt'
        OR s."ZoneId" IS NOT NULL OR s."ShelfNumber" IS NOT NULL OR NOT EXISTS (
        SELECT 1 FROM public.document_postings p JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId"
        JOIN public.document_lines l ON l."TenantId"=d."TenantId" AND l."DocumentId"=d."Id"
        JOIN public.document_approvals a ON a."TenantId"=d."TenantId" AND a."DocumentId"=d."Id"
        JOIN public.stock_receipts r ON r."TenantId"=d."TenantId" AND r."Id"=s."SourceId" AND d."SourceId"=r."Id"::text
        JOIN public.stock_receipt_items ri ON ri."ReceiptId"=r."Id" AND l."Metadata"->>'receiptItemId'=ri."Id"::text
        WHERE p."TenantId"=s."TenantId" AND p."Id"=marker AND d."Status"='approved'
          AND d."DocumentType"='stock_receipt' AND d."SourceSystem"='receipt.receive.v1'
          AND r."DestinationLocationId"=s."LocationId" AND r."Status" IN ('draft','ordered','in_transit')
          AND p."PerformedBy"=NULLIF(current_setting('app.user_id',true),'')::uuid AND s."AddedBy"=p."PerformedBy" AND d."CreatedBy"=p."PerformedBy"
          AND a."ApproverId"=p."PerformedBy" AND a."Stage"=1 AND a."Decision"='approved' AND a."ContentHash"=d."ContentHash"
          AND l."Metadata"->>'batchId'=s."Id"::text AND l."Metadata"->>'receiptId'=r."Id"::text
          AND l."ItemId"=s."ProductId" AND ri."ProductId"=s."ProductId" AND l."ToLocationId"=s."LocationId" AND l."FromLocationId" IS NULL
          AND l."Quantity"=s."Quantity" AND coalesce(ri."QuantityReceived",ri."QuantityOrdered")=s."Quantity"
          AND l."UnitCost" IS NOT DISTINCT FROM ri."PricePurchase"
          AND s."BatchNumber" IS NOT DISTINCT FROM l."BatchNumberSnapshot" AND s."BatchNumber" IS NOT DISTINCT FROM ri."BatchNumber"
          AND s."ExpiryDate"=l."ExpiryDateSnapshot" AND s."ExpiryDate"=ri."ExpiryDate"
      ) THEN RAISE EXCEPTION 'Batch creation requires a staged ERP receipt' USING ERRCODE='P4F01'; END IF;
    END $$;

    CREATE FUNCTION public.erp_w4_receipt_complete_check(s public.product_stock) RETURNS void LANGUAGE plpgsql
      SET search_path=pg_catalog,public AS $$
    DECLARE marker uuid;
    BEGIN
      BEGIN marker:=NULLIF(current_setting('app.erp_posting_id',true),'')::uuid;
      EXCEPTION WHEN invalid_text_representation THEN marker:=NULL; END;
      IF marker IS NULL OR NOT EXISTS (
        SELECT 1 FROM public.product_stock current_stock
        JOIN public.inventory_balance_projections b ON b."TenantId"=s."TenantId" AND b."BatchId"=s."Id" AND b."ItemId"=s."ProductId" AND b."LocationId"=s."LocationId"
        JOIN public.inventory_ledger_entries e ON e."TenantId"=b."TenantId" AND e."Id"=b."LastLedgerEntryId" AND e."PostingId"=marker
        JOIN public.document_postings p ON p."TenantId"=e."TenantId" AND p."Id"=marker
        JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId"
        JOIN public.document_lines l ON l."TenantId"=e."TenantId" AND l."Id"=e."DocumentLineId" AND l."DocumentId"=d."Id"
        JOIN public.stock_receipts r ON r."TenantId"=s."TenantId" AND r."Id"=s."SourceId" AND d."SourceId"=r."Id"::text
        JOIN public.stock_receipt_items ri ON ri."ReceiptId"=r."Id" AND l."Metadata"->>'receiptItemId'=ri."Id"::text
        JOIN public.outbox_events o ON o."TenantId"=d."TenantId" AND o."PostingId"=marker AND o."DeliveryScope"='live'
        WHERE current_stock."Id"=s."Id" AND current_stock."TenantId"=s."TenantId" AND to_jsonb(current_stock)=to_jsonb(s)
          AND b."Quantity"=s."Quantity" AND b."ReservedQuantity"=0
          AND s."Quantity"=(SELECT sum(x."QuantityDelta") FROM public.inventory_ledger_entries x WHERE x."TenantId"=s."TenantId" AND x."BatchId"=s."Id" AND x."ItemId"=s."ProductId" AND x."LocationId"=s."LocationId")
          AND e."BatchId"=s."Id" AND e."ItemId"=s."ProductId" AND e."LocationId"=s."LocationId"
          AND e."QuantityBefore"=0 AND e."QuantityAfter"=s."Quantity" AND e."QuantityDelta"=s."Quantity"
          AND e."UnitCost" IS NOT DISTINCT FROM ri."PricePurchase" AND l."Quantity"=s."Quantity"
          AND r."Status"='received' AND r."ReceivedBy"=p."PerformedBy" AND r."ReceivedAt"=p."PostedAt"
          AND d."Status"='posted' AND d."DocumentType"='stock_receipt' AND d."SourceSystem"='receipt.receive.v1'
          AND r."DestinationLocationId"=s."LocationId" AND l."Metadata"->>'batchId'=s."Id"::text
          AND l."Metadata"->>'receiptId'=r."Id"::text AND ri."ProductId"=s."ProductId"
          AND coalesce(ri."QuantityReceived",ri."QuantityOrdered")=s."Quantity"
          AND ri."ExpiryDate"=s."ExpiryDate" AND ri."BatchNumber" IS NOT DISTINCT FROM s."BatchNumber"
          AND (SELECT count(*) FROM public.inventory_ledger_entries x WHERE x."TenantId"=s."TenantId" AND x."PostingId"=marker)
            =(SELECT count(*) FROM public.stock_receipt_items x WHERE x."ReceiptId"=r."Id" AND coalesce(x."QuantityReceived",x."QuantityOrdered")>0)
          AND (SELECT count(*) FROM public.inventory_ledger_entries x WHERE x."TenantId"=s."TenantId" AND x."PostingId"=marker)
            =(SELECT count(*) FROM public.document_lines x WHERE x."TenantId"=s."TenantId" AND x."DocumentId"=d."Id")
          AND (SELECT count(*) FROM public.product_stock x WHERE x."TenantId"=s."TenantId" AND x."SourceType"='receipt' AND x."SourceId"=r."Id")
            =(SELECT count(*) FROM public.document_lines x WHERE x."TenantId"=s."TenantId" AND x."DocumentId"=d."Id")
          AND (SELECT count(DISTINCT x."Metadata"->>'receiptItemId') FROM public.document_lines x WHERE x."TenantId"=s."TenantId" AND x."DocumentId"=d."Id")
            =(SELECT count(*) FROM public.document_lines x WHERE x."TenantId"=s."TenantId" AND x."DocumentId"=d."Id")
          AND EXISTS (SELECT 1 FROM public.stock_movements m WHERE m."TenantId"=s."TenantId" AND m."ProductStockId"=s."Id" AND m."ProductId"=s."ProductId"
            AND m."MovementType"='receipt' AND m."ReferenceId"=r."Id" AND m."ReferenceType"='stock_receipt' AND m."ToLocationId"=s."LocationId"
            AND m."QuantityBefore"=0 AND m."QuantityAfter"=s."Quantity" AND m."Quantity"=s."Quantity" AND m."PerformedBy"=p."PerformedBy"
            AND m."UnitPrice" IS NOT DISTINCT FROM e."UnitCost" AND m."TotalAmount" IS NOT DISTINCT FROM round(e."ValueDelta",2))
          AND EXISTS (SELECT 1 FROM public.notification_queue n WHERE n."TenantId"=s."TenantId" AND n."LocationId"=s."LocationId"
            AND n."EventType"='receipt.created' AND n."Payload"::jsonb->>'receiptId'=r."Id"::text)
      ) THEN RAISE EXCEPTION 'ERP receipt, batches, ledger, projections and notification must commit together' USING ERRCODE='P4F01'; END IF;
    END $$;CREATE OR REPLACE FUNCTION public.erp_w4_stock_before() RETURNS trigger LANGUAGE plpgsql
      SET search_path=pg_catalog,public AS $$
    DECLARE tenant uuid:=TG_ARGV[0]::uuid; location uuid:=TG_ARGV[1]::uuid; posting uuid;
    BEGIN
      IF TG_OP='INSERT' THEN
        IF NEW."TenantId"=tenant AND NEW."LocationId"=location THEN
          PERFORM public.erp_w4_receipt_insert_check(NEW);
        END IF;
        RETURN NEW;
      ELSIF TG_OP='DELETE' THEN
        IF OLD."TenantId"=tenant AND OLD."LocationId"=location THEN
          RAISE EXCEPTION 'Inventory scope requires ERP posting; deletion is not enabled' USING ERRCODE='P4F01';
        END IF;
        RETURN OLD;
      END IF;
      IF NOT (OLD."TenantId"=tenant AND OLD."LocationId"=location OR NEW."TenantId"=tenant AND NEW."LocationId"=location) THEN RETURN NEW; END IF;
      IF ROW(OLD."TenantId",OLD."LocationId",OLD."ProductId",OLD."Id",OLD."QuantityInitial") IS DISTINCT FROM
         ROW(NEW."TenantId",NEW."LocationId",NEW."ProductId",NEW."Id",NEW."QuantityInitial") THEN
        RAISE EXCEPTION 'Inventory scope identity is fenced' USING ERRCODE='P4F01';
      END IF;
      IF NEW."Quantity" IS NOT DISTINCT FROM OLD."Quantity" THEN RETURN NEW; END IF;
      IF (to_jsonb(NEW)-ARRAY['Quantity','Status']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['Quantity','Status']) THEN
        RAISE EXCEPTION 'An ERP count cannot also change batch metadata' USING ERRCODE='P4F01';
      END IF;
      BEGIN posting:=NULLIF(current_setting('app.erp_posting_id',true),'')::uuid;
      EXCEPTION WHEN invalid_text_representation THEN posting:=NULL; END;
      -- A committed posting can never have an approved document (W2 deferred constraint).
      -- Thus an old posting ID, a boolean GUC or a worker role is not a write permit.
      IF posting IS NULL OR NOT EXISTS (
        SELECT 1 FROM public.document_postings p JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId"
        JOIN public.inventory_ledger_entries e ON e."TenantId"=p."TenantId" AND e."PostingId"=p."Id"
        JOIN public.document_approvals a ON a."TenantId"=d."TenantId" AND a."DocumentId"=d."Id"
        WHERE p."TenantId"=tenant AND p."Id"=posting AND d."Status"='approved'
          AND d."DocumentType"='stock_count_adjustment' AND d."SourceSystem"='inventory.adjustment.v1'
          AND d."CreatedBy"=p."PerformedBy" AND p."PerformedBy"=NULLIF(current_setting('app.user_id',true),'')::uuid
          AND a."ApproverId"=p."PerformedBy" AND a."Stage"=1 AND a."Decision"='approved' AND a."ContentHash"=d."ContentHash"
          AND e."BatchId"=OLD."Id" AND e."LocationId"=location AND e."ItemId"=OLD."ProductId"
          AND e."QuantityBefore"=OLD."Quantity" AND e."QuantityAfter"=NEW."Quantity"
          AND e."QuantityDelta"=NEW."Quantity"-OLD."Quantity"
          AND (SELECT count(*) FROM public.inventory_ledger_entries x WHERE x."TenantId"=tenant AND x."PostingId"=posting)=1
      ) THEN RAISE EXCEPTION 'Inventory scope requires a staged ERP count posting' USING ERRCODE='P4F01'; END IF;
      RETURN NEW;
    END $$;

    CREATE OR REPLACE FUNCTION public.erp_w4_stock_complete() RETURNS trigger LANGUAGE plpgsql
      SET search_path=pg_catalog,public AS $$
    DECLARE tenant uuid:=TG_ARGV[0]::uuid; location uuid:=TG_ARGV[1]::uuid; posting uuid;
    BEGIN
      IF TG_OP='INSERT' THEN
        IF NEW."TenantId"=tenant AND NEW."LocationId"=location THEN PERFORM public.erp_w4_receipt_complete_check(NEW); END IF;
        RETURN NULL;
      END IF;

      IF NEW."TenantId"<>tenant OR NEW."LocationId"<>location OR NEW."Quantity" IS NOT DISTINCT FROM OLD."Quantity" THEN RETURN NULL; END IF;
      BEGIN posting:=NULLIF(current_setting('app.erp_posting_id',true),'')::uuid;
      EXCEPTION WHEN invalid_text_representation THEN posting:=NULL; END;
      IF posting IS NULL OR NOT EXISTS (
        SELECT 1 FROM public.product_stock s
        JOIN public.inventory_balance_projections b ON b."TenantId"=s."TenantId" AND b."BatchId"=s."Id" AND b."ItemId"=s."ProductId" AND b."LocationId"=s."LocationId"
        JOIN public.inventory_ledger_entries e ON e."TenantId"=s."TenantId" AND e."Id"=b."LastLedgerEntryId" AND e."PostingId"=posting
        JOIN public.document_postings p ON p."TenantId"=tenant AND p."Id"=posting
        JOIN public.business_documents d ON d."TenantId"=tenant AND d."Id"=p."DocumentId"
        JOIN public.outbox_events o ON o."TenantId"=tenant AND o."PostingId"=posting AND o."DeliveryScope"='live'
        WHERE s."TenantId"=tenant AND s."LocationId"=location AND s."Id"=NEW."Id" AND s."Quantity"=NEW."Quantity"
          AND b."Quantity"=s."Quantity" AND b."ReservedQuantity"=0 AND d."Status"='posted'
          AND e."BatchId"=s."Id" AND e."LocationId"=location AND e."ItemId"=s."ProductId"
          AND e."QuantityBefore"=OLD."Quantity" AND e."QuantityAfter"=NEW."Quantity"
          AND s."Quantity"=(SELECT sum(x."QuantityDelta") FROM public.inventory_ledger_entries x WHERE x."TenantId"=tenant AND x."BatchId"=s."Id" AND x."LocationId"=location AND x."ItemId"=s."ProductId")
          AND EXISTS (SELECT 1 FROM public.stock_movements m WHERE m."TenantId"=tenant AND m."ReferenceId"=d."Id"
            AND m."ProductStockId"=s."Id" AND m."ProductId"=s."ProductId" AND m."MovementType"='adjustment'
            AND m."QuantityBefore"=OLD."Quantity" AND m."QuantityAfter"=NEW."Quantity" AND m."PerformedBy"=p."PerformedBy")
      ) THEN RAISE EXCEPTION 'ERP stock, ledger, projection and audit must commit together' USING ERRCODE='P4F01'; END IF;
      RETURN NULL;
    END $$;

    CREATE OR REPLACE FUNCTION public.erp_w4_scope_ready(tenant uuid, location uuid) RETURNS boolean LANGUAGE sql VOLATILE
      SET search_path=pg_catalog,public AS $$
      SELECT EXISTS (SELECT 1 FROM public.inventory_posting_scopes s
        JOIN pg_trigger b ON b.tgrelid='public.product_stock'::regclass AND b.tgname='erp_w4_b_'||md5(tenant::text||':'||location::text)
        JOIN pg_trigger a ON a.tgrelid=b.tgrelid AND a.tgname='erp_w4_a_'||md5(tenant::text||':'||location::text)
        WHERE s."TenantId"=tenant AND s."LocationId"=location
          AND b.tgenabled='O' AND b.tgtype=31 AND b.tgnargs=2 AND b.tgfoid='public.erp_w4_stock_before()'::regprocedure
          AND a.tgenabled='O' AND a.tgtype=21 AND a.tgnargs=2 AND a.tgdeferrable AND a.tginitdeferred AND a.tgfoid='public.erp_w4_stock_complete()'::regprocedure
          AND b.tgargs=convert_to(tenant::text,'UTF8')||decode('00','hex')||convert_to(location::text,'UTF8')||decode('00','hex')
          AND a.tgargs=b.tgargs)
    $$;
    CREATE OR REPLACE FUNCTION public.erp_w4_activate_scope(tenant uuid, location uuid, activation uuid, actor uuid, fingerprint text, reason text)
      RETURNS uuid LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    DECLARE prior public.inventory_posting_scopes; suffix text:=md5(tenant::text||':'||location::text); actor_role text;
    BEGIN
      IF NOT pg_has_role(current_user,(SELECT relowner FROM pg_class WHERE oid='public.product_stock'::regclass),'USAGE') THEN
        RAISE EXCEPTION 'Inventory fence activation requires the table owner' USING ERRCODE='42501';
      END IF;
      IF current_setting('transaction_isolation')<>'read committed' THEN RAISE EXCEPTION 'Activation requires Read Committed maintenance transaction'; END IF;
      IF tenant IS NULL OR location IS NULL OR activation IS NULL OR actor IS NULL OR tenant='00000000-0000-0000-0000-000000000000'::uuid
        OR location='00000000-0000-0000-0000-000000000000'::uuid OR activation='00000000-0000-0000-0000-000000000000'::uuid OR actor='00000000-0000-0000-0000-000000000000'::uuid
        OR fingerprint IS NULL OR fingerprint !~ '^[0-9a-f]{64}$' OR reason IS NULL OR length(btrim(reason)) NOT BETWEEN 1 AND 500 OR reason ~ '[[:cntrl:]]' THEN
        RAISE EXCEPTION 'Explicit activation scope, identity, fingerprint and reason required' USING ERRCODE='22023';
      END IF;
      LOCK TABLE public.product_stock, public.inventory_ledger_entries, public.inventory_balance_projections,
        public.inventory_reservations, public.inventory_posting_scopes IN SHARE ROW EXCLUSIVE MODE NOWAIT;
      PERFORM set_config('app.tenant_id',tenant::text,true),set_config('app.user_id',actor::text,true),set_config('app.role','',true),set_config('app.consumer_account_id','',true);
      SELECT u."Role" INTO actor_role FROM public.users u WHERE u."Id"=actor AND u."TenantId"=tenant AND u."IsActive"
        AND u."Role" IN ('enterprise_admin','network_manager','store_manager');
      IF actor_role IS NULL THEN RAISE EXCEPTION 'Active manager operator required' USING ERRCODE='42501'; END IF;
      PERFORM set_config('app.role',actor_role,true);
      SELECT * INTO prior FROM public.inventory_posting_scopes s WHERE s."TenantId"=tenant AND s."LocationId"=location;
      IF FOUND THEN
        IF prior."ActivationId"=activation AND prior."OperatorId"=actor AND prior."SourceFingerprint"=fingerprint AND prior."Reason"=btrim(reason)
          AND public.erp_w4_scope_ready(tenant,location) THEN RETURN activation; END IF;
        RAISE EXCEPTION 'Scope already belongs to another activation or has inconsistent triggers' USING ERRCODE='P4F01';
      END IF;
      IF NOT EXISTS (SELECT 1 FROM public.tenants t JOIN public.locations l ON l."TenantId"=t."Id"
          JOIN public.users u ON u."TenantId"=t."Id" WHERE t."Id"=tenant AND l."Id"=location AND u."Id"=actor
            AND t."IsActive" AND l."IsActive" AND u."IsActive" AND t."Modules" @> '["inventory"]'::jsonb
            AND u."Role" IN ('enterprise_admin','network_manager','store_manager')
            AND (u."Role"='enterprise_admin' OR EXISTS (SELECT 1 FROM public.user_locations ul WHERE ul."TenantId"=tenant AND ul."UserId"=actor AND ul."LocationId"=location))) THEN
        RAISE EXCEPTION 'Active authorized inventory scope/operator required' USING ERRCODE='42501';
      END IF;
      IF (SELECT count(*) FROM public.product_stock s WHERE s."TenantId"=tenant AND s."LocationId"=location) NOT BETWEEN 1 AND 10000
        OR EXISTS (SELECT 1 FROM public.inventory_reservations r WHERE r."TenantId"=tenant AND r."LocationId"=location AND r."Status"='active') THEN
        RAISE EXCEPTION 'Activation requires a bounded reconciled scope without active reservations' USING ERRCODE='P4F01';
      END IF;
      IF EXISTS (
        SELECT 1 FROM public.product_stock s LEFT JOIN public.inventory_balance_projections b
          ON b."TenantId"=s."TenantId" AND b."BatchId"=s."Id" AND b."ItemId"=s."ProductId" AND b."LocationId"=location
        LEFT JOIN LATERAL (SELECT count(*) n,sum(e."QuantityDelta") qty FROM public.inventory_ledger_entries e WHERE e."TenantId"=tenant AND e."LocationId"=location AND e."BatchId"=s."Id" AND e."ItemId"=s."ProductId") h ON true
        WHERE s."TenantId"=tenant AND s."LocationId"=location AND (s."Quantity"<>coalesce(h.qty,0)
          OR b."Id" IS NULL AND (s."Quantity"<>0 OR h.n<>0)
          OR b."Id" IS NOT NULL AND (b."Quantity"<>s."Quantity" OR b."ReservedQuantity"<>0 OR b."LastLedgerEntryId" IS NULL AND h.n<>0
            OR b."LastLedgerEntryId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM public.inventory_ledger_entries e WHERE e."TenantId"=tenant AND e."BatchId"=s."Id" AND e."Id"=b."LastLedgerEntryId" AND e."QuantityAfter"=s."Quantity")))) THEN
        RAISE EXCEPTION 'Stock, ledger and projections require reconciliation' USING ERRCODE='P4F01';
      END IF;
      IF public.erp_w4_scope_fingerprint(tenant,location)<>fingerprint THEN RAISE EXCEPTION 'Activation fingerprint changed' USING ERRCODE='P4F01'; END IF;
      INSERT INTO public.inventory_posting_scopes VALUES (tenant,location,activation,actor,fingerprint,btrim(reason),CURRENT_TIMESTAMP);
      -- Constant trigger arguments, rather than an MVCC table lookup, fence transactions whose snapshot predates activation.
      EXECUTE format('CREATE TRIGGER %I BEFORE INSERT OR UPDATE OR DELETE ON public.product_stock FOR EACH ROW EXECUTE FUNCTION public.erp_w4_stock_before(%L,%L)',
        'erp_w4_b_'||suffix,tenant::text,location::text);
      EXECUTE format('CREATE CONSTRAINT TRIGGER %I AFTER INSERT OR UPDATE ON public.product_stock DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.erp_w4_stock_complete(%L,%L)',
        'erp_w4_a_'||suffix,tenant::text,location::text);
      RETURN activation;
    END $$;

    -- Existing activations keep their immutable audit; only their deferred trigger event set expands.
    SET LOCAL app.role='worker';
    DO $upgrade$
    DECLARE s record; suffix text;
    BEGIN
      FOR s IN SELECT "TenantId", "LocationId" FROM public.inventory_posting_scopes LOOP
        suffix:=md5(s."TenantId"::text||':'||s."LocationId"::text);
        EXECUTE format('DROP TRIGGER %I ON public.product_stock','erp_w4_a_'||suffix);
        EXECUTE format('CREATE CONSTRAINT TRIGGER %I AFTER INSERT OR UPDATE ON public.product_stock DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.erp_w4_stock_complete(%L,%L)',
          'erp_w4_a_'||suffix,s."TenantId"::text,s."LocationId"::text);
      END LOOP;
    END $upgrade$;
    CREATE FUNCTION public.erp_w4_receipts_ready(tenant uuid, location uuid) RETURNS boolean LANGUAGE sql VOLATILE
      SET search_path=pg_catalog,public AS $$ SELECT public.erp_w4_scope_ready(tenant,location) $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261026120000_AddErpReceiptPosting') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261026120000_AddErpReceiptPosting', '8.0.11');
    END IF;
END $EF$;
COMMIT;

