namespace ShelfGuard.Infrastructure.Migrations;

/// <summary>Extends the existing per-scope triggers in place; old adjustment/deletion guards remain byte-for-byte.</summary>
internal static class InventoryReceiptFenceSql
{
    internal static readonly string Upgrade = Build();
    private static string Function(string from, string to) => InventoryPostingFenceSql.Create[
        InventoryPostingFenceSql.Create.IndexOf("CREATE FUNCTION public." + from, StringComparison.Ordinal)..
        InventoryPostingFenceSql.Create.IndexOf(to, InventoryPostingFenceSql.Create.IndexOf("CREATE FUNCTION public." + from, StringComparison.Ordinal), StringComparison.Ordinal)]
        .Replace("CREATE FUNCTION", "CREATE OR REPLACE FUNCTION", StringComparison.Ordinal);
    private static string Build()
    {
        var before = Function("erp_w4_stock_before", "CREATE FUNCTION public.erp_w4_stock_complete");
        const string reject = "RAISE EXCEPTION 'Inventory scope requires ERP posting; batch creation is not enabled' USING ERRCODE='P4F01';";
        if (!before.Contains(reject)) throw new InvalidOperationException("Unexpected predecessor inventory guard.");
        before = before.Replace(reject, "PERFORM public.erp_w4_receipt_insert_check(NEW);", StringComparison.Ordinal);
        var complete = Function("erp_w4_stock_complete", "CREATE FUNCTION public.erp_w4_stock_truncate");
        complete = complete.Insert(complete.IndexOf("BEGIN", StringComparison.Ordinal) + 5, """

          IF TG_OP='INSERT' THEN
            IF NEW."TenantId"=tenant AND NEW."LocationId"=location THEN PERFORM public.erp_w4_receipt_complete_check(NEW); END IF;
            RETURN NULL;
          END IF;

        """);
        var ready = Function("erp_w4_scope_ready", "CREATE FUNCTION public.erp_w4_registry_complete")
            .Replace("a.tgtype=17", "a.tgtype=21", StringComparison.Ordinal);
        var activation = Function("erp_w4_activate_scope", "REVOKE ALL ON FUNCTION public.erp_w4_activate_scope")
            .Replace("AFTER UPDATE ON public.product_stock", "AFTER INSERT OR UPDATE ON public.product_stock", StringComparison.Ordinal);
        return Helpers + before + complete + ready + activation + """

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
          """;
    }

    private const string Helpers = """
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
        END $$;
        """;
}
