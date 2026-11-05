START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261026190000_AddErpWriteOffPosting') THEN
    CREATE FUNCTION public.erp_w4_writeoff_stage(s public.product_stock, before_qty numeric, completed boolean) RETURNS boolean LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    DECLARE marker uuid; phase text;
    BEGIN
      BEGIN marker:=NULLIF(current_setting('app.erp_posting_id',true),'')::uuid; EXCEPTION WHEN invalid_text_representation THEN marker:=NULL; END;
      SELECT d."SourceSystem" INTO phase FROM public.document_postings p JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId" WHERE p."Id"=marker AND p."TenantId"=s."TenantId";
      IF phase IS NULL OR phase NOT IN ('write_off.approve.v1','write_off.reverse.v1') THEN RETURN false; END IF;
      IF NOT EXISTS(
        SELECT 1 FROM public.document_postings p JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId"
        JOIN public.write_offs w ON w."TenantId"=d."TenantId" AND w."Id"::text=d."SourceId"
        JOIN public.document_lines l ON l."TenantId"=d."TenantId" AND l."DocumentId"=d."Id"
        JOIN public.inventory_ledger_entries e ON e."TenantId"=p."TenantId" AND e."PostingId"=p."Id" AND e."DocumentLineId"=l."Id"
        JOIN public.document_approvals a ON a."TenantId"=d."TenantId" AND a."DocumentId"=d."Id" AND a."Stage"=1 AND a."Decision"='approved'
        WHERE p."Id"=marker AND p."TenantId"=s."TenantId" AND d."DocumentType"='stock_write_off' AND d."Status"=CASE WHEN completed THEN 'posted' ELSE 'approved' END
          AND d."CreatedBy"=p."PerformedBy" AND p."PerformedBy"=NULLIF(current_setting('app.user_id',true),'')::uuid AND a."ApproverId"=p."PerformedBy" AND a."ContentHash"=d."ContentHash"
          AND w."LocationId"=s."LocationId" AND l."Metadata"->>'writeOffId'=w."Id"::text AND l."Metadata"->>'batchId'=s."Id"::text
          AND l."ItemId"=s."ProductId" AND l."Quantity">0 AND l."ExpiryDateSnapshot"=s."ExpiryDate" AND l."BatchNumberSnapshot" IS NOT DISTINCT FROM s."BatchNumber"
          AND e."BatchId"=s."Id" AND e."ItemId"=s."ProductId" AND e."LocationId"=s."LocationId" AND e."ZoneId" IS NOT DISTINCT FROM s."ZoneId"
          AND e."UnitCost" IS NOT DISTINCT FROM l."UnitCost" AND e."QuantityBefore"=before_qty AND e."QuantityAfter"=s."Quantity"
          AND e."QuantityDelta"=CASE WHEN phase='write_off.approve.v1' THEN -l."Quantity" ELSE l."Quantity" END
      ) THEN RAISE EXCEPTION 'A matching staged write-off posting is required' USING ERRCODE='P4F01'; END IF;
      RETURN true;
    END $$;

    CREATE FUNCTION public.erp_w4_writeoff_complete(writeoff_id uuid) RETURNS void LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    DECLARE marker uuid; review uuid; w public.write_offs; p public.document_postings; d public.business_documents; phase text; n integer; matched integer;
    BEGIN
      SELECT * INTO w FROM public.write_offs WHERE "Id"=writeoff_id;
      IF NOT EXISTS(SELECT 1 FROM public.business_documents x WHERE x."TenantId"=w."TenantId" AND x."SourceId"=writeoff_id::text AND x."SourceSystem" IN ('write_off.approve.v1','write_off.reverse.v1','write_off.reject.v1')) THEN RETURN; END IF;
      BEGIN marker:=NULLIF(current_setting('app.erp_posting_id',true),'')::uuid; review:=NULLIF(current_setting('app.erp_review_document_id',true),'')::uuid; EXCEPTION WHEN invalid_text_representation THEN marker:=NULL; review:=NULL; END;
      IF review IS NOT NULL THEN
        SELECT * INTO d FROM public.business_documents WHERE "TenantId"=w."TenantId" AND "Id"=review;
        IF d."SourceSystem" IS DISTINCT FROM 'write_off.reject.v1' OR d."SourceId" IS DISTINCT FROM writeoff_id::text OR d."Status" IS DISTINCT FROM 'cancelled'
          OR w."Status"<>'rejected' OR EXISTS(SELECT 1 FROM public.document_postings WHERE "TenantId"=w."TenantId" AND "DocumentId"=review)
          OR EXISTS(SELECT 1 FROM public.document_lines WHERE "TenantId"=w."TenantId" AND "DocumentId"=review)
          OR NOT EXISTS(SELECT 1 FROM public.document_approvals a WHERE a."TenantId"=w."TenantId" AND a."DocumentId"=review AND a."Stage"=1 AND a."Decision"='rejected' AND a."ContentHash"=d."ContentHash" AND a."ApproverId"=d."CreatedBy") THEN
          RAISE EXCEPTION 'Rejected review must retain its immutable decision without inventory posting' USING ERRCODE='P4F01'; END IF;
        RETURN;
      END IF;
      SELECT * INTO p FROM public.document_postings WHERE "Id"=marker AND "TenantId"=w."TenantId";
      SELECT * INTO d FROM public.business_documents WHERE "Id"=p."DocumentId" AND "TenantId"=w."TenantId";
      phase:=d."SourceSystem";
      IF (w."TotalLossAmount" IS NOT NULL AND w."TotalLossAmount"<>(SELECT coalesce(sum("LossAmount"),0) FROM public.write_off_items WHERE "WriteOffId"=writeoff_id)
          OR w."TotalLossAmount" IS NULL AND EXISTS(SELECT 1 FROM public.write_off_items WHERE "WriteOffId"=writeoff_id AND "LossAmount" IS NOT NULL))
        OR (w."TotalLossAmountPurchase" IS NOT NULL AND w."TotalLossAmountPurchase"<>(SELECT coalesce(sum("LossAmountPurchase"),0) FROM public.write_off_items WHERE "WriteOffId"=writeoff_id)
          OR w."TotalLossAmountPurchase" IS NULL AND EXISTS(SELECT 1 FROM public.write_off_items WHERE "WriteOffId"=writeoff_id AND "LossAmountPurchase" IS NOT NULL))
        OR (w."TotalReimbursementAmount" IS NOT NULL AND w."TotalReimbursementAmount"<>(SELECT coalesce(sum("ReimbursementAmount"),0) FROM public.write_off_items WHERE "WriteOffId"=writeoff_id)
          OR w."TotalReimbursementAmount" IS NULL AND EXISTS(SELECT 1 FROM public.write_off_items WHERE "WriteOffId"=writeoff_id AND "ReimbursementAmount" IS NOT NULL)) THEN
        RAISE EXCEPTION 'Write-off monetary summary differs from stored source lines' USING ERRCODE='P4F01'; END IF;
      IF phase IS NULL OR phase NOT IN ('write_off.approve.v1','write_off.reverse.v1') OR d."SourceId"<>writeoff_id::text OR d."Status"<>'posted'
        OR w."Status"<>(CASE WHEN phase='write_off.approve.v1' THEN 'approved' ELSE 'reversed' END)
        OR phase='write_off.approve.v1' AND (w."ApprovedBy" IS DISTINCT FROM p."PerformedBy" OR w."ApprovedAt" IS DISTINCT FROM p."PostedAt")
        OR NOT EXISTS(SELECT 1 FROM public.outbox_events o WHERE o."TenantId"=w."TenantId" AND o."PostingId"=marker AND o."DeliveryScope"='live') THEN
        RAISE EXCEPTION 'Write-off header, posting and live event must commit together' USING ERRCODE='P4F01'; END IF;
      SELECT count(*) INTO n FROM public.document_lines WHERE "TenantId"=w."TenantId" AND "DocumentId"=d."Id";
      IF n NOT BETWEEN 1 AND 100 OR n<>(SELECT count(*) FROM public.inventory_ledger_entries WHERE "TenantId"=w."TenantId" AND "PostingId"=marker)
        OR n<>(SELECT count(DISTINCT l."Metadata"->>'batchId') FROM public.document_lines l WHERE l."TenantId"=w."TenantId" AND l."DocumentId"=d."Id") THEN
        RAISE EXCEPTION 'One ledger delta and line per affected batch required' USING ERRCODE='P4F01'; END IF;
      SELECT count(*) INTO matched FROM public.document_lines l
        JOIN public.inventory_ledger_entries e ON e."TenantId"=w."TenantId" AND e."PostingId"=marker AND e."DocumentLineId"=l."Id"
        JOIN public.product_stock s ON s."TenantId"=w."TenantId" AND s."Id"::text=l."Metadata"->>'batchId'
        JOIN public.inventory_balance_projections b ON b."TenantId"=s."TenantId" AND b."BatchId"=s."Id" AND b."ItemId"=s."ProductId" AND b."LocationId"=s."LocationId"
        WHERE l."TenantId"=w."TenantId" AND l."DocumentId"=d."Id" AND l."Metadata"->>'writeOffId'=writeoff_id::text
          AND s."LocationId"=w."LocationId" AND s."ProductId"=l."ItemId" AND s."BatchNumber" IS NOT DISTINCT FROM l."BatchNumberSnapshot" AND s."ExpiryDate"=l."ExpiryDateSnapshot"
          AND e."ItemId"=s."ProductId" AND e."BatchId"=s."Id" AND e."LocationId"=s."LocationId" AND e."ZoneId" IS NOT DISTINCT FROM s."ZoneId"
          AND e."UnitCost" IS NOT DISTINCT FROM l."UnitCost" AND e."QuantityAfter"=s."Quantity" AND e."QuantityDelta"=CASE WHEN phase='write_off.approve.v1' THEN -l."Quantity" ELSE l."Quantity" END
          AND b."Quantity"=s."Quantity" AND b."ReservedQuantity"=0 AND b."LastLedgerEntryId"=e."Id"
          AND s."Quantity"=(SELECT sum(x."QuantityDelta") FROM public.inventory_ledger_entries x WHERE x."TenantId"=w."TenantId" AND x."BatchId"=s."Id" AND x."ItemId"=s."ProductId" AND x."LocationId"=s."LocationId")
          AND jsonb_typeof(l."Metadata"->'allocations')='array' AND jsonb_array_length(l."Metadata"->'allocations')>0
          AND l."Quantity"=(SELECT sum(a."Quantity") FROM jsonb_to_recordset(l."Metadata"->'allocations') AS a("SourceItemId" uuid,"Quantity" numeric,"RetailLoss" numeric))
          AND NOT EXISTS(SELECT 1 FROM jsonb_to_recordset(l."Metadata"->'allocations') AS a("SourceItemId" uuid,"Quantity" numeric,"RetailLoss" numeric)
            LEFT JOIN public.write_off_items wi ON wi."WriteOffId"=writeoff_id AND wi."Id"=a."SourceItemId"
            WHERE wi."Id" IS NULL OR wi."ProductId"<>s."ProductId" OR wi."IsReturnedToSupplier" OR a."Quantity"<=0 OR a."Quantity" IS NULL
              OR wi."ProductStockId" IS NOT NULL AND wi."ProductStockId"<>s."Id" OR a."RetailLoss"<0)
          AND (l."Metadata"->>'retailLoss')::numeric IS NOT DISTINCT FROM
            (SELECT CASE WHEN count(*)=count(a."RetailLoss") THEN sum(a."RetailLoss") ELSE NULL END FROM jsonb_to_recordset(l."Metadata"->'allocations') AS a("SourceItemId" uuid,"Quantity" numeric,"RetailLoss" numeric))
          AND EXISTS(SELECT 1 FROM public.stock_movements m WHERE m."TenantId"=w."TenantId" AND m."ProductStockId"=s."Id" AND m."ProductId"=s."ProductId" AND m."ReferenceId"=writeoff_id
            AND m."MovementType"=CASE WHEN phase='write_off.approve.v1' THEN 'write_off' ELSE 'adjustment' END
            AND m."ReferenceType"=CASE WHEN phase='write_off.approve.v1' THEN 'write_off' ELSE 'write_off_reversal' END
            AND m."Quantity"=l."Quantity" AND m."QuantityBefore"=e."QuantityBefore" AND m."QuantityAfter"=e."QuantityAfter" AND m."PerformedBy"=p."PerformedBy" AND m."UnitPrice" IS NULL
            AND m."TotalAmount" IS NOT DISTINCT FROM (l."Metadata"->>'retailLoss')::numeric
            AND (phase='write_off.approve.v1' AND m."FromLocationId"=w."LocationId" AND m."FromZoneId" IS NOT DISTINCT FROM s."ZoneId" AND m."ToLocationId" IS NULL
              OR phase='write_off.reverse.v1' AND m."ToLocationId"=w."LocationId" AND m."ToZoneId" IS NOT DISTINCT FROM s."ZoneId" AND m."FromLocationId" IS NULL))
          AND (phase='write_off.approve.v1' AND l."FromLocationId"=w."LocationId" AND l."ToLocationId" IS NULL OR phase='write_off.reverse.v1' AND l."ToLocationId"=w."LocationId" AND l."FromLocationId" IS NULL AND EXISTS(
            SELECT 1 FROM public.business_documents od JOIN public.document_lines ol ON ol."TenantId"=od."TenantId" AND ol."DocumentId"=od."Id"
            JOIN public.inventory_ledger_entries oe ON oe."TenantId"=od."TenantId" AND oe."DocumentLineId"=ol."Id"
            JOIN public.document_links link ON link."TenantId"=od."TenantId" AND link."FromDocumentId"=od."Id" AND link."ToDocumentId"=d."Id" AND link."RelationType"='reverses'
            WHERE od."TenantId"=w."TenantId" AND od."SourceId"=writeoff_id::text AND od."SourceSystem"='write_off.approve.v1' AND od."Status"='reversed'
              AND d."ReversesDocumentId"=od."Id" AND e."ReversesEntryId"=oe."Id" AND oe."Id"::text=l."Metadata"->>'originalEntryId'
              AND ol."Metadata"->'allocations'=l."Metadata"->'allocations' AND ol."Unit"=l."Unit" AND ol."ItemNameSnapshot"=l."ItemNameSnapshot" AND ol."BarcodeSnapshot" IS NOT DISTINCT FROM l."BarcodeSnapshot"));
      IF matched<>n OR NOT EXISTS(SELECT 1 FROM public.write_off_items WHERE "WriteOffId"=writeoff_id) OR EXISTS(
        SELECT 1 FROM public.write_off_items wi LEFT JOIN LATERAL (
          SELECT sum(a."Quantity") qty,sum(a."RetailLoss") retail,count(*) total,count(a."RetailLoss") known
          FROM public.document_lines l CROSS JOIN LATERAL jsonb_to_recordset(l."Metadata"->'allocations') AS a("SourceItemId" uuid,"Quantity" numeric,"RetailLoss" numeric)
          WHERE l."TenantId"=w."TenantId" AND l."DocumentId"=d."Id" AND a."SourceItemId"=wi."Id"
        ) used ON true WHERE wi."WriteOffId"=writeoff_id AND (wi."Quantity"<=0 OR wi."Quantity" IS DISTINCT FROM used.qty OR wi."IsReturnedToSupplier"
          OR wi."LossAmount" IS DISTINCT FROM used.retail OR wi."LossAmount" IS NULL AND used.known<>0 OR wi."LossAmount" IS NOT NULL AND used.known<>used.total)
      ) THEN RAISE EXCEPTION 'Write-off source allocations, stock, ledger and legacy audit disagree' USING ERRCODE='P4F01'; END IF;
    END $$;CREATE OR REPLACE FUNCTION public.erp_w4_stock_before() RETURNS trigger LANGUAGE plpgsql
      SET search_path=pg_catalog,public AS $$
    DECLARE tenant uuid:=TG_ARGV[0]::uuid; location uuid:=TG_ARGV[1]::uuid; posting uuid;
    BEGIN
      IF TG_OP='INSERT' THEN
        IF NEW."TenantId"=tenant AND NEW."LocationId"=location THEN
          IF NEW."SourceType"='transfer' THEN IF NOT public.erp_w4_transfer_stage(NEW,0,true,false) THEN RAISE EXCEPTION 'Transfer posting required' USING ERRCODE='P4F01'; END IF; ELSE PERFORM public.erp_w4_receipt_insert_check(NEW); END IF;
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
      IF public.erp_w4_writeoff_stage(NEW,OLD."Quantity",false) THEN RETURN NEW; END IF;
              IF public.erp_w4_transfer_stage(NEW,OLD."Quantity",false,false) THEN RETURN NEW; END IF;
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
        IF NEW."TenantId"=tenant AND NEW."LocationId"=location THEN IF NEW."SourceType"='transfer' THEN IF NOT EXISTS(SELECT 1 FROM public.product_stock s WHERE s."Id"=NEW."Id" AND to_jsonb(s)=to_jsonb(NEW)) THEN RAISE EXCEPTION 'Transfer batch changed before commit' USING ERRCODE='P4F01'; END IF; PERFORM public.erp_w4_transfer_complete(NEW."SourceId"); ELSE PERFORM public.erp_w4_receipt_complete_check(NEW); END IF; END IF;
        RETURN NULL;
      END IF;

      IF NEW."TenantId"<>tenant OR NEW."LocationId"<>location OR NEW."Quantity" IS NOT DISTINCT FROM OLD."Quantity" THEN RETURN NULL; END IF;
      BEGIN posting:=NULLIF(current_setting('app.erp_posting_id',true),'')::uuid;
      EXCEPTION WHEN invalid_text_representation THEN posting:=NULL; END;
      IF public.erp_w4_writeoff_stage(NEW,OLD."Quantity",true) THEN
                IF NOT EXISTS(SELECT 1 FROM public.product_stock s WHERE s."Id"=NEW."Id" AND to_jsonb(s)=to_jsonb(NEW)) THEN RAISE EXCEPTION 'Write-off batch changed before commit' USING ERRCODE='P4F01'; END IF;
                PERFORM public.erp_w4_writeoff_complete((SELECT d."SourceId"::uuid FROM public.document_postings p JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId" WHERE p."Id"=posting)); RETURN NULL; END IF;
              IF public.erp_w4_transfer_stage(NEW,OLD."Quantity",false,true) THEN
                IF NOT EXISTS(SELECT 1 FROM public.product_stock s WHERE s."Id"=NEW."Id" AND to_jsonb(s)=to_jsonb(NEW)) THEN RAISE EXCEPTION 'Transfer batch changed before commit' USING ERRCODE='P4F01'; END IF;
                PERFORM public.erp_w4_transfer_complete((SELECT d."SourceId"::uuid FROM public.document_postings p JOIN public.business_documents d ON d."Id"=p."DocumentId" AND d."TenantId"=p."TenantId" WHERE p."Id"=posting)); RETURN NULL; END IF;
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

    CREATE FUNCTION public.erp_w4_writeoff_header_before() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    DECLARE guarded boolean; d public.business_documents; actor uuid; decision text;
    BEGIN
      SELECT EXISTS(SELECT 1 FROM public.business_documents x WHERE x."TenantId"=OLD."TenantId" AND x."SourceId"=OLD."Id"::text AND x."SourceSystem" IN ('write_off.approve.v1','write_off.reverse.v1','write_off.reject.v1')) INTO guarded;
      IF TG_OP='DELETE' THEN IF guarded THEN RAISE EXCEPTION 'ERP write-off history is immutable' USING ERRCODE='P4F01'; END IF; RETURN OLD; END IF;
      IF NOT guarded AND NOT (NEW."Status" IN ('approved','reversed') AND EXISTS(SELECT 1 FROM public.inventory_posting_scopes f WHERE f."TenantId"=NEW."TenantId" AND f."LocationId"=NEW."LocationId")) THEN RETURN NEW; END IF;
      IF to_jsonb(NEW)=to_jsonb(OLD) THEN RETURN NEW; END IF;
      IF NEW."Status"='rejected' THEN
        SELECT * INTO d FROM public.business_documents x WHERE x."TenantId"=OLD."TenantId" AND x."Id"::text=current_setting('app.erp_review_document_id',true) AND x."Status"='submitted' AND x."SourceSystem"='write_off.reject.v1';
        actor:=d."CreatedBy"; decision:='rejected';
      ELSE
        SELECT x.* INTO d FROM public.business_documents x JOIN public.document_postings p ON p."TenantId"=x."TenantId" AND p."DocumentId"=x."Id"
          WHERE p."TenantId"=OLD."TenantId" AND p."Id"::text=current_setting('app.erp_posting_id',true) AND x."Status"='approved';
        actor:=d."CreatedBy"; decision:='approved';
      END IF;
      IF d."Id" IS NULL OR d."SourceId"<>OLD."Id"::text OR actor IS DISTINCT FROM NULLIF(current_setting('app.user_id',true),'')::uuid
        OR (to_jsonb(NEW)-ARRAY['Status','ApprovedBy','ApprovedAt']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['Status','ApprovedBy','ApprovedAt'])
        OR NOT EXISTS(SELECT 1 FROM public.document_approvals a WHERE a."TenantId"=OLD."TenantId" AND a."DocumentId"=d."Id" AND a."Stage"=1 AND a."Decision"=decision AND a."ApproverId"=actor AND a."ContentHash"=d."ContentHash")
        OR NOT (OLD."Status"='pending_approval' AND NEW."Status"='approved' AND d."SourceSystem"='write_off.approve.v1' AND NEW."ApprovedBy"=actor
            AND NEW."ApprovedAt"=(SELECT p."PostedAt" FROM public.document_postings p WHERE p."TenantId"=OLD."TenantId" AND p."DocumentId"=d."Id")
          OR OLD."Status"='pending_approval' AND NEW."Status"='rejected' AND d."SourceSystem"='write_off.reject.v1' AND ROW(NEW."ApprovedBy",NEW."ApprovedAt") IS NOT DISTINCT FROM ROW(OLD."ApprovedBy",OLD."ApprovedAt")
          OR OLD."Status"='approved' AND NEW."Status"='reversed' AND d."SourceSystem"='write_off.reverse.v1' AND ROW(NEW."ApprovedBy",NEW."ApprovedAt") IS NOT DISTINCT FROM ROW(OLD."ApprovedBy",OLD."ApprovedAt")) THEN
        RAISE EXCEPTION 'ERP write-off transition requires its staged decision' USING ERRCODE='P4F01'; END IF;
      RETURN NEW;
    END $$;
    CREATE FUNCTION public.erp_w4_writeoff_items_before() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    BEGIN
      -- The parent row is changed by posting. A pre-posting RR snapshot must fail serialization here,
      -- rather than miss the newly committed ERP document and rewrite an older source line.
      IF TG_OP<>'INSERT' THEN PERFORM 1 FROM public.write_offs WHERE "Id"=OLD."WriteOffId" FOR UPDATE; END IF;
      IF TG_OP<>'DELETE' THEN PERFORM 1 FROM public.write_offs WHERE "Id"=NEW."WriteOffId" FOR UPDATE; END IF;
      IF EXISTS(SELECT 1 FROM public.write_offs w JOIN public.business_documents d ON d."TenantId"=w."TenantId" AND d."SourceId"=w."Id"::text
        WHERE d."SourceSystem" IN ('write_off.approve.v1','write_off.reverse.v1','write_off.reject.v1') AND
          (w."Id"=CASE WHEN TG_OP='INSERT' THEN NEW."WriteOffId" ELSE OLD."WriteOffId" END OR TG_OP='UPDATE' AND w."Id"=NEW."WriteOffId")) THEN
        RAISE EXCEPTION 'ERP write-off source lines are immutable' USING ERRCODE='P4F01'; END IF;
      IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
    END $$;
    CREATE FUNCTION public.erp_w4_writeoff_header_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    BEGIN PERFORM public.erp_w4_writeoff_complete(NEW."Id"); RETURN NULL; END $$;
    CREATE TRIGGER erp_w4_writeoff_header_before BEFORE UPDATE OR DELETE ON public.write_offs FOR EACH ROW EXECUTE FUNCTION public.erp_w4_writeoff_header_before();
    CREATE TRIGGER erp_w4_writeoff_items_before BEFORE INSERT OR UPDATE OR DELETE ON public.write_off_items FOR EACH ROW EXECUTE FUNCTION public.erp_w4_writeoff_items_before();
    CREATE CONSTRAINT TRIGGER erp_w4_writeoff_header_complete AFTER INSERT OR UPDATE ON public.write_offs DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.erp_w4_writeoff_header_complete();
    CREATE TRIGGER erp_w4_writeoff_truncate BEFORE TRUNCATE ON public.write_offs FOR EACH STATEMENT EXECUTE FUNCTION public.erp_w4_transfer_truncate();
    CREATE TRIGGER erp_w4_writeoff_items_truncate BEFORE TRUNCATE ON public.write_off_items FOR EACH STATEMENT EXECUTE FUNCTION public.erp_w4_transfer_truncate();
    CREATE FUNCTION public.erp_w4_writeoffs_ready(tenant uuid, location uuid) RETURNS boolean LANGUAGE sql VOLATILE SET search_path=pg_catalog,public AS $$
      SELECT public.erp_w4_transfers_ready(tenant,location) AND (SELECT count(*)=5 FROM pg_trigger WHERE NOT tgisinternal AND tgenabled='O' AND
        (tgrelid='public.write_offs'::regclass AND (
          tgname='erp_w4_writeoff_header_before' AND tgtype=27 AND tgfoid='public.erp_w4_writeoff_header_before()'::regprocedure
          OR tgname='erp_w4_writeoff_header_complete' AND tgtype=21 AND tgdeferrable AND tginitdeferred AND tgfoid='public.erp_w4_writeoff_header_complete()'::regprocedure
          OR tgname='erp_w4_writeoff_truncate' AND tgtype=34 AND tgfoid='public.erp_w4_transfer_truncate()'::regprocedure)
         OR tgrelid='public.write_off_items'::regclass AND (
          tgname='erp_w4_writeoff_items_before' AND tgtype=31 AND tgfoid='public.erp_w4_writeoff_items_before()'::regprocedure
          OR tgname='erp_w4_writeoff_items_truncate' AND tgtype=34 AND tgfoid='public.erp_w4_transfer_truncate()'::regprocedure)))
    $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261026190000_AddErpWriteOffPosting') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261026190000_AddErpWriteOffPosting', '8.0.11');
    END IF;
END $EF$;
COMMIT;

