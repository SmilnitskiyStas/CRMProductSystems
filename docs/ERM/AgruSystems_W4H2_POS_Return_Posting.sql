START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027114758_AddErpPosReturnPosting') THEN
    CREATE TABLE pos_return_fiscalizations (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "SaleId" uuid NOT NULL,
        "ShiftId" uuid NOT NULL,
        "ClientRequestId" uuid NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        "Reason" character varying(500) NOT NULL,
        "PaymentType" character varying(20) NOT NULL,
        "Amount" numeric(12,2) NOT NULL,
        "TaxAmount" numeric(12,2) NOT NULL,
        "RequestJson" jsonb NOT NULL,
        "Status" character varying(20) NOT NULL,
        "Attempts" integer NOT NULL,
        "FiscalNumber" character varying(128),
        "LastError" character varying(80),
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_pos_return_fiscalizations" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_pos_return_amount" CHECK ("Amount">=0 AND "TaxAmount">=0 AND "TaxAmount"<="Amount" AND "Attempts">=0),
        CONSTRAINT "CK_pos_return_status" CHECK ("Status" IN ('pending','done','failed') AND ("Status"<>'done' OR "FiscalNumber" IS NOT NULL AND length(btrim("FiscalNumber"))>0)),
        CONSTRAINT "FK_pos_return_fiscalizations_business_documents_TenantId_Id" FOREIGN KEY ("TenantId", "Id") REFERENCES business_documents ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_pos_return_fiscalizations_pos_shifts_ShiftId" FOREIGN KEY ("ShiftId") REFERENCES pos_shifts ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_pos_return_fiscalizations_pos_transactions_SaleId" FOREIGN KEY ("SaleId") REFERENCES pos_transactions ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027114758_AddErpPosReturnPosting') THEN
    CREATE INDEX "IX_pos_return_fiscalizations_SaleId" ON pos_return_fiscalizations ("SaleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027114758_AddErpPosReturnPosting') THEN
    CREATE INDEX "IX_pos_return_fiscalizations_ShiftId" ON pos_return_fiscalizations ("ShiftId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027114758_AddErpPosReturnPosting') THEN
    CREATE UNIQUE INDEX "IX_pos_return_fiscalizations_TenantId_ClientRequestId" ON pos_return_fiscalizations ("TenantId", "ClientRequestId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027114758_AddErpPosReturnPosting') THEN
    CREATE INDEX "IX_pos_return_fiscalizations_TenantId_Id" ON pos_return_fiscalizations ("TenantId", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027114758_AddErpPosReturnPosting') THEN
    CREATE UNIQUE INDEX "IX_pos_return_fiscalizations_TenantId_SaleId" ON pos_return_fiscalizations ("TenantId", "SaleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027114758_AddErpPosReturnPosting') THEN
    ALTER TABLE public.pos_return_fiscalizations ENABLE ROW LEVEL SECURITY;
    ALTER TABLE public.pos_return_fiscalizations FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON public.pos_return_fiscalizations USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid) WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
    CREATE POLICY worker_bypass ON public.pos_return_fiscalizations USING (current_setting('app.role',true)='worker') WITH CHECK (current_setting('app.role',true)='worker');
    CREATE FUNCTION public.erp_w4_pos_return_stage(s public.product_stock,before_qty numeric,completed boolean) RETURNS boolean LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    DECLARE marker uuid; phase text;
    BEGIN
      BEGIN marker:=NULLIF(current_setting('app.erp_posting_id',true),'')::uuid; EXCEPTION WHEN invalid_text_representation THEN marker:=NULL; END;
      SELECT d."SourceSystem" INTO phase FROM public.document_postings p JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId" WHERE p."TenantId"=s."TenantId" AND p."Id"=marker;
      IF phase IS DISTINCT FROM 'pos.return.v1' THEN RETURN false; END IF;
      IF NOT EXISTS(SELECT 1 FROM public.inventory_ledger_entries e JOIN public.document_postings p ON p."TenantId"=e."TenantId" AND p."Id"=e."PostingId"
        JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId"
        JOIN public.document_approvals a ON a."TenantId"=d."TenantId" AND a."DocumentId"=d."Id" AND a."Stage"=1 AND a."Decision"='approved' AND a."ContentHash"=d."ContentHash"
        JOIN public.document_lines l ON l."TenantId"=e."TenantId" AND l."Id"=e."DocumentLineId"
        JOIN public.items item ON item."TenantId"=e."TenantId" AND item."Id"=e."ItemId" AND item."Unit"=l."Unit"
        JOIN public.inventory_ledger_entries o ON o."TenantId"=e."TenantId" AND o."Id"=e."ReversesEntryId" AND o."PostingId"=p."ReversesPostingId"
        JOIN public.document_lines ol ON ol."TenantId"=o."TenantId" AND ol."Id"=o."DocumentLineId"
        JOIN public.business_documents od ON od."TenantId"=o."TenantId" AND od."Id"=o."DocumentId" AND od."SourceSystem"='pos.sale.v1'
        WHERE e."TenantId"=s."TenantId" AND e."PostingId"=marker AND e."BatchId"=s."Id" AND e."ItemId"=s."ProductId" AND e."LocationId"=s."LocationId"
          AND e."ZoneId" IS NOT DISTINCT FROM s."ZoneId" AND e."BatchNumberSnapshot" IS NOT DISTINCT FROM s."BatchNumber" AND e."ExpiryDateSnapshot"=s."ExpiryDate"
          AND d."DocumentType"='pos_return' AND d."SourceId"=od."SourceId" AND d."ReversesDocumentId"=od."Id"
          AND d."Status"=CASE WHEN completed THEN 'posted' ELSE 'approved' END AND p."PerformedBy"=d."CreatedBy" AND a."ApproverId"=p."PerformedBy"
          AND p."PerformedBy"=NULLIF(current_setting('app.user_id',true),'')::uuid
          AND e."QuantityDelta">0 AND e."QuantityDelta"=-o."QuantityDelta" AND e."QuantityBefore"=before_qty AND e."QuantityAfter"=s."Quantity" AND s."Quantity"=before_qty+e."QuantityDelta"
          AND l."Quantity"=e."QuantityDelta" AND l."ToLocationId"=s."LocationId" AND l."FromLocationId" IS NULL AND l."Unit"=ol."Unit"
          AND l."Metadata"->>'originalEntryId'=o."Id"::text AND l."Metadata"->'source'=public.erp_w4_pos_snapshot(od."SourceId"::uuid)) THEN
        RAISE EXCEPTION 'POS return requires an exact staged source reversal' USING ERRCODE='P4F01'; END IF;
      RETURN true;
    END $$;
    CREATE FUNCTION public.erp_w4_pos_return_complete(posting uuid) RETURNS void LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    DECLARE d public.business_documents; j public.pos_return_fiscalizations; sale public.pos_transactions;
    BEGIN
      SELECT x.* INTO d FROM public.business_documents x JOIN public.document_postings p ON p."TenantId"=x."TenantId" AND p."DocumentId"=x."Id" WHERE p."Id"=posting AND x."SourceSystem"='pos.return.v1';
      IF NOT FOUND THEN RETURN; END IF;
      SELECT * INTO j FROM public.pos_return_fiscalizations WHERE "TenantId"=d."TenantId" AND "Id"=d."Id";
      SELECT * INTO sale FROM public.pos_transactions WHERE "TenantId"=d."TenantId" AND "Id"::text=d."SourceId";
      IF j."Id" IS NULL OR sale."Id" IS NULL OR j."SaleId"<>sale."Id" OR j."Amount"<>sale."TotalAmount" OR j."TaxAmount"<>sale."TaxAmount" OR j."PaymentType"<>sale."PaymentType"
        OR j."RequestHash"<>d."ContentHash" OR d."Status"<>'posted' OR sale."LoyaltyMembershipId" IS NOT NULL
        OR lower(sale."PaymentType") NOT IN ('cash','card') OR EXISTS(SELECT 1 FROM public.loyalty_ledger_entries WHERE "TenantId"=sale."TenantId" AND "PosTransactionId"=sale."Id")
        OR NOT EXISTS(SELECT 1 FROM public.pos_shifts s WHERE s."Id"=j."ShiftId" AND s."TenantId"=j."TenantId" AND s."LocationId"=sale."LocationId" AND s."ClosedAt" IS NULL AND s."CashierId"=d."CreatedBy" AND s."RegisterId" IS NOT DISTINCT FROM sale."CashRegisterId")
        OR NOT EXISTS(SELECT 1 FROM public.document_approvals a WHERE a."TenantId"=d."TenantId" AND a."DocumentId"=d."Id" AND a."Reason"=j."Reason")
        OR j."RequestJson"->>'LocalReceiptId' IS DISTINCT FROM d."Id"::text OR j."RequestJson"->>'RelatedReceiptId' IS DISTINCT FROM sale."Id"::text OR j."RequestJson"->>'IsReturn' IS DISTINCT FROM 'true'
        OR j."RequestJson"->'Items' IS DISTINCT FROM (SELECT jsonb_agg(jsonb_build_object('Code',i."ProductId"::text,'Name',i."ProductNameSnapshot",'UnitPrice',i."PriceFinal",'Quantity',i."Quantity",'Barcode',i."BarcodeSnapshot",'VatRate',i."VatRate") ORDER BY i."Id") FROM public.pos_transaction_items i WHERE i."TransactionId"=sale."Id")
        OR j."RequestJson"->'Payments' IS DISTINCT FROM jsonb_build_array(jsonb_build_object('Kind',CASE WHEN lower(sale."PaymentType")='cash' THEN 0 ELSE 1 END,'Amount',sale."TotalAmount"))
        OR EXISTS(SELECT 1 FROM public.inventory_ledger_entries e JOIN public.product_stock s ON s."Id"=e."BatchId" AND s."TenantId"=e."TenantId"
          LEFT JOIN public.inventory_balance_projections b ON b."TenantId"=e."TenantId" AND b."BatchId"=e."BatchId"
          WHERE e."PostingId"=posting AND (NOT public.erp_w4_pos_return_stage(s,e."QuantityBefore",true) OR b."Quantity" IS DISTINCT FROM e."QuantityAfter" OR b."LastLedgerEntryId" IS DISTINCT FROM e."Id" OR b."ReservedQuantity"<>0
            OR NOT EXISTS(SELECT 1 FROM public.stock_movements m WHERE m."TenantId"=e."TenantId" AND m."ProductStockId"=e."BatchId" AND m."ReferenceId"=d."Id" AND m."ReferenceType"='pos_return' AND m."MovementType"='adjustment' AND m."Quantity"=e."QuantityDelta" AND m."QuantityBefore"=e."QuantityBefore" AND m."QuantityAfter"=e."QuantityAfter" AND m."PerformedBy"=d."CreatedBy"))) THEN
        RAISE EXCEPTION 'POS return source, fiscal job, stock, movement and projection disagree' USING ERRCODE='P4F01'; END IF;
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
      IF public.erp_w4_pos_return_stage(NEW,OLD."Quantity",false) THEN RETURN NEW; END IF;
    IF public.erp_w4_pos_stage(NEW,OLD."Quantity",false) THEN RETURN NEW; END IF;
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
      IF public.erp_w4_pos_return_stage(NEW,OLD."Quantity",true) THEN PERFORM public.erp_w4_pos_return_complete(posting); RETURN NULL; END IF;
    IF public.erp_w4_pos_stage(NEW,OLD."Quantity",true) THEN
                IF NOT EXISTS(SELECT 1 FROM public.product_stock s WHERE s."Id"=NEW."Id" AND to_jsonb(s)=to_jsonb(NEW)) THEN RAISE EXCEPTION 'POS batch changed before commit' USING ERRCODE='P4F01'; END IF;
                PERFORM public.erp_w4_pos_complete((SELECT d."SourceId"::uuid FROM public.document_postings p JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId" WHERE p."Id"=posting),true); RETURN NULL; END IF;
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

    CREATE OR REPLACE FUNCTION public.erp_w4_pos_complete(sale_id uuid,verify_stock boolean) RETURNS void LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    DECLARE t public.pos_transactions; d public.business_documents; p public.document_postings; marker uuid; n integer; matched integer;
    BEGIN
      SELECT * INTO t FROM public.pos_transactions WHERE "Id"=sale_id;
      IF t."Id" IS NULL THEN RETURN; END IF;
      SELECT * INTO d FROM public.business_documents WHERE "TenantId"=t."TenantId" AND "SourceSystem"='pos.sale.v1' AND "SourceId"=sale_id::text;
      IF d."Id" IS NULL THEN
        IF verify_stock AND EXISTS(SELECT 1 FROM public.inventory_posting_scopes f WHERE f."TenantId"=t."TenantId" AND f."LocationId"=t."LocationId") THEN
          RAISE EXCEPTION 'Fenced POS sale requires an ERP document' USING ERRCODE='P4F01'; END IF;
        RETURN;
      END IF;
      SELECT * INTO p FROM public.document_postings WHERE "TenantId"=t."TenantId" AND "DocumentId"=d."Id";
      BEGIN marker:=NULLIF(current_setting('app.erp_posting_id',true),'')::uuid; EXCEPTION WHEN invalid_text_representation THEN marker:=NULL; END;
      IF p."Id" IS NULL OR (d."Status"<>'posted' AND (verify_stock OR d."Status"<>'reversed')) OR d."DocumentType"<>'pos_sale' OR d."CreatedBy" IS DISTINCT FROM t."CashierId" OR p."PerformedBy" IS DISTINCT FROM t."CashierId"
        OR verify_stock AND marker IS DISTINCT FROM p."Id" OR t."Status" NOT IN ('pending_fiscalization','fiscalized','fiscalization_failed')
        OR NOT EXISTS(SELECT 1 FROM public.document_approvals a WHERE a."TenantId"=t."TenantId" AND a."DocumentId"=d."Id" AND a."Stage"=1 AND a."Decision"='approved' AND a."ContentHash"=d."ContentHash" AND a."ApproverId"=t."CashierId")
        OR NOT EXISTS(SELECT 1 FROM public.outbox_events o WHERE o."TenantId"=t."TenantId" AND o."PostingId"=p."Id" AND o."DeliveryScope"='live') THEN
        RAISE EXCEPTION 'POS sale, confirmation, posting and live event must commit together' USING ERRCODE='P4F01'; END IF;
      SELECT count(*) INTO n FROM public.document_lines WHERE "TenantId"=t."TenantId" AND "DocumentId"=d."Id";
      IF n NOT BETWEEN 1 AND 100 OR n<>(SELECT count(*) FROM public.inventory_ledger_entries WHERE "TenantId"=t."TenantId" AND "PostingId"=p."Id")
        OR n<>(SELECT count(DISTINCT l."Metadata"->>'batchId') FROM public.document_lines l WHERE l."TenantId"=t."TenantId" AND l."DocumentId"=d."Id")
        OR EXISTS(SELECT 1 FROM public.document_lines l WHERE l."TenantId"=t."TenantId" AND l."DocumentId"=d."Id" AND l."Metadata"->'source' IS DISTINCT FROM public.erp_w4_pos_snapshot(sale_id)) THEN
        RAISE EXCEPTION 'POS financial snapshot and batch graph are inconsistent' USING ERRCODE='P4F01'; END IF;
      SELECT count(*) INTO matched FROM public.document_lines l
        JOIN public.inventory_ledger_entries e ON e."TenantId"=t."TenantId" AND e."PostingId"=p."Id" AND e."DocumentLineId"=l."Id"
        WHERE l."TenantId"=t."TenantId" AND l."DocumentId"=d."Id" AND l."Metadata"->>'saleId'=sale_id::text AND l."Metadata"->>'batchId'=e."BatchId"::text
          AND l."ItemId"=e."ItemId" AND l."FromLocationId"=t."LocationId" AND l."ToLocationId" IS NULL AND l."Quantity">0 AND e."QuantityDelta"=-l."Quantity"
          AND e."LocationId"=t."LocationId" AND e."QuantityBefore"+e."QuantityDelta"=e."QuantityAfter" AND e."UnitCost" IS NOT DISTINCT FROM l."UnitCost"
          AND jsonb_typeof(l."Metadata"->'eventIds')='array' AND jsonb_array_length(l."Metadata"->'eventIds')>0
          AND jsonb_array_length(l."Metadata"->'eventIds')=(SELECT count(*) FROM public.stock_events v WHERE v."TenantId"=t."TenantId" AND v."Id"::text IN (SELECT jsonb_array_elements_text(l."Metadata"->'eventIds'))
            AND v."ProductStockId"=e."BatchId" AND v."ProductId"=e."ItemId" AND v."StoreId"=t."LocationId" AND v."EventType"='pos_sale' AND v."QuantityDelta"<0 AND v."PerformedBy"=t."CashierId" AND v."Meta"->>'tx_id'=sale_id::text)
          AND e."QuantityDelta"=(SELECT sum(v."QuantityDelta") FROM public.stock_events v WHERE v."TenantId"=t."TenantId" AND v."Id"::text IN (SELECT jsonb_array_elements_text(l."Metadata"->'eventIds')))
          AND EXISTS(SELECT 1 FROM public.stock_movements m WHERE m."TenantId"=t."TenantId" AND m."ReferenceId"=sale_id AND m."ReferenceType"='pos_sale' AND m."MovementType"='sale'
            AND m."ProductStockId"=e."BatchId" AND m."ProductId"=e."ItemId" AND m."FromLocationId"=t."LocationId" AND m."FromZoneId" IS NOT DISTINCT FROM e."ZoneId" AND m."ToLocationId" IS NULL
            AND m."Quantity"=l."Quantity" AND m."QuantityBefore"=e."QuantityBefore" AND m."QuantityAfter"=e."QuantityAfter" AND m."PerformedBy"=t."CashierId" AND m."TotalAmount" IS NULL AND m."UnitPrice" IS NULL)
          AND (NOT verify_stock OR EXISTS(SELECT 1 FROM public.product_stock s JOIN public.inventory_balance_projections b ON b."TenantId"=s."TenantId" AND b."BatchId"=s."Id"
            WHERE s."TenantId"=t."TenantId" AND s."Id"=e."BatchId" AND s."ProductId"=e."ItemId" AND s."LocationId"=t."LocationId" AND s."ZoneId" IS NOT DISTINCT FROM e."ZoneId"
              AND s."BatchNumber" IS NOT DISTINCT FROM l."BatchNumberSnapshot" AND s."ExpiryDate"=l."ExpiryDateSnapshot" AND s."Quantity"=e."QuantityAfter"
              AND b."ItemId"=s."ProductId" AND b."LocationId"=s."LocationId" AND b."Quantity"=s."Quantity" AND b."ReservedQuantity"=0 AND b."LastLedgerEntryId"=e."Id"
              AND s."Quantity"=(SELECT sum(x."QuantityDelta") FROM public.inventory_ledger_entries x WHERE x."TenantId"=s."TenantId" AND x."BatchId"=s."Id")));
      IF matched<>n OR (SELECT count(*) FROM public.stock_events v WHERE v."TenantId"=t."TenantId" AND v."Meta"->>'tx_id'=sale_id::text)<>(SELECT sum(jsonb_array_length(l."Metadata"->'eventIds')) FROM public.document_lines l WHERE l."TenantId"=t."TenantId" AND l."DocumentId"=d."Id")
        OR EXISTS(SELECT 1 FROM (SELECT "ProductId" item,sum("Quantity") qty FROM public.pos_transaction_items WHERE "TransactionId"=sale_id GROUP BY "ProductId") sold
          FULL JOIN (SELECT "ItemId" item,sum("Quantity") qty FROM public.document_lines WHERE "TenantId"=t."TenantId" AND "DocumentId"=d."Id" GROUP BY "ItemId") posted USING(item)
          WHERE sold.qty IS DISTINCT FROM posted.qty)
        OR NOT EXISTS(SELECT 1 FROM public.pos_transaction_items WHERE "TransactionId"=sale_id)
        OR t."TotalAmount" IS DISTINCT FROM (SELECT sum("TaxableAmount") FROM public.pos_transaction_items WHERE "TransactionId"=sale_id)
        OR t."TotalAmount">(SELECT sum("LineTotal") FROM public.pos_transaction_items WHERE "TransactionId"=sale_id)
        OR t."TaxAmount" IS DISTINCT FROM (SELECT sum("VatAmount") FROM public.pos_transaction_items WHERE "TransactionId"=sale_id)
        OR EXISTS(SELECT 1 FROM public.pos_transaction_items WHERE "TransactionId"=sale_id AND ("Quantity"<=0 OR "LineTotal" IS NULL OR "VatAmount" IS NULL OR "TaxableAmount" IS NULL OR "ProductNameSnapshot" IS NULL
          OR "LineTotal"<0 OR "TaxableAmount"<0 OR "TaxableAmount">"LineTotal" OR "VatRate" IS NULL OR "VatRate" NOT BETWEEN 0 AND 100 OR "VatAmount"<0 OR "VatAmount">"TaxableAmount")) THEN
        RAISE EXCEPTION 'POS quantities, tax snapshot, events, ledger and projection disagree' USING ERRCODE='P4F01'; END IF;
    END $$;CREATE FUNCTION public.erp_w4_pos_return_job_before() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    BEGIN
      IF TG_OP='INSERT' THEN
        IF NEW."Status"<>'pending' OR NEW."Attempts"<>0 OR NEW."FiscalNumber" IS NOT NULL OR NEW."LastError" IS NOT NULL OR NEW."ClientRequestId"='00000000-0000-0000-0000-000000000000'::uuid
          OR NEW."RequestHash" !~ '^[0-9a-f]{64}$' OR length(btrim(NEW."Reason")) NOT BETWEEN 15 AND 500 THEN RAISE EXCEPTION 'Invalid new fiscal compensation' USING ERRCODE='P4F01'; END IF;
        RETURN NEW;
      END IF;
      IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Retain fiscal compensation audit' USING ERRCODE='P4F01'; END IF;
      IF (to_jsonb(NEW)-ARRAY['Status','Attempts','FiscalNumber','LastError']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['Status','Attempts','FiscalNumber','LastError'])
        OR OLD."Status"<>'pending' AND to_jsonb(NEW) IS DISTINCT FROM to_jsonb(OLD) OR NEW."Attempts"<OLD."Attempts" OR NEW."Attempts">OLD."Attempts"+1 THEN
        RAISE EXCEPTION 'Fiscal compensation source is immutable' USING ERRCODE='P4F01'; END IF;
      RETURN NEW;
    END $$;
    CREATE FUNCTION public.erp_w4_pos_return_job_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    BEGIN
      IF NOT EXISTS(SELECT 1 FROM public.business_documents d JOIN public.document_postings p ON p."TenantId"=d."TenantId" AND p."DocumentId"=d."Id" WHERE d."TenantId"=NEW."TenantId" AND d."Id"=NEW."Id" AND d."SourceSystem"='pos.return.v1' AND d."SourceId"=NEW."SaleId"::text AND d."Status"='posted') THEN
        RAISE EXCEPTION 'Fiscal compensation requires a committed POS inventory return' USING ERRCODE='P4F01'; END IF;
      PERFORM public.erp_w4_pos_return_complete((SELECT "Id" FROM public.document_postings WHERE "TenantId"=NEW."TenantId" AND "DocumentId"=NEW."Id")); RETURN NULL;
    END $$;
    CREATE FUNCTION public.erp_w4_pos_return_doc_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    BEGIN
      IF NEW."SourceSystem"='pos.return.v1' THEN
        PERFORM public.erp_w4_pos_return_complete((SELECT "Id" FROM public.document_postings WHERE "TenantId"=NEW."TenantId" AND "DocumentId"=NEW."Id"));
      END IF; RETURN NULL;
    END $$;
    CREATE CONSTRAINT TRIGGER erp_w4_pos_return_doc_complete AFTER INSERT OR UPDATE ON public.business_documents DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.erp_w4_pos_return_doc_complete();
    CREATE TRIGGER erp_w4_pos_return_job_before BEFORE INSERT OR UPDATE OR DELETE ON public.pos_return_fiscalizations FOR EACH ROW EXECUTE FUNCTION public.erp_w4_pos_return_job_before();
    CREATE CONSTRAINT TRIGGER erp_w4_pos_return_job_complete AFTER INSERT ON public.pos_return_fiscalizations DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.erp_w4_pos_return_job_complete();
    CREATE TRIGGER erp_w4_pos_return_job_truncate BEFORE TRUNCATE ON public.pos_return_fiscalizations FOR EACH STATEMENT EXECUTE FUNCTION public.erp_w4_transfer_truncate();
    CREATE FUNCTION public.erp_w4_pos_returns_ready(tenant uuid,location uuid) RETURNS boolean LANGUAGE sql VOLATILE SET search_path=pg_catalog,public AS $$
      SELECT public.erp_w4_pos_ready(tenant,location) AND (SELECT count(*)=4 FROM pg_trigger WHERE NOT tgisinternal AND tgenabled='O' AND (tgrelid='public.pos_return_fiscalizations'::regclass AND (
        tgname='erp_w4_pos_return_job_before' AND tgtype=31 AND tgfoid='public.erp_w4_pos_return_job_before()'::regprocedure
        OR tgname='erp_w4_pos_return_job_complete' AND tgtype=5 AND tgdeferrable AND tginitdeferred AND tgfoid='public.erp_w4_pos_return_job_complete()'::regprocedure
        OR tgname='erp_w4_pos_return_job_truncate' AND tgtype=34 AND tgfoid='public.erp_w4_transfer_truncate()'::regprocedure)
        OR tgrelid='public.business_documents'::regclass AND tgname='erp_w4_pos_return_doc_complete' AND tgtype=21 AND tgdeferrable AND tginitdeferred AND tgfoid='public.erp_w4_pos_return_doc_complete()'::regprocedure))
    $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027114758_AddErpPosReturnPosting') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261027114758_AddErpPosReturnPosting', '8.0.11');
    END IF;
END $EF$;
COMMIT;

