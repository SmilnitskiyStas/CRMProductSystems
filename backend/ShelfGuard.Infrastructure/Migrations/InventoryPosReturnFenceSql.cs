namespace ShelfGuard.Infrastructure.Migrations;
internal static class InventoryPosReturnFenceSql
{
    internal static readonly string Upgrade=Build();
    private static string Build()
    {
        var sql=InventoryPosFenceSql.Upgrade;
        var start=sql.IndexOf("CREATE OR REPLACE FUNCTION public.erp_w4_stock_before",StringComparison.Ordinal);
        var middle=sql.IndexOf("CREATE OR REPLACE FUNCTION public.erp_w4_stock_complete",start,StringComparison.Ordinal);
        var end=sql.IndexOf("CREATE FUNCTION public.erp_w4_pos_header_before",middle,StringComparison.Ordinal);
        var before=sql[start..middle];var complete=sql[middle..end];
        before=before.Insert(before.IndexOf("IF public.erp_w4_pos_stage",StringComparison.Ordinal),"IF public.erp_w4_pos_return_stage(NEW,OLD.\"Quantity\",false) THEN RETURN NEW; END IF;\n");
        complete=complete.Insert(complete.IndexOf("IF public.erp_w4_pos_stage",StringComparison.Ordinal),"IF public.erp_w4_pos_return_stage(NEW,OLD.\"Quantity\",true) THEN PERFORM public.erp_w4_pos_return_complete(posting); RETURN NULL; END IF;\n");
        var checkStart=sql.IndexOf("CREATE FUNCTION public.erp_w4_pos_complete",StringComparison.Ordinal);
        var checkEnd=sql.IndexOf("END $$;",checkStart,StringComparison.Ordinal)+"END $$;".Length;
        var saleCheck=sql[checkStart..checkEnd].Replace("CREATE FUNCTION", "CREATE OR REPLACE FUNCTION",StringComparison.Ordinal)
            .Replace("d.\"Status\"<>'posted'", "(d.\"Status\"<>'posted' AND (verify_stock OR d.\"Status\"<>'reversed'))",StringComparison.Ordinal);
        return Helpers+before+complete+saleCheck+Triggers;
    }
    private const string Helpers="""
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
        END $$;
        """;
    private const string Triggers="""
        CREATE FUNCTION public.erp_w4_pos_return_job_before() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
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
        """;
}
