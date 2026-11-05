START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027173037_AddErpProductionPosting') THEN
    CREATE FUNCTION public.erp_w4_production_snapshot(order_id uuid) RETURNS jsonb LANGUAGE sql STABLE SET search_path=pg_catalog,public AS $$
      SELECT jsonb_build_object('order',to_jsonb(o)-ARRAY['Status','CompletedAt','UpdatedAt'],'recipe',to_jsonb(r),
        'ingredients',(SELECT jsonb_agg(to_jsonb(i) ORDER BY i."Id") FROM public.recipe_ingredients i WHERE i."RecipeId"=r."Id"),
        'items',(SELECT jsonb_agg(jsonb_build_object('Id',i."Id",'Name',i."Name",'Unit',i."Unit",'IsActive',i."IsActive",'ShelfLifeDays',i."ShelfLifeDays",'Barcodes',i."Barcodes") ORDER BY i."Id") FROM public.items i WHERE i."TenantId"=o."TenantId" AND (i."Id"=r."OutputItemId" OR i."Id" IN(SELECT "ItemId" FROM public.recipe_ingredients WHERE "RecipeId"=r."Id"))),
        'links',coalesce((SELECT jsonb_agg(to_jsonb(l) ORDER BY l."Id") FROM public.production_order_request_lines l WHERE l."TenantId"=o."TenantId" AND l."ProductionOrderId"=o."Id"),'[]'::jsonb),
        'requestLines',coalesce((SELECT jsonb_agg(to_jsonb(rl) ORDER BY rl."Id") FROM public.production_request_lines rl WHERE rl."TenantId"=o."TenantId" AND rl."Id" IN(SELECT l."RequestLineId" FROM public.production_order_request_lines l WHERE l."TenantId"=o."TenantId" AND l."ProductionOrderId"=o."Id")),'[]'::jsonb),
        'requests',coalesce((SELECT jsonb_agg(to_jsonb(q) ORDER BY q."Id") FROM public.production_requests q WHERE q."TenantId"=o."TenantId" AND q."Id" IN(SELECT rl."RequestId" FROM public.production_request_lines rl JOIN public.production_order_request_lines l ON l."TenantId"=rl."TenantId" AND l."RequestLineId"=rl."Id" WHERE l."ProductionOrderId"=o."Id")),'[]'::jsonb))
      FROM public.production_orders o JOIN public.recipes r ON r."TenantId"=o."TenantId" AND r."Id"=o."RecipeId" WHERE o."Id"=order_id
    $$;
    CREATE FUNCTION public.erp_w4_production_posting() RETURNS uuid LANGUAGE plpgsql STABLE SET search_path=pg_catalog,public AS $$
    DECLARE marker uuid;
    BEGIN
      BEGIN marker:=NULLIF(current_setting('app.erp_posting_id',true),'')::uuid; EXCEPTION WHEN invalid_text_representation THEN RETURN NULL; END;
      RETURN (SELECT p."Id" FROM public.document_postings p JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId" WHERE p."Id"=marker AND d."SourceSystem"='production.complete.v1');
    END $$;
    CREATE FUNCTION public.erp_w4_production_insert_stage(s public.product_stock,completed boolean) RETURNS boolean LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    DECLARE marker uuid:=public.erp_w4_production_posting();
    BEGIN
      IF marker IS NULL THEN RETURN false; END IF;
      IF NOT EXISTS(SELECT 1 FROM public.document_postings p JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId"
        JOIN public.production_orders o ON o."TenantId"=p."TenantId" AND o."Id"::text=d."SourceId"
        JOIN public.recipes r ON r."TenantId"=o."TenantId" AND r."Id"=o."RecipeId"
        JOIN public.items i ON i."TenantId"=o."TenantId" AND i."Id"=r."OutputItemId"
        JOIN public.document_lines l ON l."TenantId"=d."TenantId" AND l."DocumentId"=d."Id" AND l."Metadata"->>'role'='output'
        JOIN public.document_approvals a ON a."TenantId"=d."TenantId" AND a."DocumentId"=d."Id" AND a."Stage"=1 AND a."Decision"='approved' AND a."ContentHash"=d."ContentHash"
        WHERE p."Id"=marker AND p."TenantId"=s."TenantId" AND d."DocumentType"='production_completion' AND d."Status"=CASE WHEN completed THEN 'posted' ELSE 'approved' END
          AND p."PerformedBy"=d."CreatedBy" AND a."ApproverId"=p."PerformedBy" AND p."PerformedBy"=NULLIF(current_setting('app.user_id',true),'')::uuid
          AND o."Status"=CASE WHEN completed THEN 'Done' ELSE 'InProgress' END AND o."LocationId"=s."LocationId" AND o."StartedAt" IS NOT NULL
          AND s."SourceType"='production' AND s."SourceId"=o."Id" AND s."AddedBy"=p."PerformedBy" AND s."AddedAt"=p."PostedAt" AND s."LastCheckedAt"=p."PostedAt"
          AND s."ProductId"=r."OutputItemId" AND s."ZoneId" IS NULL AND s."Quantity">0 AND s."Quantity"=o."PlannedQty" AND s."QuantityInitial"=s."Quantity"
          AND l."Metadata"->>'batchId'=s."Id"::text AND l."ItemId"=s."ProductId" AND l."FromLocationId" IS NULL AND l."ToLocationId"=s."LocationId" AND l."Quantity"=s."Quantity" AND l."Unit"=i."Unit" AND r."Unit"=i."Unit"
          AND l."ExpiryDateSnapshot"=s."ExpiryDate" AND l."BatchNumberSnapshot" IS NOT DISTINCT FROM s."BatchNumber" AND i."IsActive" AND r."IsActive" AND i."ShelfLifeDays">0
          AND s."ExpiryDate"=(p."PostedAt" AT TIME ZONE 'UTC')::date+i."ShelfLifeDays"
          AND s."BatchNumber"='PROD-'||upper(substr(replace(o."Id"::text,'-',''),1,8))||'-'||to_char(p."PostedAt" AT TIME ZONE 'UTC','YYYYMMDD')
          AND l."Metadata"->'source'=public.erp_w4_production_snapshot(o."Id")
      ) THEN RAISE EXCEPTION 'Production output requires a staged reviewed posting' USING ERRCODE='P4F01'; END IF;
      RETURN true;
    END $$;
    CREATE FUNCTION public.erp_w4_production_update_stage(s public.product_stock,before_qty numeric,before_status text,completed boolean) RETURNS boolean LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    DECLARE marker uuid:=public.erp_w4_production_posting();
    BEGIN
      IF marker IS NULL THEN RETURN false; END IF;
      IF NOT EXISTS(SELECT 1 FROM public.document_postings p JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId"
        JOIN public.production_orders o ON o."TenantId"=p."TenantId" AND o."Id"::text=d."SourceId"
        JOIN public.document_lines l ON l."TenantId"=p."TenantId" AND l."DocumentId"=d."Id" AND l."Metadata"->>'role'='material'
        JOIN public.document_lines output ON output."TenantId"=d."TenantId" AND output."DocumentId"=d."Id" AND output."Metadata"->>'role'='output'
        JOIN public.inventory_ledger_entries e ON e."TenantId"=p."TenantId" AND e."PostingId"=p."Id" AND e."DocumentLineId"=l."Id"
        JOIN public.inventory_ledger_entries previous ON previous."TenantId"=e."TenantId" AND previous."Id"::text=l."Metadata"->>'lastLedgerEntryId' AND previous."BatchId"=e."BatchId"
        JOIN public.recipe_ingredients ri ON ri."Id"::text=l."Metadata"->>'ingredientId' AND ri."RecipeId"=o."RecipeId" AND ri."ItemId"=e."ItemId"
        JOIN public.items i ON i."TenantId"=e."TenantId" AND i."Id"=e."ItemId" AND i."IsActive"
        JOIN public.document_approvals a ON a."TenantId"=d."TenantId" AND a."DocumentId"=d."Id" AND a."Stage"=1 AND a."Decision"='approved' AND a."ContentHash"=d."ContentHash"
        WHERE p."Id"=marker AND p."TenantId"=s."TenantId" AND d."DocumentType"='production_completion' AND d."Status"=CASE WHEN completed THEN 'posted' ELSE 'approved' END
          AND p."PerformedBy"=d."CreatedBy" AND p."PerformedBy"=a."ApproverId" AND p."PerformedBy"=NULLIF(current_setting('app.user_id',true),'')::uuid
          AND o."Status"=CASE WHEN completed THEN 'Done' ELSE 'InProgress' END AND o."LocationId"=s."LocationId" AND l."Metadata"->>'batchId'=s."Id"::text AND l."Metadata"->>'beforeStatus'=before_status
          AND before_status NOT IN ('archived','sold_out','expired') AND s."ExpiryDate">(p."PostedAt" AT TIME ZONE 'UTC')::date
          AND e."BatchId"=s."Id" AND e."ItemId"=s."ProductId" AND e."LocationId"=s."LocationId" AND e."ZoneId" IS NOT DISTINCT FROM s."ZoneId"
          AND e."QuantityBefore"=before_qty AND e."QuantityAfter"=s."Quantity" AND e."QuantityDelta"=-l."Quantity" AND e."QuantityDelta"=s."Quantity"-before_qty AND l."Quantity">0
          AND l."FromLocationId"=s."LocationId" AND l."ToLocationId" IS NULL AND l."Unit"=i."Unit" AND l."Unit"=ri."Unit"
          AND l."UnitCost" IS NOT DISTINCT FROM e."UnitCost" AND e."UnitCost" IS NOT DISTINCT FROM previous."UnitCost" AND previous."QuantityAfter"=before_qty
          AND previous."ItemId"=s."ProductId" AND previous."LocationId"=s."LocationId" AND previous."Currency"='UAH'
          AND l."ExpiryDateSnapshot"=s."ExpiryDate" AND l."BatchNumberSnapshot" IS NOT DISTINCT FROM s."BatchNumber"
          AND output."Metadata"->'source'=public.erp_w4_production_snapshot(o."Id")
          AND (completed OR EXISTS(SELECT 1 FROM public.inventory_balance_projections b WHERE b."TenantId"=s."TenantId" AND b."BatchId"=s."Id" AND b."LastLedgerEntryId"=previous."Id" AND b."Quantity"=before_qty AND b."ReservedQuantity"=0))
      ) THEN RAISE EXCEPTION 'Production consumption requires the reviewed batch posting' USING ERRCODE='P4F01'; END IF;
      RETURN true;
    END $$;
    CREATE FUNCTION public.erp_w4_production_complete(order_id uuid) RETURNS void LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    DECLARE o public.production_orders; d public.business_documents; p public.document_postings; output public.document_lines; n bigint;
    BEGIN
      SELECT * INTO o FROM public.production_orders WHERE "Id"=order_id;
      SELECT * INTO d FROM public.business_documents WHERE "TenantId"=o."TenantId" AND "SourceSystem"='production.complete.v1' AND "SourceId"=order_id::text;
      SELECT * INTO p FROM public.document_postings WHERE "TenantId"=d."TenantId" AND "DocumentId"=d."Id";
      SELECT count(*) INTO n FROM public.document_lines WHERE "TenantId"=d."TenantId" AND "DocumentId"=d."Id";
      IF o."Id" IS NULL OR d."Id" IS NULL OR p."Id" IS NULL OR d."Status"<>'posted' OR d."DocumentType"<>'production_completion' OR o."Status"<>'Done' OR o."CompletedAt" IS DISTINCT FROM p."PostedAt"
        OR public.erp_w4_production_posting() IS DISTINCT FROM p."Id" OR n NOT BETWEEN 2 AND 100
        OR n<>(SELECT count(*) FROM public.inventory_ledger_entries WHERE "TenantId"=o."TenantId" AND "PostingId"=p."Id")
        OR (SELECT count(*) FROM public.document_lines WHERE "TenantId"=d."TenantId" AND "DocumentId"=d."Id" AND "Metadata"->>'role'='output')<>1
        OR (SELECT count(*) FROM public.document_lines WHERE "TenantId"=d."TenantId" AND "DocumentId"=d."Id" AND "Metadata"->>'role'='material')<>n-1
        OR NOT EXISTS(SELECT 1 FROM public.outbox_events e WHERE e."TenantId"=o."TenantId" AND e."PostingId"=p."Id" AND e."DeliveryScope"='live') THEN
        RAISE EXCEPTION 'Production order, document and live event must commit together' USING ERRCODE='P4F01'; END IF;
      SELECT * INTO output FROM public.document_lines WHERE "TenantId"=d."TenantId" AND "DocumentId"=d."Id" AND "Metadata"->>'role'='output';
      IF output."Metadata"->'source' IS DISTINCT FROM public.erp_w4_production_snapshot(order_id)
        OR (SELECT count(*) FROM public.production_order_consumptions WHERE "ProductionOrderId"=order_id)<>n-1
        OR (SELECT count(*) FROM public.product_stock WHERE "TenantId"=o."TenantId" AND "SourceType"='production' AND "SourceId"=order_id)<>1
        OR (SELECT count(*) FROM public.stock_events WHERE "TenantId"=o."TenantId" AND "Meta"->>'production_order_id'=order_id::text)<>n
        OR EXISTS(SELECT 1 FROM public.inventory_reservations r WHERE r."TenantId"=o."TenantId" AND r."LocationId"=o."LocationId" AND r."Status"='active'
          AND r."ItemId" IN(SELECT "ItemId" FROM public.document_lines WHERE "TenantId"=d."TenantId" AND "DocumentId"=d."Id")) THEN
        RAISE EXCEPTION 'Production source, allocation or reservation changed' USING ERRCODE='P4F01'; END IF;
      IF EXISTS(SELECT 1 FROM public.inventory_ledger_entries e JOIN public.document_lines l ON l."TenantId"=e."TenantId" AND l."Id"=e."DocumentLineId"
        LEFT JOIN public.product_stock s ON s."TenantId"=e."TenantId" AND s."Id"=e."BatchId"
        LEFT JOIN public.inventory_balance_projections b ON b."TenantId"=e."TenantId" AND b."BatchId"=e."BatchId"
        WHERE e."TenantId"=o."TenantId" AND e."PostingId"=p."Id" AND (s."Id" IS NULL OR b."Id" IS NULL OR b."LastLedgerEntryId" IS DISTINCT FROM e."Id" OR b."Quantity" IS DISTINCT FROM e."QuantityAfter" OR s."Quantity" IS DISTINCT FROM e."QuantityAfter" OR b."ReservedQuantity"<>0
          OR s."Quantity" IS DISTINCT FROM(SELECT sum(x."QuantityDelta") FROM public.inventory_ledger_entries x WHERE x."TenantId"=s."TenantId" AND x."BatchId"=s."Id")
          OR NOT EXISTS(SELECT 1 FROM public.stock_events v WHERE v."TenantId"=e."TenantId" AND v."Id"::text=l."Metadata"->>'eventId' AND v."ProductStockId"=s."Id" AND v."ProductId"=s."ProductId" AND v."StoreId"=o."LocationId" AND v."QuantityDelta"=e."QuantityDelta" AND v."PerformedBy"=p."PerformedBy" AND v."CreatedAt"=p."PostedAt" AND v."Meta"->>'production_order_id'=order_id::text AND v."EventType"=CASE WHEN e."QuantityDelta">0 THEN 'production_output' ELSE 'production_consumption' END)
          OR l."Metadata"->>'role'='output' AND (e."QuantityBefore"<>0 OR e."QuantityDelta"<>s."Quantity" OR NOT public.erp_w4_production_insert_stage(s,true)
            OR e."UnitCost" IS DISTINCT FROM l."UnitCost" OR e."ValueDelta" IS DISTINCT FROM round(e."QuantityDelta"*e."UnitCost",4)
            OR NOT EXISTS(SELECT 1 FROM public.stock_movements m WHERE m."TenantId"=e."TenantId" AND m."ProductStockId"=s."Id" AND m."ReferenceId"=order_id AND m."ReferenceType"='production_output' AND m."MovementType"='receipt' AND m."QuantityBefore"=0 AND m."QuantityAfter"=e."QuantityAfter" AND m."Quantity"=e."QuantityDelta" AND m."PerformedBy"=p."PerformedBy" AND m."UnitPrice" IS NULL AND m."TotalAmount" IS NULL))
          OR l."Metadata"->>'role'='material' AND (NOT public.erp_w4_production_update_stage(s,e."QuantityBefore",l."Metadata"->>'beforeStatus',true)
            OR e."ValueDelta" IS DISTINCT FROM round(e."QuantityDelta"*e."UnitCost",4)
            OR NOT EXISTS(SELECT 1 FROM public.production_order_consumptions c WHERE c."Id"::text=l."Metadata"->>'consumptionId' AND c."ProductionOrderId"=order_id AND c."ItemId"=e."ItemId" AND c."ProductStockId"=s."Id" AND c."QtyConsumed"=-e."QuantityDelta" AND c."ConsumedAt"=p."PostedAt")
            OR NOT EXISTS(SELECT 1 FROM public.stock_movements m WHERE m."TenantId"=e."TenantId" AND m."ProductStockId"=s."Id" AND m."ReferenceId"::text=l."Metadata"->>'eventId' AND m."ReferenceType"='production_consumption_event' AND m."MovementType"='write_off' AND m."QuantityBefore"=e."QuantityBefore" AND m."QuantityAfter"=e."QuantityAfter" AND m."Quantity"=-e."QuantityDelta" AND m."PerformedBy"=p."PerformedBy" AND m."UnitPrice" IS NULL AND m."TotalAmount" IS NULL)))) THEN
        RAISE EXCEPTION 'Production entries, source events, movements and balances disagree' USING ERRCODE='P4F01'; END IF;
      IF EXISTS(SELECT 1 FROM public.recipe_ingredients ri JOIN public.recipes r ON r."Id"=ri."RecipeId" WHERE ri."RecipeId"=o."RecipeId" AND (ri."Qty"<=0 OR r."OutputQty"<=0 OR ri."ItemId"=r."OutputItemId"
        OR (SELECT sum(-e."QuantityDelta") FROM public.inventory_ledger_entries e WHERE e."TenantId"=o."TenantId" AND e."PostingId"=p."Id" AND e."ItemId"=ri."ItemId" AND e."QuantityDelta"<0) IS DISTINCT FROM ri."Qty"*o."PlannedQty"/r."OutputQty"))
        OR EXISTS(SELECT "ItemId" FROM public.recipe_ingredients WHERE "RecipeId"=o."RecipeId" GROUP BY "ItemId" HAVING count(*)>1)
        OR (SELECT count(DISTINCT l."ItemId") FROM public.document_lines l WHERE l."TenantId"=o."TenantId" AND l."DocumentId"=d."Id" AND l."Metadata"->>'role'='material')<>(SELECT count(*) FROM public.recipe_ingredients WHERE "RecipeId"=o."RecipeId") THEN
        RAISE EXCEPTION 'Production recipe ratios disagree with physical issue' USING ERRCODE='P4F01'; END IF;
      IF EXISTS(WITH candidates AS(
        SELECT s."Id",s."ProductId",s."ExpiryDate",s."AddedAt",coalesce(e."QuantityBefore",s."Quantity") before_qty,coalesce(-e."QuantityDelta",0) taken,
          ri."Qty"*o."PlannedQty"/r."OutputQty" required
        FROM public.product_stock s JOIN public.recipe_ingredients ri ON ri."ItemId"=s."ProductId" AND ri."RecipeId"=o."RecipeId" JOIN public.recipes r ON r."Id"=ri."RecipeId"
        LEFT JOIN public.inventory_ledger_entries e ON e."TenantId"=s."TenantId" AND e."PostingId"=p."Id" AND e."BatchId"=s."Id"
        LEFT JOIN public.document_lines l ON l."TenantId"=e."TenantId" AND l."Id"=e."DocumentLineId"
        WHERE s."TenantId"=o."TenantId" AND s."LocationId"=o."LocationId" AND(s."Quantity">0 OR e."Id" IS NOT NULL)
          AND s."ExpiryDate">(p."PostedAt" AT TIME ZONE 'UTC')::date AND coalesce(l."Metadata"->>'beforeStatus',s."Status") NOT IN('archived','sold_out','expired')
      ),expected AS(SELECT *,coalesce(sum(before_qty) OVER(PARTITION BY "ProductId" ORDER BY "ExpiryDate","AddedAt","Id" ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),0) prior FROM candidates)
      SELECT 1 FROM expected WHERE taken<>greatest(least(before_qty,required-prior),0)) THEN RAISE EXCEPTION 'Production must use the complete current FEFO take' USING ERRCODE='P4F01'; END IF;
      IF EXISTS(SELECT 1 FROM public.document_lines WHERE "TenantId"=d."TenantId" AND "DocumentId"=d."Id" AND "Metadata"->>'role'='material' AND "UnitCost" IS NULL) THEN
        IF output."UnitCost" IS NOT NULL THEN RAISE EXCEPTION 'Unknown production cost must stay unknown' USING ERRCODE='P4F01'; END IF;
      ELSIF output."UnitCost" IS DISTINCT FROM round((SELECT sum(-"ValueDelta") FROM public.inventory_ledger_entries WHERE "TenantId"=o."TenantId" AND "PostingId"=p."Id" AND "QuantityDelta"<0)/o."PlannedQty",4)
        OR round(output."Quantity"*output."UnitCost",4) IS DISTINCT FROM(SELECT sum(-"ValueDelta") FROM public.inventory_ledger_entries WHERE "TenantId"=o."TenantId" AND "PostingId"=p."Id" AND "QuantityDelta"<0) THEN
        RAISE EXCEPTION 'Production valuation requires an explicit variance policy' USING ERRCODE='P4F01'; END IF;
      IF EXISTS(SELECT 1 FROM public.production_order_request_lines l
        LEFT JOIN public.production_request_lines rl ON rl."TenantId"=l."TenantId" AND rl."Id"=l."RequestLineId"
        LEFT JOIN public.production_requests q ON q."TenantId"=rl."TenantId" AND q."Id"=rl."RequestId"
        WHERE l."TenantId"=o."TenantId" AND l."ProductionOrderId"=order_id AND
          (rl."Id" IS NULL OR q."Id" IS NULL OR q."ProductionLocationId"<>o."LocationId" OR q."Status" NOT IN('accepted','partial') OR rl."ItemId"<>output."ItemId" OR l."Qty"<=0 OR coalesce(rl."AcceptedQty",0)<=0
           OR (SELECT sum(x."Qty") FROM public.production_order_request_lines x JOIN public.production_orders xo ON xo."TenantId"=x."TenantId" AND xo."Id"=x."ProductionOrderId" WHERE x."TenantId"=l."TenantId" AND x."RequestLineId"=l."RequestLineId" AND xo."Status"<>'Cancelled')>rl."AcceptedQty"))
        OR (SELECT coalesce(sum("Qty"),0) FROM public.production_order_request_lines WHERE "TenantId"=o."TenantId" AND "ProductionOrderId"=order_id)>o."PlannedQty" THEN
        RAISE EXCEPTION 'Production request allocation is invalid' USING ERRCODE='P4F01'; END IF;
      IF EXISTS(SELECT 1 FROM public.production_requests q WHERE q."TenantId"=o."TenantId" AND q."Status" IN('accepted','partial')
        AND q."Id" IN(SELECT rl."RequestId" FROM public.production_request_lines rl JOIN public.production_order_request_lines l ON l."TenantId"=rl."TenantId" AND l."RequestLineId"=rl."Id" WHERE l."TenantId"=o."TenantId" AND l."ProductionOrderId"=order_id)
        AND EXISTS(SELECT 1 FROM public.production_request_lines rl WHERE rl."TenantId"=q."TenantId" AND rl."RequestId"=q."Id" AND rl."AcceptedQty">0)
        AND NOT EXISTS(SELECT 1 FROM public.production_request_lines rl WHERE rl."TenantId"=q."TenantId" AND rl."RequestId"=q."Id" AND rl."AcceptedQty">0 AND rl."AcceptedQty">coalesce((SELECT sum(l."Qty") FROM public.production_order_request_lines l JOIN public.production_orders x ON x."TenantId"=l."TenantId" AND x."Id"=l."ProductionOrderId" WHERE l."TenantId"=rl."TenantId" AND l."RequestLineId"=rl."Id" AND x."Status"<>'Cancelled'),0))
        AND NOT EXISTS(SELECT 1 FROM public.production_request_lines rl JOIN public.production_order_request_lines l ON l."TenantId"=rl."TenantId" AND l."RequestLineId"=rl."Id" JOIN public.production_orders x ON x."TenantId"=l."TenantId" AND x."Id"=l."ProductionOrderId" WHERE rl."TenantId"=q."TenantId" AND rl."RequestId"=q."Id" AND x."Status" NOT IN('Done','Cancelled'))
        AND NOT EXISTS(SELECT 1 FROM public.notification_queue v WHERE v."TenantId"=q."TenantId" AND v."LocationId"=q."StoreLocationId" AND v."EventType"='production_request.ready' AND v."Payload"->>'requestId'=q."Id"::text AND v."Payload"->>'productionLocationId'=q."ProductionLocationId"::text)) THEN
        RAISE EXCEPTION 'Ready production request notification must commit with output' USING ERRCODE='P4F01'; END IF;
    END $$;CREATE OR REPLACE FUNCTION public.erp_w4_stock_before() RETURNS trigger LANGUAGE plpgsql
      SET search_path=pg_catalog,public AS $$
    DECLARE tenant uuid:=TG_ARGV[0]::uuid; location uuid:=TG_ARGV[1]::uuid; posting uuid;
    BEGIN
      IF TG_OP='INSERT' THEN
     IF NEW."TenantId"=tenant AND NEW."LocationId"=location AND public.erp_w4_production_insert_stage(NEW,false) THEN RETURN NEW; END IF;

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
      IF public.erp_w4_production_update_stage(NEW,OLD."Quantity",OLD."Status",false) THEN RETURN NEW; END IF;
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
     IF NEW."TenantId"=tenant AND NEW."LocationId"=location AND public.erp_w4_production_insert_stage(NEW,true) THEN PERFORM public.erp_w4_production_complete(NEW."SourceId"); RETURN NULL; END IF;

        IF NEW."TenantId"=tenant AND NEW."LocationId"=location THEN IF NEW."SourceType"='transfer' THEN IF NOT EXISTS(SELECT 1 FROM public.product_stock s WHERE s."Id"=NEW."Id" AND to_jsonb(s)=to_jsonb(NEW)) THEN RAISE EXCEPTION 'Transfer batch changed before commit' USING ERRCODE='P4F01'; END IF; PERFORM public.erp_w4_transfer_complete(NEW."SourceId"); ELSE PERFORM public.erp_w4_receipt_complete_check(NEW); END IF; END IF;
        RETURN NULL;
      END IF;

      IF NEW."TenantId"<>tenant OR NEW."LocationId"<>location OR NEW."Quantity" IS NOT DISTINCT FROM OLD."Quantity" THEN RETURN NULL; END IF;
      BEGIN posting:=NULLIF(current_setting('app.erp_posting_id',true),'')::uuid;
      EXCEPTION WHEN invalid_text_representation THEN posting:=NULL; END;
      IF public.erp_w4_production_update_stage(NEW,OLD."Quantity",OLD."Status",true) THEN IF NOT EXISTS(SELECT 1 FROM public.product_stock s WHERE s."Id"=NEW."Id" AND to_jsonb(s)=to_jsonb(NEW)) THEN RAISE EXCEPTION 'Production batch changed before commit' USING ERRCODE='P4F01'; END IF; PERFORM public.erp_w4_production_complete((SELECT d."SourceId"::uuid FROM public.document_postings p JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId" WHERE p."Id"=posting)); RETURN NULL; END IF;
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

    CREATE FUNCTION public.erp_w4_production_order_before() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    BEGIN
      IF EXISTS(SELECT 1 FROM public.business_documents d WHERE d."TenantId"=OLD."TenantId" AND d."SourceSystem"='production.complete.v1' AND d."SourceId"=OLD."Id"::text AND d."Status"='posted') THEN
        IF TG_OP='DELETE' OR to_jsonb(NEW) IS DISTINCT FROM to_jsonb(OLD) THEN RAISE EXCEPTION 'ERP completed production order is immutable' USING ERRCODE='P4F01'; END IF;
      END IF;
      IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
    END $$;
    CREATE FUNCTION public.erp_w4_production_order_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    BEGIN
      IF NEW."Status"='Done' AND(TG_OP='INSERT' OR OLD."Status" IS DISTINCT FROM 'Done') AND EXISTS(SELECT 1 FROM public.inventory_posting_scopes WHERE "TenantId"=NEW."TenantId" AND "LocationId"=NEW."LocationId") THEN PERFORM public.erp_w4_production_complete(NEW."Id"); END IF;
      RETURN NULL;
    END $$;
    CREATE FUNCTION public.erp_w4_production_child_before() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    DECLARE order_id uuid;
    BEGIN
      IF TG_OP<>'INSERT' THEN
        order_id:=OLD."ProductionOrderId";PERFORM 1 FROM public.production_orders WHERE "Id"=order_id FOR UPDATE;
        IF EXISTS(SELECT 1 FROM public.business_documents WHERE "SourceSystem"='production.complete.v1' AND "SourceId"=order_id::text) THEN RAISE EXCEPTION 'Production source allocations are immutable' USING ERRCODE='P4F01'; END IF;
      END IF;
      IF TG_OP<>'DELETE' THEN
        order_id:=NEW."ProductionOrderId";PERFORM 1 FROM public.production_orders WHERE "Id"=order_id FOR UPDATE;
        IF EXISTS(SELECT 1 FROM public.business_documents WHERE "SourceSystem"='production.complete.v1' AND "SourceId"=order_id::text) AND (TG_TABLE_NAME<>'production_order_consumptions' OR NOT EXISTS(SELECT 1 FROM public.business_documents d JOIN public.document_postings p ON p."TenantId"=d."TenantId" AND p."DocumentId"=d."Id" WHERE p."Id"=public.erp_w4_production_posting() AND d."SourceId"=order_id::text AND d."Status"='approved')) THEN RAISE EXCEPTION 'Production source is sealed' USING ERRCODE='P4F01'; END IF;
      END IF;
      IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
    END $$;
    CREATE FUNCTION public.erp_w4_production_event_before() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    DECLARE source_id text;
    BEGIN
      IF TG_OP<>'INSERT' THEN
        source_id:=OLD."Meta"->>'production_order_id';PERFORM 1 FROM public.production_orders WHERE "Id"::text=source_id FOR UPDATE;
        IF EXISTS(SELECT 1 FROM public.business_documents WHERE "TenantId"=OLD."TenantId" AND "SourceSystem"='production.complete.v1' AND "SourceId"=source_id) THEN RAISE EXCEPTION 'Production stock events are immutable' USING ERRCODE='P4F01'; END IF;
      END IF;
      IF TG_OP<>'DELETE' THEN
        source_id:=NEW."Meta"->>'production_order_id';PERFORM 1 FROM public.production_orders WHERE "Id"::text=source_id FOR UPDATE;
        IF NEW."EventType" IN('production_consumption','production_output') AND EXISTS(SELECT 1 FROM public.inventory_posting_scopes WHERE "TenantId"=NEW."TenantId" AND "LocationId"=NEW."StoreId")
          OR EXISTS(SELECT 1 FROM public.business_documents WHERE "TenantId"=NEW."TenantId" AND "SourceSystem"='production.complete.v1' AND "SourceId"=source_id) THEN
          IF NOT EXISTS(SELECT 1 FROM public.document_postings p JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId" WHERE p."Id"=public.erp_w4_production_posting() AND p."TenantId"=NEW."TenantId" AND d."SourceId"=source_id AND d."Status"='approved') THEN RAISE EXCEPTION 'Production event requires staged posting' USING ERRCODE='P4F01'; END IF;
        END IF;
      END IF;
      IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
    END $$;
    CREATE FUNCTION public.erp_w4_production_doc_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    BEGIN IF NEW."SourceSystem"='production.complete.v1' AND NEW."Status"='posted' AND(TG_OP='INSERT' OR OLD."Status" IS DISTINCT FROM NEW."Status") THEN PERFORM public.erp_w4_production_complete(NEW."SourceId"::uuid); END IF; RETURN NULL; END $$;
    CREATE TRIGGER erp_w4_production_order_before BEFORE UPDATE OR DELETE ON public.production_orders FOR EACH ROW EXECUTE FUNCTION public.erp_w4_production_order_before();
    CREATE CONSTRAINT TRIGGER erp_w4_production_order_complete AFTER INSERT OR UPDATE ON public.production_orders DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.erp_w4_production_order_complete();
    CREATE TRIGGER erp_w4_production_order_truncate BEFORE TRUNCATE ON public.production_orders FOR EACH STATEMENT EXECUTE FUNCTION public.erp_w4_transfer_truncate();
    CREATE TRIGGER erp_w4_production_consumptions_before BEFORE INSERT OR UPDATE OR DELETE ON public.production_order_consumptions FOR EACH ROW EXECUTE FUNCTION public.erp_w4_production_child_before();
    CREATE TRIGGER erp_w4_production_consumptions_truncate BEFORE TRUNCATE ON public.production_order_consumptions FOR EACH STATEMENT EXECUTE FUNCTION public.erp_w4_transfer_truncate();
    CREATE TRIGGER erp_w4_production_links_before BEFORE INSERT OR UPDATE OR DELETE ON public.production_order_request_lines FOR EACH ROW EXECUTE FUNCTION public.erp_w4_production_child_before();
    CREATE TRIGGER erp_w4_production_links_truncate BEFORE TRUNCATE ON public.production_order_request_lines FOR EACH STATEMENT EXECUTE FUNCTION public.erp_w4_transfer_truncate();
    CREATE TRIGGER erp_w4_production_event_before BEFORE INSERT OR UPDATE OR DELETE ON public.stock_events FOR EACH ROW EXECUTE FUNCTION public.erp_w4_production_event_before();
    CREATE CONSTRAINT TRIGGER erp_w4_production_doc_complete AFTER INSERT OR UPDATE ON public.business_documents DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.erp_w4_production_doc_complete();
    CREATE FUNCTION public.erp_w4_production_ready(tenant uuid,location uuid) RETURNS boolean LANGUAGE sql VOLATILE SET search_path=pg_catalog,public AS $$
      SELECT public.erp_w4_pos_returns_ready(tenant,location) AND (SELECT count(*)=9 FROM pg_trigger WHERE NOT tgisinternal AND tgenabled='O'
        AND tgfoid=CASE tgname WHEN 'erp_w4_production_order_before' THEN 'public.erp_w4_production_order_before()'::regprocedure
          WHEN 'erp_w4_production_order_complete' THEN 'public.erp_w4_production_order_complete()'::regprocedure
          WHEN 'erp_w4_production_order_truncate' THEN 'public.erp_w4_transfer_truncate()'::regprocedure
          WHEN 'erp_w4_production_consumptions_before' THEN 'public.erp_w4_production_child_before()'::regprocedure
          WHEN 'erp_w4_production_consumptions_truncate' THEN 'public.erp_w4_transfer_truncate()'::regprocedure
          WHEN 'erp_w4_production_links_before' THEN 'public.erp_w4_production_child_before()'::regprocedure
          WHEN 'erp_w4_production_links_truncate' THEN 'public.erp_w4_transfer_truncate()'::regprocedure
          WHEN 'erp_w4_production_event_before' THEN 'public.erp_w4_production_event_before()'::regprocedure
          WHEN 'erp_w4_production_doc_complete' THEN 'public.erp_w4_production_doc_complete()'::regprocedure END AND (
        tgrelid='public.production_orders'::regclass AND(tgname='erp_w4_production_order_before' AND tgtype=27 OR tgname='erp_w4_production_order_complete' AND tgtype=21 AND tgdeferrable AND tginitdeferred OR tgname='erp_w4_production_order_truncate' AND tgtype=34)
        OR tgrelid='public.production_order_consumptions'::regclass AND(tgname='erp_w4_production_consumptions_before' AND tgtype=31 OR tgname='erp_w4_production_consumptions_truncate' AND tgtype=34)
        OR tgrelid='public.production_order_request_lines'::regclass AND(tgname='erp_w4_production_links_before' AND tgtype=31 OR tgname='erp_w4_production_links_truncate' AND tgtype=34)
        OR tgrelid='public.stock_events'::regclass AND tgname='erp_w4_production_event_before' AND tgtype=31
        OR tgrelid='public.business_documents'::regclass AND tgname='erp_w4_production_doc_complete' AND tgtype=21 AND tgdeferrable AND tginitdeferred))
    $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027173037_AddErpProductionPosting') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261027173037_AddErpProductionPosting', '8.0.11');
    END IF;
END $EF$;
COMMIT;

