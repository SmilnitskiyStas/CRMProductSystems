namespace ShelfGuard.Infrastructure.Migrations;
internal static class InventoryPosFenceSql
{
    internal static readonly string Upgrade = Build();
    private static string Build()
    {
        var sql = InventoryWriteOffFenceSql.Upgrade;
        var start = sql.IndexOf("CREATE OR REPLACE FUNCTION public.erp_w4_stock_before", StringComparison.Ordinal);
        var middle = sql.IndexOf("CREATE OR REPLACE FUNCTION public.erp_w4_stock_complete", start, StringComparison.Ordinal);
        var end = sql.IndexOf("CREATE FUNCTION public.erp_w4_writeoff_header_before", middle, StringComparison.Ordinal);
        var before = sql[start..middle]; var complete = sql[middle..end];
        before = before.Insert(before.IndexOf("IF public.erp_w4_writeoff_stage(NEW,OLD.", StringComparison.Ordinal), "IF public.erp_w4_pos_stage(NEW,OLD.\"Quantity\",false) THEN RETURN NEW; END IF;\n          ");
        complete = complete.Insert(complete.IndexOf("IF public.erp_w4_writeoff_stage(NEW,OLD.", StringComparison.Ordinal), "IF public.erp_w4_pos_stage(NEW,OLD.\"Quantity\",true) THEN\n            IF NOT EXISTS(SELECT 1 FROM public.product_stock s WHERE s.\"Id\"=NEW.\"Id\" AND to_jsonb(s)=to_jsonb(NEW)) THEN RAISE EXCEPTION 'POS batch changed before commit' USING ERRCODE='P4F01'; END IF;\n            PERFORM public.erp_w4_pos_complete((SELECT d.\"SourceId\"::uuid FROM public.document_postings p JOIN public.business_documents d ON d.\"TenantId\"=p.\"TenantId\" AND d.\"Id\"=p.\"DocumentId\" WHERE p.\"Id\"=posting),true); RETURN NULL; END IF;\n          ");
        return Helpers + before + complete + Triggers;
    }
    private const string Helpers = """
        CREATE FUNCTION public.erp_w4_pos_snapshot(sale_id uuid) RETURNS jsonb LANGUAGE sql STABLE SET search_path=pg_catalog,public AS $$
          SELECT jsonb_build_object('header',to_jsonb(t)-ARRAY['FiscalNumber','Status','RetryCount'],
            'items',coalesce((SELECT jsonb_agg(to_jsonb(i) ORDER BY i."Id") FROM public.pos_transaction_items i WHERE i."TransactionId"=t."Id"),'[]'::jsonb))
          FROM public.pos_transactions t WHERE t."Id"=sale_id
        $$;
        CREATE FUNCTION public.erp_w4_pos_stage(s public.product_stock,before_qty numeric,completed boolean) RETURNS boolean LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
        DECLARE marker uuid; phase text;
        BEGIN
          BEGIN marker:=NULLIF(current_setting('app.erp_posting_id',true),'')::uuid; EXCEPTION WHEN invalid_text_representation THEN marker:=NULL; END;
          SELECT d."SourceSystem" INTO phase FROM public.document_postings p JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId" WHERE p."TenantId"=s."TenantId" AND p."Id"=marker;
          IF phase IS DISTINCT FROM 'pos.sale.v1' THEN RETURN false; END IF;
          IF NOT EXISTS(SELECT 1 FROM public.document_postings p JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId"
            JOIN public.pos_transactions t ON t."TenantId"=p."TenantId" AND t."Id"::text=d."SourceId"
            JOIN public.document_lines l ON l."TenantId"=p."TenantId" AND l."DocumentId"=d."Id"
            JOIN public.inventory_ledger_entries e ON e."TenantId"=p."TenantId" AND e."PostingId"=p."Id" AND e."DocumentLineId"=l."Id"
            JOIN public.document_approvals a ON a."TenantId"=p."TenantId" AND a."DocumentId"=d."Id" AND a."Stage"=1 AND a."Decision"='approved'
            WHERE p."TenantId"=s."TenantId" AND p."Id"=marker AND d."DocumentType"='pos_sale' AND d."Status"=CASE WHEN completed THEN 'posted' ELSE 'approved' END
              AND t."CashierId"=p."PerformedBy" AND p."PerformedBy"=d."CreatedBy" AND p."PerformedBy"=NULLIF(current_setting('app.user_id',true),'')::uuid
              AND a."ApproverId"=p."PerformedBy" AND a."ContentHash"=d."ContentHash" AND t."LocationId"=s."LocationId"
              AND l."Metadata"->>'saleId'=t."Id"::text AND l."Metadata"->>'batchId'=s."Id"::text AND l."ItemId"=s."ProductId" AND l."Quantity">0
              AND l."FromLocationId"=s."LocationId" AND l."ToLocationId" IS NULL AND l."BatchNumberSnapshot" IS NOT DISTINCT FROM s."BatchNumber" AND l."ExpiryDateSnapshot"=s."ExpiryDate"
              AND e."BatchId"=s."Id" AND e."ItemId"=s."ProductId" AND e."LocationId"=s."LocationId" AND e."ZoneId" IS NOT DISTINCT FROM s."ZoneId"
              AND e."QuantityBefore"=before_qty AND e."QuantityAfter"=s."Quantity" AND e."QuantityDelta"=-l."Quantity" AND e."UnitCost" IS NOT DISTINCT FROM l."UnitCost") THEN
            RAISE EXCEPTION 'POS stock requires its staged ERP sale' USING ERRCODE='P4F01'; END IF;
          RETURN true;
        END $$;
        CREATE FUNCTION public.erp_w4_pos_complete(sale_id uuid,verify_stock boolean) RETURNS void LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
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
          IF p."Id" IS NULL OR d."Status"<>'posted' OR d."DocumentType"<>'pos_sale' OR d."CreatedBy" IS DISTINCT FROM t."CashierId" OR p."PerformedBy" IS DISTINCT FROM t."CashierId"
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
        END $$;
        """;
    private const string Triggers = """
        CREATE FUNCTION public.erp_w4_pos_header_before() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
        BEGIN
          IF EXISTS(SELECT 1 FROM public.business_documents d WHERE d."TenantId"=OLD."TenantId" AND d."SourceSystem"='pos.sale.v1' AND d."SourceId"=OLD."Id"::text) THEN
            IF TG_OP='DELETE' THEN RAISE EXCEPTION 'ERP POS source is immutable' USING ERRCODE='P4F01'; END IF;
            IF (to_jsonb(NEW)-ARRAY['FiscalNumber','Status','RetryCount']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['FiscalNumber','Status','RetryCount'])
              OR NEW."Status" NOT IN ('pending_fiscalization','fiscalized','fiscalization_failed') THEN
              RAISE EXCEPTION 'ERP POS source permits only fiscal metadata updates' USING ERRCODE='P4F01'; END IF;
          END IF;
          IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
        END $$;
        CREATE FUNCTION public.erp_w4_pos_items_before() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
        BEGIN
          IF TG_OP<>'INSERT' THEN PERFORM 1 FROM public.pos_transactions WHERE "Id"=OLD."TransactionId" FOR UPDATE; END IF;
          IF TG_OP<>'DELETE' THEN PERFORM 1 FROM public.pos_transactions WHERE "Id"=NEW."TransactionId" FOR UPDATE; END IF;
          IF TG_OP='UPDATE' AND to_jsonb(NEW)=to_jsonb(OLD) THEN RETURN NEW; END IF;
          IF EXISTS(SELECT 1 FROM public.business_documents d WHERE d."SourceSystem"='pos.sale.v1' AND
            (d."SourceId"=CASE WHEN TG_OP='INSERT' THEN NEW."TransactionId" ELSE OLD."TransactionId" END::text OR TG_OP='UPDATE' AND d."SourceId"=NEW."TransactionId"::text)) THEN
            RAISE EXCEPTION 'ERP POS receipt lines are immutable' USING ERRCODE='P4F01'; END IF;
          IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
        END $$;
        CREATE FUNCTION public.erp_w4_pos_event_before() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
        DECLARE source_id text; guarded boolean;
        BEGIN
          IF TG_OP<>'INSERT' THEN
            source_id:=OLD."Meta"->>'tx_id';
            PERFORM 1 FROM public.pos_transactions WHERE "Id"::text=source_id FOR UPDATE;
            IF EXISTS(SELECT 1 FROM public.business_documents d WHERE d."TenantId"=OLD."TenantId" AND d."SourceSystem"='pos.sale.v1' AND d."SourceId"=source_id) THEN
              RAISE EXCEPTION 'ERP POS stock events are immutable' USING ERRCODE='P4F01'; END IF;
          END IF;
          IF TG_OP<>'DELETE' THEN
            source_id:=NEW."Meta"->>'tx_id';
            PERFORM 1 FROM public.pos_transactions WHERE "Id"::text=source_id FOR UPDATE;
            SELECT EXISTS(SELECT 1 FROM public.business_documents d WHERE d."TenantId"=NEW."TenantId" AND d."SourceSystem"='pos.sale.v1' AND d."SourceId"=source_id) INTO guarded;
            IF guarded AND NOT EXISTS(SELECT 1 FROM public.document_postings p JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId"
              WHERE p."TenantId"=NEW."TenantId" AND p."Id"::text=current_setting('app.erp_posting_id',true) AND d."Status"='approved' AND d."SourceSystem"='pos.sale.v1' AND d."SourceId"=source_id) THEN
              RAISE EXCEPTION 'New POS event requires its staged posting' USING ERRCODE='P4F01'; END IF;
          END IF;
          IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
        END $$;
        CREATE FUNCTION public.erp_w4_pos_header_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
        BEGIN PERFORM public.erp_w4_pos_complete(NEW."Id",TG_OP='INSERT'); RETURN NULL; END $$;
        CREATE TRIGGER erp_w4_pos_header_before BEFORE UPDATE OR DELETE ON public.pos_transactions FOR EACH ROW EXECUTE FUNCTION public.erp_w4_pos_header_before();
        CREATE TRIGGER erp_w4_pos_items_before BEFORE INSERT OR UPDATE OR DELETE ON public.pos_transaction_items FOR EACH ROW EXECUTE FUNCTION public.erp_w4_pos_items_before();
        CREATE TRIGGER erp_w4_pos_event_before BEFORE INSERT OR UPDATE OR DELETE ON public.stock_events FOR EACH ROW EXECUTE FUNCTION public.erp_w4_pos_event_before();
        CREATE CONSTRAINT TRIGGER erp_w4_pos_header_complete AFTER INSERT OR UPDATE ON public.pos_transactions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.erp_w4_pos_header_complete();
        CREATE TRIGGER erp_w4_pos_truncate BEFORE TRUNCATE ON public.pos_transactions FOR EACH STATEMENT EXECUTE FUNCTION public.erp_w4_transfer_truncate();
        CREATE TRIGGER erp_w4_pos_items_truncate BEFORE TRUNCATE ON public.pos_transaction_items FOR EACH STATEMENT EXECUTE FUNCTION public.erp_w4_transfer_truncate();
        CREATE TRIGGER erp_w4_pos_events_truncate BEFORE TRUNCATE ON public.stock_events FOR EACH STATEMENT EXECUTE FUNCTION public.erp_w4_transfer_truncate();
        CREATE FUNCTION public.erp_w4_pos_ready(tenant uuid,location uuid) RETURNS boolean LANGUAGE sql VOLATILE SET search_path=pg_catalog,public AS $$
          SELECT public.erp_w4_writeoffs_ready(tenant,location) AND (SELECT count(*)=7 FROM pg_trigger WHERE NOT tgisinternal AND tgenabled='O' AND
            (tgrelid='public.pos_transactions'::regclass AND (
              tgname='erp_w4_pos_header_before' AND tgtype=27 AND tgfoid='public.erp_w4_pos_header_before()'::regprocedure
              OR tgname='erp_w4_pos_header_complete' AND tgtype=21 AND tgdeferrable AND tginitdeferred AND tgfoid='public.erp_w4_pos_header_complete()'::regprocedure
              OR tgname='erp_w4_pos_truncate' AND tgtype=34 AND tgfoid='public.erp_w4_transfer_truncate()'::regprocedure)
            OR tgrelid='public.pos_transaction_items'::regclass AND (
              tgname='erp_w4_pos_items_before' AND tgtype=31 AND tgfoid='public.erp_w4_pos_items_before()'::regprocedure
              OR tgname='erp_w4_pos_items_truncate' AND tgtype=34 AND tgfoid='public.erp_w4_transfer_truncate()'::regprocedure)
            OR tgrelid='public.stock_events'::regclass AND (
              tgname='erp_w4_pos_event_before' AND tgtype=31 AND tgfoid='public.erp_w4_pos_event_before()'::regprocedure
              OR tgname='erp_w4_pos_events_truncate' AND tgtype=34 AND tgfoid='public.erp_w4_transfer_truncate()'::regprocedure)))
        $$;
        """;
}
