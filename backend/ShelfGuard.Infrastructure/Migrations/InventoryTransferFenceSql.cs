namespace ShelfGuard.Infrastructure.Migrations;

internal static class InventoryTransferFenceSql
{
    internal static readonly string Upgrade = Build();
    private static string Extract(string name, string next)
    {
        var sql = InventoryReceiptFenceSql.Upgrade;
        var start = sql.IndexOf("CREATE OR REPLACE FUNCTION public." + name, StringComparison.Ordinal);
        return sql[start..sql.IndexOf("CREATE OR REPLACE FUNCTION public." + next, start, StringComparison.Ordinal)];
    }
    private static string Build()
    {
        var before = Extract("erp_w4_stock_before", "erp_w4_stock_complete")
            .Replace("PERFORM public.erp_w4_receipt_insert_check(NEW);", "IF NEW.\"SourceType\"='transfer' THEN IF NOT public.erp_w4_transfer_stage(NEW,0,true,false) THEN RAISE EXCEPTION 'Transfer posting required' USING ERRCODE='P4F01'; END IF; ELSE PERFORM public.erp_w4_receipt_insert_check(NEW); END IF;", StringComparison.Ordinal);
        const string anchor = "-- A committed posting can never have an approved document";
        before = before.Insert(before.IndexOf(anchor, StringComparison.Ordinal), "IF public.erp_w4_transfer_stage(NEW,OLD.\"Quantity\",false,false) THEN RETURN NEW; END IF;\n          ");
        var complete = Extract("erp_w4_stock_complete", "erp_w4_scope_ready")
            .Replace("PERFORM public.erp_w4_receipt_complete_check(NEW);", "IF NEW.\"SourceType\"='transfer' THEN IF NOT EXISTS(SELECT 1 FROM public.product_stock s WHERE s.\"Id\"=NEW.\"Id\" AND to_jsonb(s)=to_jsonb(NEW)) THEN RAISE EXCEPTION 'Transfer batch changed before commit' USING ERRCODE='P4F01'; END IF; PERFORM public.erp_w4_transfer_complete(NEW.\"SourceId\"); ELSE PERFORM public.erp_w4_receipt_complete_check(NEW); END IF;", StringComparison.Ordinal);
        var index = complete.IndexOf("IF posting IS NULL OR NOT EXISTS (", StringComparison.Ordinal);
        complete = complete.Insert(index, "IF public.erp_w4_transfer_stage(NEW,OLD.\"Quantity\",false,true) THEN\n            IF NOT EXISTS(SELECT 1 FROM public.product_stock s WHERE s.\"Id\"=NEW.\"Id\" AND to_jsonb(s)=to_jsonb(NEW)) THEN RAISE EXCEPTION 'Transfer batch changed before commit' USING ERRCODE='P4F01'; END IF;\n            PERFORM public.erp_w4_transfer_complete((SELECT d.\"SourceId\"::uuid FROM public.document_postings p JOIN public.business_documents d ON d.\"Id\"=p.\"DocumentId\" AND d.\"TenantId\"=p.\"TenantId\" WHERE p.\"Id\"=posting)); RETURN NULL; END IF;\n          ");
        return Helpers + before + complete + Triggers;
    }
    private const string Helpers = """
        CREATE FUNCTION public.erp_w4_transfer_stage(s public.product_stock, before_qty numeric, inserting boolean, completed boolean) RETURNS boolean LANGUAGE plpgsql
          SET search_path=pg_catalog,public AS $$
        DECLARE marker uuid; phase text; valid boolean;
        BEGIN
          BEGIN marker:=NULLIF(current_setting('app.erp_posting_id',true),'')::uuid; EXCEPTION WHEN invalid_text_representation THEN marker:=NULL; END;
          SELECT d."SourceSystem" INTO phase FROM public.document_postings p JOIN public.business_documents d ON d."Id"=p."DocumentId" AND d."TenantId"=p."TenantId"
            WHERE p."Id"=marker AND p."TenantId"=s."TenantId";
          IF phase IS NULL OR phase NOT IN ('transfer.dispatch.v1','transfer.receive.v1','transfer.cancel.v1') THEN RETURN false; END IF;
          SELECT EXISTS (
            SELECT 1 FROM public.document_postings p JOIN public.business_documents d ON d."Id"=p."DocumentId" AND d."TenantId"=p."TenantId"
            JOIN public.stock_transfers t ON t."TenantId"=d."TenantId" AND t."Id"::text=d."SourceId"
            JOIN public.document_lines l ON l."DocumentId"=d."Id" AND l."TenantId"=d."TenantId"
            JOIN public.stock_transfer_items ti ON ti."TransferId"=t."Id" AND ti."Id"::text=l."Metadata"->>'transferItemId'
            JOIN public.document_approvals a ON a."DocumentId"=d."Id" AND a."TenantId"=d."TenantId" AND a."Stage"=1 AND a."Decision"='approved'
            WHERE p."Id"=marker AND p."TenantId"=s."TenantId" AND d."DocumentType"='stock_transfer'
              AND d."Status"=CASE WHEN completed THEN 'posted' ELSE 'approved' END
              AND p."PerformedBy"=d."CreatedBy" AND p."PerformedBy"=NULLIF(current_setting('app.user_id',true),'')::uuid
              AND a."ApproverId"=p."PerformedBy" AND a."ContentHash"=d."ContentHash"
              AND l."Metadata"->>'transferId'=t."Id"::text AND l."Metadata"->>'batchId'=s."Id"::text
              AND l."Metadata"->>'sourceBatchId'=ti."ProductStockId"::text
              AND l."FromLocationId"=t."FromLocationId" AND l."ToLocationId"=t."ToLocationId"
              AND ti."ProductId"=s."ProductId" AND l."ItemId"=s."ProductId" AND l."Quantity"=ti."Quantity" AND ti."Quantity">0
              AND ti."ExpiryDate"=s."ExpiryDate" AND ti."BatchNumber" IS NOT DISTINCT FROM s."BatchNumber"
              AND l."ExpiryDateSnapshot"=s."ExpiryDate" AND l."BatchNumberSnapshot" IS NOT DISTINCT FROM s."BatchNumber"
              AND (inserting AND phase='transfer.receive.v1' AND s."LocationId"=t."ToLocationId" AND s."SourceType"='transfer'
                  AND s."SourceId"=t."Id" AND s."Quantity"=ti."Quantity" AND s."QuantityInitial"=ti."Quantity"
                  AND s."AddedBy"=p."PerformedBy" AND s."AddedAt"=p."PostedAt" AND s."ZoneId" IS NULL AND s."ShelfNumber" IS NULL
                OR NOT inserting AND phase IN ('transfer.dispatch.v1','transfer.cancel.v1') AND s."LocationId"=t."FromLocationId"
                  AND s."Id"=ti."ProductStockId" AND s."Quantity"=before_qty+CASE WHEN phase='transfer.dispatch.v1' THEN -ti."Quantity" ELSE ti."Quantity" END)
              AND (inserting AND NOT completed OR EXISTS (
                SELECT 1 FROM public.inventory_ledger_entries e WHERE e."TenantId"=s."TenantId" AND e."PostingId"=marker AND e."DocumentLineId"=l."Id"
                  AND e."BatchId"=s."Id" AND e."LocationId"=s."LocationId" AND e."ItemId"=s."ProductId"
                  AND e."QuantityBefore"=before_qty AND e."QuantityAfter"=s."Quantity" AND e."QuantityDelta"=s."Quantity"-before_qty
                  AND e."UnitCost" IS NOT DISTINCT FROM l."UnitCost"))
          ) INTO valid;
          IF NOT valid THEN RAISE EXCEPTION 'A matching staged transfer posting is required' USING ERRCODE='P4F01'; END IF;
          RETURN true;
        END $$;

        CREATE FUNCTION public.erp_w4_transfer_complete(transfer_id uuid) RETURNS void LANGUAGE plpgsql
          SET search_path=pg_catalog,public AS $$
        DECLARE marker uuid; d public.business_documents; p public.document_postings; t public.stock_transfers; phase text; n integer; matched integer;
        BEGIN
          BEGIN marker:=NULLIF(current_setting('app.erp_posting_id',true),'')::uuid; EXCEPTION WHEN invalid_text_representation THEN marker:=NULL; END;
          SELECT * INTO t FROM public.stock_transfers WHERE "Id"=transfer_id;
          IF NOT EXISTS(SELECT 1 FROM public.business_documents x WHERE x."TenantId"=t."TenantId" AND x."SourceSystem"='transfer.dispatch.v1' AND x."SourceId"=transfer_id::text) THEN RETURN; END IF;
          SELECT * INTO p FROM public.document_postings WHERE "Id"=marker AND "TenantId"=t."TenantId";
          SELECT * INTO d FROM public.business_documents WHERE "Id"=p."DocumentId" AND "TenantId"=t."TenantId";
          phase:=d."SourceSystem";
          IF phase IS NULL OR phase NOT IN ('transfer.dispatch.v1','transfer.receive.v1','transfer.cancel.v1') OR d."SourceId"<>transfer_id::text
            OR d."Status"<>'posted' OR d."DocumentType"<>'stock_transfer'
            OR t."Status"<>(CASE phase WHEN 'transfer.dispatch.v1' THEN 'in_transit' WHEN 'transfer.receive.v1' THEN 'received' ELSE 'cancelled' END)
            OR phase='transfer.receive.v1' AND t."ConfirmedBy" IS DISTINCT FROM p."PerformedBy"
            OR phase='transfer.dispatch.v1' AND t."InitiatedBy" IS DISTINCT FROM p."PerformedBy"
            OR NOT EXISTS(SELECT 1 FROM public.outbox_events o WHERE o."TenantId"=t."TenantId" AND o."PostingId"=marker AND o."DeliveryScope"='live') THEN
            RAISE EXCEPTION 'Transfer header, posting and live event must commit together' USING ERRCODE='P4F01'; END IF;
          SELECT count(*) INTO n FROM public.stock_transfer_items WHERE "TransferId"=transfer_id;
          IF n NOT BETWEEN 1 AND 100 OR n<>(SELECT count(*) FROM public.document_lines WHERE "TenantId"=t."TenantId" AND "DocumentId"=d."Id")
            OR n<>(SELECT count(*) FROM public.inventory_ledger_entries WHERE "TenantId"=t."TenantId" AND "PostingId"=marker)
            OR n<>(SELECT count(DISTINCT l."Metadata"->>'transferItemId') FROM public.document_lines l WHERE l."TenantId"=t."TenantId" AND l."DocumentId"=d."Id") THEN
            RAISE EXCEPTION 'Transfer line cardinality mismatch' USING ERRCODE='P4F01'; END IF;
          SELECT count(*) INTO matched FROM public.stock_transfer_items ti
            JOIN public.document_lines l ON l."TenantId"=t."TenantId" AND l."DocumentId"=d."Id" AND l."Metadata"->>'transferItemId'=ti."Id"::text
            JOIN public.inventory_ledger_entries e ON e."TenantId"=t."TenantId" AND e."PostingId"=marker AND e."DocumentLineId"=l."Id"
            JOIN public.product_stock s ON s."TenantId"=t."TenantId" AND s."Id"::text=l."Metadata"->>'batchId'
            JOIN public.inventory_balance_projections b ON b."TenantId"=s."TenantId" AND b."BatchId"=s."Id" AND b."ItemId"=s."ProductId" AND b."LocationId"=s."LocationId"
            WHERE ti."TransferId"=transfer_id AND l."ItemId"=ti."ProductId" AND l."Quantity"=ti."Quantity"
              AND l."Metadata"->>'transferId'=transfer_id::text AND l."Metadata"->>'sourceBatchId'=ti."ProductStockId"::text
              AND l."FromLocationId"=t."FromLocationId" AND l."ToLocationId"=t."ToLocationId"
              AND s."ProductId"=ti."ProductId" AND s."BatchNumber" IS NOT DISTINCT FROM ti."BatchNumber" AND s."ExpiryDate"=ti."ExpiryDate"
              AND l."BatchNumberSnapshot" IS NOT DISTINCT FROM ti."BatchNumber" AND l."ExpiryDateSnapshot"=ti."ExpiryDate"
              AND e."ItemId"=ti."ProductId" AND e."BatchId"=s."Id" AND e."LocationId"=s."LocationId"
              AND e."QuantityAfter"=s."Quantity" AND b."Quantity"=s."Quantity" AND b."ReservedQuantity"=0 AND b."LastLedgerEntryId"=e."Id"
              AND e."UnitCost" IS NOT DISTINCT FROM l."UnitCost"
              AND s."Quantity"=(SELECT sum(x."QuantityDelta") FROM public.inventory_ledger_entries x WHERE x."TenantId"=s."TenantId" AND x."BatchId"=s."Id" AND x."ItemId"=s."ProductId" AND x."LocationId"=s."LocationId")
              AND (phase='transfer.dispatch.v1' AND e."QuantityDelta"=-ti."Quantity" AND s."Id"=ti."ProductStockId" AND s."LocationId"=t."FromLocationId"
                OR phase='transfer.cancel.v1' AND e."QuantityDelta"=ti."Quantity" AND s."Id"=ti."ProductStockId" AND s."LocationId"=t."FromLocationId"
                OR phase='transfer.receive.v1' AND e."QuantityDelta"=ti."Quantity" AND e."QuantityBefore"=0 AND s."LocationId"=t."ToLocationId"
                  AND s."SourceType"='transfer' AND s."SourceId"=transfer_id AND s."QuantityInitial"=ti."Quantity" AND s."AddedBy"=p."PerformedBy" AND s."AddedAt"=p."PostedAt")
              AND (phase='transfer.dispatch.v1' OR EXISTS(
                SELECT 1 FROM public.business_documents od JOIN public.document_lines ol ON ol."TenantId"=od."TenantId" AND ol."DocumentId"=od."Id"
                JOIN public.inventory_ledger_entries oe ON oe."TenantId"=od."TenantId" AND oe."DocumentLineId"=ol."Id" AND oe."DocumentId"=od."Id"
                JOIN public.document_links link ON link."TenantId"=od."TenantId" AND link."FromDocumentId"=od."Id" AND link."ToDocumentId"=d."Id"
                WHERE od."TenantId"=t."TenantId" AND od."SourceSystem"='transfer.dispatch.v1' AND od."SourceId"=transfer_id::text
                  AND od."Status"=CASE WHEN phase='transfer.cancel.v1' THEN 'reversed' ELSE 'posted' END
                  AND ol."Metadata"->>'transferItemId'=ti."Id"::text AND oe."Id"::text=l."Metadata"->>'originalEntryId'
                  AND oe."QuantityDelta"=-ti."Quantity" AND oe."BatchId"=ti."ProductStockId" AND oe."ItemId"=ti."ProductId"
                  AND oe."UnitCost" IS NOT DISTINCT FROM e."UnitCost" AND ol."Unit"=l."Unit" AND ol."ItemNameSnapshot"=l."ItemNameSnapshot"
                  AND ol."BarcodeSnapshot" IS NOT DISTINCT FROM l."BarcodeSnapshot"
                  AND (phase='transfer.receive.v1' AND link."RelationType"='receives' OR phase='transfer.cancel.v1' AND link."RelationType"='cancels'
                    AND d."ReversesDocumentId"=od."Id" AND e."ReversesEntryId"=oe."Id")
              ))
              AND EXISTS(SELECT 1 FROM public.stock_movements m WHERE m."TenantId"=t."TenantId" AND m."ReferenceId"=transfer_id AND m."ProductStockId"=s."Id"
                AND m."ProductId"=s."ProductId" AND m."MovementType"='transfer' AND m."Quantity"=ti."Quantity" AND m."QuantityBefore"=e."QuantityBefore"
                AND m."QuantityAfter"=e."QuantityAfter" AND m."PerformedBy"=p."PerformedBy" AND m."UnitPrice" IS NOT DISTINCT FROM e."UnitCost"
                AND m."TotalAmount" IS NOT DISTINCT FROM round(abs(e."ValueDelta"),2)
                AND (phase='transfer.cancel.v1' AND m."ReferenceType"='stock_transfer_cancellation' AND m."ToLocationId"=t."FromLocationId" AND m."FromLocationId" IS NULL
                  OR phase<>'transfer.cancel.v1' AND m."ReferenceType"='stock_transfer' AND m."FromLocationId"=t."FromLocationId" AND m."ToLocationId"=t."ToLocationId"));
          IF matched<>n OR phase='transfer.receive.v1' AND n<>(SELECT count(*) FROM public.product_stock s WHERE s."TenantId"=t."TenantId" AND s."SourceType"='transfer' AND s."SourceId"=transfer_id) THEN
            RAISE EXCEPTION 'Transfer stock, transit allocation, ledger, projections and movements disagree' USING ERRCODE='P4F01'; END IF;
        END $$;
        """;
    private const string Triggers = """
        CREATE FUNCTION public.erp_w4_transfer_header_before() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
        DECLARE phase text; actor uuid;
        BEGIN
          IF NOT EXISTS(SELECT 1 FROM public.business_documents d WHERE d."TenantId"=OLD."TenantId" AND d."SourceId"=OLD."Id"::text AND d."SourceSystem"='transfer.dispatch.v1') THEN
            IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
          END IF;
          IF TG_OP='DELETE' THEN RAISE EXCEPTION 'ERP transfer history is immutable' USING ERRCODE='P4F01'; END IF;
          IF to_jsonb(NEW)=to_jsonb(OLD) THEN RETURN NEW; END IF;
          SELECT d."SourceSystem",p."PerformedBy" INTO phase,actor FROM public.document_postings p JOIN public.business_documents d ON d."Id"=p."DocumentId" AND d."TenantId"=p."TenantId"
            WHERE p."TenantId"=OLD."TenantId" AND p."Id"::text=current_setting('app.erp_posting_id',true) AND d."SourceId"=OLD."Id"::text AND d."Status"='approved';
          IF OLD."Status"<>'in_transit' OR phase IS NULL OR phase NOT IN ('transfer.receive.v1','transfer.cancel.v1')
            OR NEW."Status"<>(CASE phase WHEN 'transfer.receive.v1' THEN 'received' ELSE 'cancelled' END)
            OR (to_jsonb(NEW)-ARRAY['Status','ConfirmedBy']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['Status','ConfirmedBy'])
            OR phase='transfer.receive.v1' AND NEW."ConfirmedBy" IS DISTINCT FROM actor
            OR phase='transfer.cancel.v1' AND NEW."ConfirmedBy" IS DISTINCT FROM OLD."ConfirmedBy" THEN
            RAISE EXCEPTION 'ERP transfer transition requires terminal posting' USING ERRCODE='P4F01'; END IF;
          RETURN NEW;
        END $$;
        CREATE FUNCTION public.erp_w4_transfer_items_before() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
        BEGIN
          IF EXISTS(SELECT 1 FROM public.stock_transfers t JOIN public.business_documents d ON d."TenantId"=t."TenantId" AND d."SourceId"=t."Id"::text
            WHERE d."SourceSystem"='transfer.dispatch.v1' AND (t."Id"=CASE WHEN TG_OP='INSERT' THEN NEW."TransferId" ELSE OLD."TransferId" END
              OR TG_OP='UPDATE' AND t."Id"=NEW."TransferId")) THEN
            RAISE EXCEPTION 'Dispatched transfer lines are immutable' USING ERRCODE='P4F01'; END IF;
          IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
        END $$;
        CREATE FUNCTION public.erp_w4_transfer_header_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
        BEGIN PERFORM public.erp_w4_transfer_complete(NEW."Id"); RETURN NULL; END $$;
        CREATE FUNCTION public.erp_w4_transfer_truncate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
        BEGIN
          IF EXISTS(SELECT 1 FROM pg_trigger WHERE tgrelid='public.product_stock'::regclass AND NOT tgisinternal AND tgfoid='public.erp_w4_stock_before()'::regprocedure) THEN
            RAISE EXCEPTION 'A fenced inventory scope prevents transfer history truncation' USING ERRCODE='P4F01'; END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER erp_w4_transfer_header_before BEFORE UPDATE OR DELETE ON public.stock_transfers FOR EACH ROW EXECUTE FUNCTION public.erp_w4_transfer_header_before();
        CREATE TRIGGER erp_w4_transfer_items_before BEFORE INSERT OR UPDATE OR DELETE ON public.stock_transfer_items FOR EACH ROW EXECUTE FUNCTION public.erp_w4_transfer_items_before();
        CREATE CONSTRAINT TRIGGER erp_w4_transfer_header_complete AFTER INSERT OR UPDATE ON public.stock_transfers DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.erp_w4_transfer_header_complete();
        CREATE TRIGGER erp_w4_transfer_truncate BEFORE TRUNCATE ON public.stock_transfers FOR EACH STATEMENT EXECUTE FUNCTION public.erp_w4_transfer_truncate();
        CREATE TRIGGER erp_w4_transfer_items_truncate BEFORE TRUNCATE ON public.stock_transfer_items FOR EACH STATEMENT EXECUTE FUNCTION public.erp_w4_transfer_truncate();
        CREATE FUNCTION public.erp_w4_transfers_ready(tenant uuid, location uuid) RETURNS boolean LANGUAGE sql VOLATILE SET search_path=pg_catalog,public AS $$
          SELECT public.erp_w4_scope_ready(tenant,location) AND
            (SELECT count(*)=5 FROM pg_trigger WHERE NOT tgisinternal AND tgenabled='O' AND
              (tgrelid='public.stock_transfers'::regclass AND (
                 tgname='erp_w4_transfer_header_before' AND tgtype=27 AND tgfoid='public.erp_w4_transfer_header_before()'::regprocedure
                 OR tgname='erp_w4_transfer_header_complete' AND tgtype=21 AND tgdeferrable AND tginitdeferred AND tgfoid='public.erp_w4_transfer_header_complete()'::regprocedure
                 OR tgname='erp_w4_transfer_truncate' AND tgtype=34 AND tgfoid='public.erp_w4_transfer_truncate()'::regprocedure)
               OR tgrelid='public.stock_transfer_items'::regclass AND (
                 tgname='erp_w4_transfer_items_before' AND tgtype=31 AND tgfoid='public.erp_w4_transfer_items_before()'::regprocedure
                 OR tgname='erp_w4_transfer_items_truncate' AND tgtype=34 AND tgfoid='public.erp_w4_transfer_truncate()'::regprocedure)))
        $$;
        """;
}
