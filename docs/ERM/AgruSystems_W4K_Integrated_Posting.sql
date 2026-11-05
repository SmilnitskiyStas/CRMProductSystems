START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027193717_AddIntegratedInventoryReadiness') THEN
    ALTER TABLE public.iot_inventory_policies ADD COLUMN "AccountingMode" varchar(32) NOT NULL DEFAULT 'observation';
    ALTER TABLE public.iot_inventory_policies ADD CONSTRAINT "CK_iot_inventory_accounting_mode" CHECK("AccountingMode" IN('observation','automatic_write_down'));
    CREATE FUNCTION public.erp_w4_batch_usable(status text,quantity numeric,expiry date,today date) RETURNS boolean LANGUAGE sql IMMUTABLE AS $$
      SELECT quantity>0 AND expiry>today AND status NOT IN('expired','sold_out','archived','temp_violation')
    $$;CREATE FUNCTION public.erp_w4_quality_release_matches(old_stock public.product_stock,new_stock public.product_stock) RETURNS boolean LANGUAGE sql VOLATILE SET search_path=pg_catalog,public AS $$
      SELECT pg_has_role(current_user,(SELECT relowner FROM pg_class WHERE oid='public.product_stock'::regclass),'USAGE')
        AND (to_jsonb(old_stock)-'Status')=(to_jsonb(new_stock)-'Status')
        AND EXISTS(SELECT 1 FROM public.stock_events e WHERE e."Id"::text=current_setting('app.erp_quality_release_id',true) AND e."TenantId"=old_stock."TenantId" AND e."ProductStockId"=old_stock."Id" AND e."EventType"='quality_release' AND e."Meta"->>'beforeStatus'=old_stock."Status" AND e."Meta"->>'afterStatus'=new_stock."Status" AND(e."Meta"->>'quantity')::numeric=old_stock."Quantity")
    $$;
    CREATE FUNCTION public.erp_w4_quality_event_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    BEGIN
      IF TG_OP<>'INSERT' AND OLD."EventType"='quality_release' THEN RAISE EXCEPTION 'Retain quality decisions' USING ERRCODE='P4F01'; END IF;
      IF TG_OP<>'DELETE' AND NEW."EventType"='quality_release' THEN
        IF TG_OP<>'INSERT' OR NOT pg_has_role(current_user,(SELECT relowner FROM pg_class WHERE oid='public.product_stock'::regclass),'USAGE')
          OR NEW."Id"::text IS DISTINCT FROM current_setting('app.erp_quality_release_id',true) OR NEW."QuantityDelta" IS NOT NULL OR NEW."PerformedBy" IS NULL
          OR NOT EXISTS(SELECT 1 FROM public.product_stock s WHERE s."Id"=NEW."ProductStockId" AND s."TenantId"=NEW."TenantId" AND s."ProductId"=NEW."ProductId" AND s."LocationId"=NEW."StoreId" AND s."Status"='temp_violation') THEN RAISE EXCEPTION 'Quality release requires an owner maintenance decision' USING ERRCODE='P4F01'; END IF;
      END IF;
      IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
    END $$;
    CREATE FUNCTION public.erp_w4_quality_event_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    BEGIN
      IF NEW."EventType"='quality_release' AND NOT EXISTS(SELECT 1 FROM public.product_stock s WHERE s."Id"=NEW."ProductStockId" AND s."TenantId"=NEW."TenantId" AND s."Status"=NEW."Meta"->>'afterStatus' AND s."Quantity"=(NEW."Meta"->>'quantity')::numeric) THEN RAISE EXCEPTION 'Quality decision and status must commit together' USING ERRCODE='P4F01'; END IF;RETURN NULL;
    END $$;
    CREATE FUNCTION public.erp_w4_release_batch_hold(tenant_id uuid,batch_id uuid,actor_id uuid,expected_version text,reason text) RETURNS uuid LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    DECLARE s public.product_stock;version text;decision uuid:=gen_random_uuid();target text;today date:=(clock_timestamp() AT TIME ZONE 'UTC')::date;
    BEGIN
      IF NOT pg_has_role(current_user,(SELECT relowner FROM pg_class WHERE oid='public.product_stock'::regclass),'USAGE') THEN RAISE EXCEPTION 'Quality release requires owner maintenance' USING ERRCODE='42501'; END IF;
      PERFORM set_config('app.tenant_id',tenant_id::text,true),set_config('app.user_id',actor_id::text,true),set_config('app.role','',true),set_config('app.consumer_account_id','',true);
      SELECT * INTO s FROM public.product_stock x WHERE x."TenantId"=tenant_id AND x."Id"=batch_id FOR UPDATE;
      SELECT xmin::text INTO version FROM public.product_stock WHERE "TenantId"=tenant_id AND "Id"=batch_id;
      IF s."Id" IS NULL OR s."Status"<>'temp_violation' OR version IS DISTINCT FROM expected_version OR reason IS NULL OR length(btrim(reason)) NOT BETWEEN 15 AND 500 OR reason~'[[:cntrl:]]' OR s."ExpiryDate"<=today
        OR NOT EXISTS(SELECT 1 FROM public.users u JOIN public.tenants t ON t."Id"=u."TenantId" JOIN public.locations l ON l."TenantId"=t."Id" AND l."Id"=s."LocationId" WHERE u."Id"=actor_id AND u."TenantId"=s."TenantId" AND u."IsActive" AND t."IsActive" AND l."IsActive" AND t."Modules" @> '["inventory"]'::jsonb AND u."Role" IN('enterprise_admin','network_manager','store_manager') AND(u."Role"='enterprise_admin' OR EXISTS(SELECT 1 FROM public.user_locations ul WHERE ul."TenantId"=u."TenantId" AND ul."UserId"=u."Id" AND ul."LocationId"=l."Id"))) THEN RAISE EXCEPTION 'Review a current unexpired held batch and authorized operator' USING ERRCODE='P4F01'; END IF;
      IF NOT public.erp_w4_integrated_ready(s."TenantId",s."LocationId") THEN RAISE EXCEPTION 'Integrated scope required' USING ERRCODE='P4F01'; END IF;
      target:=CASE WHEN s."Quantity"<=0 THEN 'sold_out' WHEN s."ExpiryDate"-today<=6 THEN 'critical' WHEN s."ExpiryDate"-today<=14 THEN 'warning' WHEN clock_timestamp()-s."LastCheckedAt">interval '90 days' THEN 'needs_verification' ELSE 'safe' END;
      PERFORM set_config('app.tenant_id',s."TenantId"::text,true),set_config('app.user_id',actor_id::text,true),set_config('app.erp_quality_release_id',decision::text,true);
      INSERT INTO public.stock_events("Id","TenantId","ProductStockId","ProductId","StoreId","EventType","QuantityDelta","PerformedBy","Meta") VALUES(decision,s."TenantId",s."Id",s."ProductId",s."LocationId",'quality_release',NULL,actor_id,jsonb_build_object('beforeStatus',s."Status",'afterStatus',target,'quantity',s."Quantity",'version',version,'reason',btrim(reason)));
      UPDATE public.product_stock SET "Status"=target WHERE "Id"=s."Id" AND "TenantId"=s."TenantId";
      RETURN decision;
    END $$;
    REVOKE ALL ON FUNCTION public.erp_w4_release_batch_hold(uuid,uuid,uuid,text,text) FROM PUBLIC;
    CREATE TRIGGER erp_w4_quality_event_guard BEFORE INSERT OR UPDATE OR DELETE ON public.stock_events FOR EACH ROW EXECUTE FUNCTION public.erp_w4_quality_event_guard();
    CREATE CONSTRAINT TRIGGER erp_w4_quality_event_complete AFTER INSERT ON public.stock_events DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.erp_w4_quality_event_complete();
    CREATE TRIGGER erp_w4_quality_event_truncate BEFORE TRUNCATE ON public.stock_events FOR EACH STATEMENT EXECUTE FUNCTION public.erp_w4_transfer_truncate();CREATE OR REPLACE FUNCTION public.erp_w4_production_update_stage(s public.product_stock,before_qty numeric,before_status text,completed boolean) RETURNS boolean LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
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
          AND before_status NOT IN ('archived','sold_out','expired','temp_violation') AND s."ExpiryDate">(p."PostedAt" AT TIME ZONE 'UTC')::date
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
    CREATE OR REPLACE FUNCTION public.erp_w4_production_complete(order_id uuid) RETURNS void LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
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
          AND s."ExpiryDate">(p."PostedAt" AT TIME ZONE 'UTC')::date AND coalesce(l."Metadata"->>'beforeStatus',s."Status") NOT IN('archived','sold_out','expired','temp_violation')
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
    END $$;
    CREATE OR REPLACE FUNCTION public.erp_w4_pos_stage(s public.product_stock,before_qty numeric,completed boolean) RETURNS boolean LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
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
          AND s."Status" NOT IN('archived','expired','temp_violation') AND s."ExpiryDate">(p."PostedAt" AT TIME ZONE 'UTC')::date
     AND l."Metadata"->>'saleId'=t."Id"::text AND l."Metadata"->>'batchId'=s."Id"::text AND l."ItemId"=s."ProductId" AND l."Quantity">0
          AND l."FromLocationId"=s."LocationId" AND l."ToLocationId" IS NULL AND l."BatchNumberSnapshot" IS NOT DISTINCT FROM s."BatchNumber" AND l."ExpiryDateSnapshot"=s."ExpiryDate"
          AND e."BatchId"=s."Id" AND e."ItemId"=s."ProductId" AND e."LocationId"=s."LocationId" AND e."ZoneId" IS NOT DISTINCT FROM s."ZoneId"
          AND e."QuantityBefore"=before_qty AND e."QuantityAfter"=s."Quantity" AND e."QuantityDelta"=-l."Quantity" AND e."UnitCost" IS NOT DISTINCT FROM l."UnitCost") THEN
        RAISE EXCEPTION 'POS stock requires its staged ERP sale' USING ERRCODE='P4F01'; END IF;
      RETURN true;
    END $$;
    CREATE OR REPLACE FUNCTION public.erp_w4_transfer_stage(s public.product_stock, before_qty numeric, inserting boolean, completed boolean) RETURNS boolean LANGUAGE plpgsql
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
          AND(phase<>'transfer.dispatch.v1' OR s."Status" NOT IN('archived','expired','temp_violation') AND s."ExpiryDate">(p."PostedAt" AT TIME ZONE 'UTC')::date)
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
    CREATE OR REPLACE FUNCTION public.erp_w4_iot_source(receipt_id uuid,canonical_payload text) RETURNS jsonb LANGUAGE sql STABLE SET search_path=pg_catalog,public AS $$
      SELECT jsonb_build_object('posPrimary',t."Modules" @> '["pos"]'::jsonb,'receipt',to_jsonb(r),'payload',canonical_payload::jsonb,'policy',to_jsonb(p),
        'device',to_jsonb(d)-ARRAY['LastSeenAt','BatteryLevel'],'item',jsonb_build_object('Id',i."Id",'Name',i."Name",'Unit',i."Unit",'IsActive',i."IsActive",'Barcodes',i."Barcodes"),
        'authorized',coalesce(t."IsActive" AND t."Modules" @> '["inventory"]'::jsonb AND l."IsActive" AND d."IsActive" AND d."DeviceType"='weight_sensor'
          AND d."LocationId"=p."LocationId" AND d."ZoneId" IS NOT DISTINCT FROM p."ZoneId" AND p."Enabled" AND i."IsActive" AND i."Unit"=p."Unit"
          AND d."Config"->>'product_id'=p."ItemId"::text AND d."Config"->>'inventory_unit'=p."Unit" AND d."Config"->'unit_weight_grams'=to_jsonb(p."UnitWeightGrams")
          AND u."IsActive" AND u."TenantId"=r."TenantId" AND u."Role" IN('enterprise_admin','network_manager','store_manager')
          AND(u."Role"='enterprise_admin' OR EXISTS(SELECT 1 FROM public.user_locations ul WHERE ul."TenantId"=r."TenantId" AND ul."UserId"=u."Id" AND ul."LocationId"=d."LocationId")),false))
      FROM public.iot_message_receipts r JOIN public.iot_devices d ON d."Id"=r."DeviceId" AND d."TenantId"=r."TenantId"
      JOIN public.tenants t ON t."Id"=r."TenantId" JOIN public.locations l ON l."TenantId"=r."TenantId" AND l."Id"=d."LocationId"
      LEFT JOIN LATERAL(SELECT x.* FROM public.iot_inventory_policies x WHERE x."TenantId"=r."TenantId" AND x."DeviceId"=d."Id" ORDER BY x."Version" DESC LIMIT 1)p ON true
      LEFT JOIN public.items i ON i."TenantId"=p."TenantId" AND i."Id"=p."ItemId" LEFT JOIN public.users u ON u."Id"=p."ActorId"
      WHERE r."Id"=receipt_id
    $$;
    CREATE OR REPLACE FUNCTION public.erp_w4_iot_stage(s public.product_stock,before_qty numeric,before_status text,completed boolean) RETURNS boolean LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    DECLARE marker uuid:=public.erp_w4_iot_posting();
    BEGIN
      IF marker IS NULL THEN RETURN false; END IF;
      IF NOT EXISTS(SELECT 1 FROM public.inventory_ledger_entries e JOIN public.document_postings p ON p."TenantId"=e."TenantId" AND p."Id"=e."PostingId"
        JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId" JOIN public.document_lines l ON l."TenantId"=e."TenantId" AND l."Id"=e."DocumentLineId"
        JOIN public.inventory_ledger_entries previous ON previous."TenantId"=e."TenantId" AND previous."Id"::text=l."Metadata"->>'previousEntryId'
        WHERE p."Id"=marker AND e."TenantId"=s."TenantId" AND d."Status"=CASE WHEN completed THEN 'posted' ELSE 'approved' END AND d."DocumentType"='sensor_write_down'
          AND p."PerformedBy"=NULLIF(current_setting('app.user_id',true),'')::uuid AND p."PerformedBy"=d."CreatedBy"
          AND e."BatchId"=s."Id" AND e."ItemId"=s."ProductId" AND e."LocationId"=s."LocationId" AND e."ZoneId" IS NOT DISTINCT FROM s."ZoneId"
          AND e."QuantityBefore"=before_qty AND e."QuantityDelta"=-l."Quantity" AND e."QuantityDelta"<0 AND e."QuantityAfter"=s."Quantity" AND s."Quantity"=before_qty+e."QuantityDelta"
          AND l."FromLocationId"=s."LocationId" AND l."ToLocationId" IS NULL AND l."Metadata"->>'batchId'=s."Id"::text
          AND (l."Metadata"->'source'->'policy'->>'AccountingMode')='automatic_write_down' AND NOT(l."Metadata"->'source'->>'posPrimary')::boolean AND (l."Metadata"->'source'->>'authorized')::boolean AND l."Metadata"->'source'=public.erp_w4_iot_source(d."SourceId"::uuid,l."Metadata"->>'canonicalPayload')
          AND previous."BatchId"=s."Id" AND previous."ItemId"=s."ProductId" AND previous."LocationId"=s."LocationId" AND previous."ZoneId" IS NOT DISTINCT FROM s."ZoneId" AND previous."QuantityAfter"=before_qty
          AND previous."ExpiryDateSnapshot"=s."ExpiryDate" AND previous."BatchNumberSnapshot" IS NOT DISTINCT FROM s."BatchNumber"
          AND EXISTS(SELECT 1 FROM public.document_lines pl WHERE pl."TenantId"=previous."TenantId" AND pl."Id"=previous."DocumentLineId" AND pl."Unit"=l."Unit")
          AND e."UnitCost" IS NOT DISTINCT FROM previous."UnitCost" AND e."UnitCost" IS NOT DISTINCT FROM l."UnitCost" AND previous."Currency"='UAH' AND e."ValueDelta" IS NOT DISTINCT FROM round(e."QuantityDelta"*e."UnitCost",4)
          AND l."ExpiryDateSnapshot"=s."ExpiryDate" AND l."BatchNumberSnapshot" IS NOT DISTINCT FROM s."BatchNumber"
          AND l."Unit"=l."Metadata"->'source'->'policy'->>'Unit' AND s."ExpiryDate">(p."PostedAt" AT TIME ZONE 'UTC')::date
          AND l."Metadata"->>'beforeStatus'=before_status AND before_status NOT IN('expired','sold_out','archived','temp_violation')
          AND(completed OR EXISTS(SELECT 1 FROM public.inventory_balance_projections b WHERE b."TenantId"=s."TenantId" AND b."BatchId"=s."Id" AND b."Quantity"=before_qty AND b."ReservedQuantity"=0 AND b."LastLedgerEntryId"=previous."Id"))
      ) THEN RAISE EXCEPTION 'IoT stock change requires its authorized reconciled posting' USING ERRCODE='P4F01'; END IF;
      RETURN true;
    END $$;
    CREATE OR REPLACE FUNCTION public.erp_w4_iot_complete(posting_id uuid) RETURNS void LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    DECLARE d public.business_documents;p public.document_postings;r public.iot_message_receipts;line public.document_lines; source jsonb; units numeric;ratio numeric;n bigint;
    BEGIN
      SELECT * INTO p FROM public.document_postings WHERE "Id"=posting_id;
      SELECT * INTO d FROM public.business_documents WHERE "TenantId"=p."TenantId" AND "Id"=p."DocumentId";
      SELECT * INTO r FROM public.iot_message_receipts WHERE "TenantId"=d."TenantId" AND "Id"::text=d."SourceId";
      SELECT * INTO line FROM public.document_lines WHERE "TenantId"=d."TenantId" AND "DocumentId"=d."Id" ORDER BY "LineNumber" LIMIT 1;
      source:=line."Metadata"->'source';ratio:=abs((source->'payload'->>'delta')::numeric)/(source->'policy'->>'UnitWeightGrams')::numeric;units:=round(ratio);
      SELECT count(*) INTO n FROM public.document_lines WHERE "TenantId"=d."TenantId" AND "DocumentId"=d."Id";
      IF d."Id" IS NULL OR r."Id" IS NULL OR d."Status"<>'posted' OR d."DocumentType"<>'sensor_write_down' OR d."SourceSystem"<>'iot.weight.v1' OR public.erp_w4_iot_posting() IS DISTINCT FROM p."Id" OR n NOT BETWEEN 1 AND 100
        OR source->'policy'->>'AccountingMode' IS DISTINCT FROM 'automatic_write_down' OR(source->>'posPrimary')::boolean OR source IS DISTINCT FROM public.erp_w4_iot_source(r."Id",line."Metadata"->>'canonicalPayload') OR NOT(source->>'authorized')::boolean
        OR source->'payload'->>'timestamp' IS NULL OR(source->'payload'->>'timestamp')::timestamptz IS DISTINCT FROM r."RecordedAt"
        OR abs((source->'payload'->>'delta')::numeric)>99999999.99 OR round((source->'payload'->>'delta')::numeric,4) IS DISTINCT FROM (source->'payload'->>'delta')::numeric
        OR round((source->'payload'->>'delta')::numeric,2) IS DISTINCT FROM (source->'payload'->>'delta')::numeric OR round((source->'payload'->>'weight_before')::numeric,2) IS DISTINCT FROM (source->'payload'->>'weight_before')::numeric OR round((source->'payload'->>'weight_after')::numeric,2) IS DISTINCT FROM (source->'payload'->>'weight_after')::numeric
        OR r."PayloadHash"<>encode(sha256(convert_to(line."Metadata"->>'canonicalPayload','UTF8')),'hex') OR (source->'payload'->>'delta')::numeric>=0 OR units NOT BETWEEN 1 AND 99999999 OR abs(ratio-units)>0.05*units
        OR d."ContentHash" IS DISTINCT FROM encode(sha256(convert_to(jsonb_build_object('source',source,'batches',line."Metadata"->'plan')::text,'UTF8')),'hex')
        OR p."PostedAt" IS DISTINCT FROM d."PostedAt" OR p."PerformedBy" IS DISTINCT FROM (source->'policy'->>'ActorId')::uuid
        OR r."RecordedAt"<p."PostedAt"-interval '5 minutes' OR r."RecordedAt">p."PostedAt"+interval '5 minutes'
        OR n<>(SELECT count(*) FROM public.inventory_ledger_entries WHERE "TenantId"=p."TenantId" AND "PostingId"=p."Id")
        OR (SELECT sum(-"QuantityDelta") FROM public.inventory_ledger_entries WHERE "TenantId"=p."TenantId" AND "PostingId"=p."Id") IS DISTINCT FROM units
        OR NOT EXISTS(SELECT 1 FROM public.outbox_events WHERE "TenantId"=p."TenantId" AND "PostingId"=p."Id" AND "DeliveryScope"='live')
        OR NOT EXISTS(SELECT 1 FROM public.document_approvals WHERE "TenantId"=p."TenantId" AND "DocumentId"=d."Id" AND "ContentHash"=d."ContentHash" AND "ApproverId"=p."PerformedBy" AND "Decision"='approved')
        OR EXISTS(SELECT 1 FROM public.inventory_reservations WHERE "TenantId"=p."TenantId" AND "LocationId"=(source->'policy'->>'LocationId')::uuid AND "ItemId"=(source->'policy'->>'ItemId')::uuid AND "Status"='active')
        OR NOT EXISTS(SELECT 1 FROM public.weight_readings w WHERE w."IngestionId"=r."Id" AND w."DeviceId"=r."DeviceId" AND w."ZoneId" IS NOT DISTINCT FROM (source->'policy'->>'ZoneId')::uuid AND w."Processed" AND w."DeltaWeight"=(source->'payload'->>'delta')::numeric AND w."WeightBefore" IS NOT DISTINCT FROM (source->'payload'->>'weight_before')::numeric AND w."WeightAfter" IS NOT DISTINCT FROM (source->'payload'->>'weight_after')::numeric AND w."Confidence"=CASE WHEN units=1 THEN 95 ELSE 85 END AND w."RecordedAt"=r."RecordedAt")
        OR (SELECT count(*) FROM public.stock_events WHERE "TenantId"=r."TenantId" AND "Meta"->>'ingestionId'=r."Id"::text)<>1
        OR NOT EXISTS(SELECT 1 FROM public.stock_events v WHERE v."TenantId"=r."TenantId" AND v."Meta"->>'ingestionId'=r."Id"::text AND v."Meta"->>'postingId'=p."Id"::text AND v."Meta"->>'applied'='true' AND v."Meta"->>'reason'='applied' AND v."Meta"->>'policyId'=source->'policy'->>'Id' AND(v."Meta"->>'units')::numeric=units AND(v."Meta"->>'delta')::numeric=(source->'payload'->>'delta')::numeric AND v."EventType"='sensor' AND v."QuantityDelta"=-units AND v."PerformedBy"=p."PerformedBy" AND v."ProductId"=(source->'policy'->>'ItemId')::uuid AND v."StoreId"=(source->'policy'->>'LocationId')::uuid AND v."SourceDeviceId"=source->'device'->>'DeviceId' AND v."CreatedAt"=p."PostedAt" AND v."Confidence"=CASE WHEN units=1 THEN 95 ELSE 85 END) THEN RAISE EXCEPTION 'IoT inbox, source, telemetry, posting and outbox must agree' USING ERRCODE='P4F01'; END IF;
      IF EXISTS(SELECT 1 FROM public.inventory_ledger_entries e JOIN public.document_lines l ON l."TenantId"=e."TenantId" AND l."Id"=e."DocumentLineId"
        LEFT JOIN public.product_stock s ON s."TenantId"=e."TenantId" AND s."Id"=e."BatchId" LEFT JOIN public.inventory_balance_projections b ON b."TenantId"=e."TenantId" AND b."BatchId"=e."BatchId"
        WHERE e."TenantId"=p."TenantId" AND e."PostingId"=p."Id" AND(s."Id" IS NULL OR NOT public.erp_w4_iot_stage(s,e."QuantityBefore",l."Metadata"->>'beforeStatus',true) OR b."LastLedgerEntryId" IS DISTINCT FROM e."Id" OR b."Quantity" IS DISTINCT FROM e."QuantityAfter" OR b."ReservedQuantity"<>0
          OR s."Quantity" IS DISTINCT FROM(SELECT sum(x."QuantityDelta") FROM public.inventory_ledger_entries x WHERE x."TenantId"=s."TenantId" AND x."BatchId"=s."Id")
          OR l."Metadata"->'source' IS DISTINCT FROM source OR NOT EXISTS(SELECT 1 FROM public.stock_movements m WHERE m."TenantId"=p."TenantId" AND m."ReferenceType"='iot_sensor' AND m."ReferenceId"=r."Id" AND m."ProductStockId"=s."Id" AND m."MovementType"='adjustment' AND m."Quantity"=-e."QuantityDelta" AND m."QuantityBefore"=e."QuantityBefore" AND m."QuantityAfter"=e."QuantityAfter" AND m."PerformedBy"=p."PerformedBy" AND m."UnitPrice" IS NULL AND m."TotalAmount" IS NULL))) THEN RAISE EXCEPTION 'IoT balances and audit do not match ledger' USING ERRCODE='P4F01'; END IF;
      IF EXISTS(WITH candidates AS(SELECT s."Id",s."ExpiryDate",s."AddedAt",coalesce(e."QuantityBefore",s."Quantity") before_qty,coalesce(-e."QuantityDelta",0) taken
        FROM public.product_stock s LEFT JOIN public.inventory_ledger_entries e ON e."TenantId"=s."TenantId" AND e."PostingId"=p."Id" AND e."BatchId"=s."Id" LEFT JOIN public.document_lines l ON l."TenantId"=e."TenantId" AND l."Id"=e."DocumentLineId"
        WHERE s."TenantId"=p."TenantId" AND s."LocationId"=(source->'policy'->>'LocationId')::uuid AND s."ProductId"=(source->'policy'->>'ItemId')::uuid AND s."ZoneId" IS NOT DISTINCT FROM (source->'policy'->>'ZoneId')::uuid
          AND(s."Quantity">0 OR e."Id" IS NOT NULL) AND s."ExpiryDate">(p."PostedAt" AT TIME ZONE 'UTC')::date AND coalesce(l."Metadata"->>'beforeStatus',s."Status") NOT IN('expired','sold_out','archived','temp_violation')),
        expected AS(SELECT *,coalesce(sum(before_qty) OVER(ORDER BY "ExpiryDate","AddedAt","Id" ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),0) prior FROM candidates)
        SELECT 1 FROM expected WHERE taken<>greatest(least(before_qty,units-prior),0)) THEN RAISE EXCEPTION 'IoT FEFO allocation changed' USING ERRCODE='P4F01'; END IF;
    END $$;
    CREATE OR REPLACE FUNCTION public.erp_w4_iot_ingest_weight(receipt_id uuid,canonical_payload text,configured boolean) RETURNS jsonb LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    DECLARE r public.iot_message_receipts; device public.iot_devices; policy public.iot_inventory_policies; source jsonb; payload jsonb:=canonical_payload::jsonb;
      reason text:='not_eligible'; units numeric:=0; confidence int:=0; ratio numeric; delta numeric; remaining numeric; b record; batches jsonb:='[]'; take numeric;
      doc_id uuid:=gen_random_uuid(); doc_key uuid:=gen_random_uuid(); posting_id uuid:=gen_random_uuid(); line_id uuid; entry_id uuid; event_id uuid:=gen_random_uuid(); at timestamptz:=clock_timestamp();
      hash text; n int:=0; applied boolean:=false; event_entries jsonb:='[]'; value numeric; cost numeric; previous_role text:=current_setting('app.role',true);
    BEGIN
      IF current_setting('app.role',true) IS DISTINCT FROM 'worker' OR current_setting('transaction_isolation')<>'read committed' THEN RAISE EXCEPTION 'IoT posting requires worker ingestion transaction' USING ERRCODE='P4F01'; END IF;
      SELECT * INTO r FROM public.iot_message_receipts WHERE "Id"=receipt_id FOR UPDATE;
      SELECT * INTO device FROM public.iot_devices WHERE "Id"=r."DeviceId" AND "TenantId"=r."TenantId" FOR UPDATE;
      IF r."Id" IS NULL OR device."Id" IS NULL OR jsonb_typeof(payload->'delta') IS DISTINCT FROM 'number' OR r."TenantId" IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid
        OR r."PayloadHash"<>encode(sha256(convert_to(canonical_payload,'UTF8')),'hex') OR payload->>'device_id' IS DISTINCT FROM device."DeviceId"
        OR r."MessageKey" IS DISTINCT FROM encode(sha256(convert_to(CASE WHEN payload->>'event_id' IS NOT NULL THEN 'id:'||(payload->>'event_id') ELSE 'time:'||(payload->>'timestamp') END,'UTF8')),'hex')
        OR(payload->>'timestamp' IS NOT NULL AND (payload->>'timestamp')::timestamptz IS DISTINCT FROM r."RecordedAt")
        OR EXISTS(SELECT 1 FROM public.weight_readings WHERE "IngestionId"=receipt_id) THEN RAISE EXCEPTION 'Invalid IoT ingestion identity' USING ERRCODE='P4F01'; END IF;
      source:=public.erp_w4_iot_source(receipt_id,canonical_payload);
      SELECT * INTO policy FROM public.iot_inventory_policies WHERE "TenantId"=r."TenantId" AND "DeviceId"=device."Id" ORDER BY "Version" DESC LIMIT 1;
      delta:=(payload->>'delta')::numeric;
      IF delta IS NOT NULL AND policy."UnitWeightGrams">0 AND delta<0 AND abs(delta)<=99999999.99 AND round(delta,4)=delta THEN
        ratio:=abs(delta)/policy."UnitWeightGrams";units:=round(ratio);
        IF units BETWEEN 1 AND 99999999 AND abs(ratio-units)<=0.05*units THEN confidence:=CASE WHEN units=1 THEN 95 ELSE 85 END; ELSE units:=0;confidence:=60; END IF;
      END IF;
      IF NOT configured THEN reason:='erp_disabled';
      ELSIF NOT public.erp_w4_integrated_ready(r."TenantId",device."LocationId") THEN reason:='erp_scope_not_ready';
      ELSIF (source->>'posPrimary')::boolean THEN reason:='pos_observation';
     ELSIF policy."AccountingMode"='observation' THEN reason:='policy_observation';
     ELSIF policy."Id" IS NULL THEN reason:='policy_required';
      ELSIF NOT (source->>'authorized')::boolean THEN reason:='policy_or_scope_not_authorized';
      ELSIF payload->>'timestamp' IS NULL THEN reason:='timestamp_required';
      ELSIF r."RecordedAt"<at-interval '5 minutes' OR r."RecordedAt">at+interval '5 minutes' THEN reason:='stale_or_future_reading';
      ELSIF round(delta,2) IS DISTINCT FROM delta OR round((payload->>'weight_before')::numeric,2) IS DISTINCT FROM (payload->>'weight_before')::numeric OR round((payload->>'weight_after')::numeric,2) IS DISTINCT FROM (payload->>'weight_after')::numeric THEN reason:='telemetry_precision';
      ELSIF confidence<70 THEN reason:='not_eligible';
      ELSE
        LOCK TABLE public.product_stock IN EXCLUSIVE MODE; LOCK TABLE public.inventory_reservations IN SHARE MODE; LOCK TABLE public.items IN SHARE MODE;
        -- Re-read current source after the locks. Policy revision is serialized by the device row.
        source:=public.erp_w4_iot_source(receipt_id,canonical_payload);remaining:=units;
        IF NOT(source->>'authorized')::boolean THEN reason:='policy_or_scope_not_authorized';
        ELSIF EXISTS(SELECT 1 FROM public.inventory_reservations WHERE "TenantId"=r."TenantId" AND "LocationId"=device."LocationId" AND "ItemId"=policy."ItemId" AND "Status"='active') THEN reason:='active_reservation';
        ELSE
          reason:='applied';
          FOR b IN SELECT s.*,p."Quantity" balance,p."ReservedQuantity" reserved,p."LastLedgerEntryId" previous,e."UnitCost" cost,e."QuantityAfter" ledger_after,e."ItemId" ledger_item,e."LocationId" ledger_location,e."BatchId" ledger_batch,e."ZoneId" ledger_zone,e."ExpiryDateSnapshot" ledger_expiry,e."BatchNumberSnapshot" ledger_number,e."Currency" currency,l."Unit" ledger_unit,
              (SELECT sum(x."QuantityDelta") FROM public.inventory_ledger_entries x WHERE x."TenantId"=s."TenantId" AND x."BatchId"=s."Id") ledger_sum
            FROM public.product_stock s LEFT JOIN public.inventory_balance_projections p ON p."TenantId"=s."TenantId" AND p."BatchId"=s."Id"
            LEFT JOIN public.inventory_ledger_entries e ON e."TenantId"=s."TenantId" AND e."Id"=p."LastLedgerEntryId" LEFT JOIN public.document_lines l ON l."TenantId"=e."TenantId" AND l."Id"=e."DocumentLineId"
            WHERE s."TenantId"=r."TenantId" AND s."LocationId"=device."LocationId" AND s."ProductId"=policy."ItemId" AND s."ZoneId" IS NOT DISTINCT FROM device."ZoneId" AND s."Quantity">0
              AND s."ExpiryDate">(at AT TIME ZONE 'UTC')::date AND s."Status" NOT IN('expired','sold_out','archived','temp_violation') ORDER BY s."ExpiryDate",s."AddedAt",s."Id" LIMIT 1001
          LOOP
            n:=n+1; IF n>1000 THEN reason:='candidate_limit';EXIT; END IF;
            IF remaining>0 THEN
              IF b.previous IS NULL OR b.balance IS DISTINCT FROM b."Quantity" OR b.reserved IS DISTINCT FROM 0 OR b.ledger_after IS DISTINCT FROM b."Quantity" OR b.ledger_sum IS DISTINCT FROM b."Quantity"
                OR b.ledger_batch IS DISTINCT FROM b."Id" OR b.ledger_item IS DISTINCT FROM b."ProductId" OR b.ledger_location IS DISTINCT FROM b."LocationId" OR b.ledger_zone IS DISTINCT FROM b."ZoneId" OR b.ledger_expiry IS DISTINCT FROM b."ExpiryDate" OR b.ledger_number IS DISTINCT FROM b."BatchNumber" OR b.currency IS DISTINCT FROM 'UAH' OR b.ledger_unit IS DISTINCT FROM policy."Unit" THEN reason:='inventory_not_reconciled';EXIT; END IF;
              take:=least(remaining,b."Quantity"); remaining:=remaining-take;
              IF abs(round(take*b.cost,4))>99999999999999.9999 THEN reason:='valuation_limit';EXIT; END IF;
              batches:=batches||jsonb_build_array(jsonb_build_object('batchId',b."Id",'quantity',take,'before',b."Quantity",'after',b."Quantity"-take,'previousEntryId',b.previous,'cost',b.cost,'zone',b."ZoneId",'expiry',b."ExpiryDate",'number',b."BatchNumber",'status',b."Status"));
            END IF;
          END LOOP;
          IF reason='applied' AND remaining>0 THEN reason:='insufficient_eligible_stock'; END IF;
          IF reason='applied' AND jsonb_array_length(batches)>100 THEN reason:='allocation_limit'; END IF;
        END IF;
        IF reason='applied' THEN
          hash:=encode(sha256(convert_to(jsonb_build_object('source',source,'batches',batches)::text,'UTF8')),'hex');
          PERFORM set_config('app.user_id',policy."ActorId"::text,true);
          INSERT INTO public.business_documents("Id","TenantId","DocumentKey","Version","DocumentType","Status","SourceSystem","SourceId","Currency","CreatedBy","ContentHash","OccurredAt","CreatedAt","UpdatedAt")
            VALUES(doc_id,r."TenantId",doc_key,1,'sensor_write_down','draft','iot.weight.v1',receipt_id::text,'UAH',policy."ActorId",hash,r."RecordedAt",at,at);
          n:=0;
          FOR b IN SELECT * FROM jsonb_array_elements(batches) LOOP
            n:=n+1;line_id:=gen_random_uuid();
            INSERT INTO public.document_lines("Id","TenantId","DocumentId","LineNumber","ItemId","FromLocationId","Quantity","Unit","UnitCost","ItemNameSnapshot","BarcodeSnapshot","BatchNumberSnapshot","ExpiryDateSnapshot","Metadata")
              VALUES(line_id,r."TenantId",doc_id,n,policy."ItemId",device."LocationId",(b.value->>'quantity')::numeric,policy."Unit",(b.value->>'cost')::numeric,source->'item'->>'Name',source->'item'->'Barcodes'->>0,b.value->>'number',(b.value->>'expiry')::date,
                jsonb_build_object('batchId',b.value->>'batchId','previousEntryId',b.value->>'previousEntryId','beforeStatus',b.value->>'status','source',source,'canonicalPayload',canonical_payload,'plan',batches));
          END LOOP;
          UPDATE public.business_documents SET "Status"='submitted' WHERE "Id"=doc_id;
          INSERT INTO public.document_approvals("TenantId","DocumentId","Stage","ApproverId","Decision","ContentHash","Reason","CreatedAt") VALUES(r."TenantId",doc_id,1,policy."ActorId",'approved',hash,'Automatic IoT policy '||policy."Id"::text||' v'||policy."Version"||': '||policy."Reason",at);
          UPDATE public.business_documents SET "Status"='approved' WHERE "Id"=doc_id;
          INSERT INTO public.document_postings("Id","TenantId","DocumentId","IdempotencyKey","RequestHash","PerformedBy","PostedAt") VALUES(posting_id,r."TenantId",doc_id,'iot.weight.v1:'||receipt_id::text,hash,policy."ActorId",at);
          PERFORM set_config('app.erp_posting_id',posting_id::text,true);
          FOR b IN SELECT l.*,x.value data FROM public.document_lines l JOIN jsonb_array_elements(batches) x ON x.value->>'batchId'=l."Metadata"->>'batchId' WHERE l."TenantId"=r."TenantId" AND l."DocumentId"=doc_id ORDER BY l."LineNumber" LOOP
            entry_id:=gen_random_uuid();cost:=b."UnitCost";value:=round(-b."Quantity"*cost,4);
            INSERT INTO public.inventory_ledger_entries("Id","TenantId","DocumentId","PostingId","DocumentLineId","EntryNumber","ItemId","LocationId","BatchId","ZoneId","QuantityDelta","QuantityBefore","QuantityAfter","UnitCost","ValueDelta","Currency","BatchNumberSnapshot","ExpiryDateSnapshot","OccurredAt","CreatedAt")
              VALUES(entry_id,r."TenantId",doc_id,posting_id,b."Id",b."LineNumber",policy."ItemId",device."LocationId",(b.data->>'batchId')::uuid,device."ZoneId",-b."Quantity",(b.data->>'before')::numeric,(b.data->>'after')::numeric,cost,value,'UAH',b."BatchNumberSnapshot",b."ExpiryDateSnapshot",r."RecordedAt",at);
            UPDATE public.product_stock SET "Quantity"=(b.data->>'after')::numeric,"Status"=CASE WHEN(b.data->>'after')::numeric=0 THEN 'sold_out' ELSE "Status" END WHERE "Id"=(b.data->>'batchId')::uuid AND "TenantId"=r."TenantId";
            UPDATE public.inventory_balance_projections SET "Quantity"=(b.data->>'after')::numeric,"LastLedgerEntryId"=entry_id,"UpdatedAt"=at WHERE "TenantId"=r."TenantId" AND "BatchId"=(b.data->>'batchId')::uuid;
            INSERT INTO public.stock_movements("TenantId","MovementType","ProductStockId","ProductId","FromLocationId","FromZoneId","Quantity","QuantityBefore","QuantityAfter","ReferenceId","ReferenceType","PerformedBy","CreatedAt") VALUES(r."TenantId",'adjustment',(b.data->>'batchId')::uuid,policy."ItemId",device."LocationId",device."ZoneId",b."Quantity",(b.data->>'before')::numeric,(b.data->>'after')::numeric,receipt_id,'iot_sensor',policy."ActorId",at);
            event_entries:=event_entries||jsonb_build_array(jsonb_build_object('entryId',entry_id,'entryNumber',b."LineNumber",'itemId',policy."ItemId",'locationId',device."LocationId",'batchId',b.data->>'batchId','zoneId',device."ZoneId",'quantityDelta',(-b."Quantity")::text,'unitCost',cost::text,'valueDelta',value::text,'currency','UAH','reversesEntryId',NULL));
          END LOOP;
          INSERT INTO public.outbox_events("TenantId","PostingId","EventType","SchemaVersion","AggregateType","AggregateId","AggregateVersion","EventOrdinal","Payload","CorrelationId","OccurredAt","Attempts")
            VALUES(r."TenantId",posting_id,'inventory.document_posted',1,'business_document',doc_key,1,1,jsonb_build_object('documentId',doc_id,'documentKey',doc_key,'documentVersion',1,'postingId',posting_id,'entries',event_entries,'reversesPostingId',NULL),receipt_id,r."RecordedAt",0);
          applied:=true;
        END IF;
      END IF;
      INSERT INTO public.weight_readings("IngestionId","DeviceId","ZoneId","WeightBefore","WeightAfter","DeltaWeight","Processed","Confidence","RecordedAt") VALUES(receipt_id,device."Id",device."ZoneId",(payload->>'weight_before')::numeric,(payload->>'weight_after')::numeric,delta,applied,confidence,r."RecordedAt");
      INSERT INTO public.stock_events("Id","TenantId","EventType","ProductId","StoreId","SourceDeviceId","QuantityDelta","Confidence","Meta","PerformedBy","CreatedAt") VALUES(event_id,r."TenantId",'sensor',policy."ItemId",device."LocationId",device."DeviceId",CASE WHEN applied THEN -units ELSE NULL END,confidence,jsonb_build_object('source',source,'canonicalPayload',canonical_payload,'requiresReview',NOT applied,'ingestionId',receipt_id,'policyId',policy."Id",'applied',applied,'units',units,'delta',delta,'reason',reason,'postingId',CASE WHEN applied THEN posting_id ELSE NULL END),CASE WHEN applied THEN policy."ActorId" ELSE NULL END,at);
      IF applied THEN UPDATE public.business_documents SET "Status"='posted',"PostedAt"=at WHERE "Id"=doc_id; END IF;
      RETURN jsonb_build_object('applied',applied,'reason',reason,'units',units,'postingId',CASE WHEN applied THEN posting_id ELSE NULL END);
    END $$;
    CREATE OR REPLACE FUNCTION public.erp_w4_stock_before() RETURNS trigger LANGUAGE plpgsql
      SET search_path=pg_catalog,public AS $$
    DECLARE tenant uuid:=TG_ARGV[0]::uuid; location uuid:=TG_ARGV[1]::uuid; posting uuid;
    BEGIN
    IF TG_OP='UPDATE' AND OLD."TenantId"=tenant AND OLD."LocationId"=location THEN
      IF OLD."Status"='temp_violation' AND NEW."Status"<>'temp_violation' AND NOT public.erp_w4_quality_release_matches(OLD,NEW) THEN
        RAISE EXCEPTION 'Quality hold requires an audited release; quantity changes cannot release it' USING ERRCODE='P4F01'; END IF;
      IF OLD."Quantity" IS DISTINCT FROM NEW."Quantity" AND NOT public.erp_w4_batch_usable(OLD."Status",OLD."Quantity",OLD."ExpiryDate",(clock_timestamp() AT TIME ZONE 'UTC')::date)
        AND EXISTS(SELECT 1 FROM public.document_postings p JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId" WHERE p."Id"::text=current_setting('app.erp_posting_id',true) AND d."SourceSystem" IN('pos.sale.v1','production.complete.v1','transfer.dispatch.v1')) THEN
        RAISE EXCEPTION 'Sale, production and dispatch require a usable batch' USING ERRCODE='P4F01'; END IF;
    ELSIF TG_OP='INSERT' AND NEW."TenantId"=tenant AND NEW."LocationId"=location AND NEW."SourceType"='transfer' AND NEW."Status"<>'temp_violation' THEN
      IF EXISTS(SELECT 1 FROM public.document_postings p JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId"
        JOIN public.document_lines l ON l."TenantId"=d."TenantId" AND l."DocumentId"=d."Id" AND l."Metadata"->>'batchId'=NEW."Id"::text
        JOIN public.product_stock s ON s."TenantId"=p."TenantId" AND s."Id"::text=l."Metadata"->>'sourceBatchId'
        WHERE p."Id"::text=current_setting('app.erp_posting_id',true) AND d."SourceSystem"='transfer.receive.v1' AND s."Status"='temp_violation') THEN
        RAISE EXCEPTION 'Received transfer must retain its source quality hold' USING ERRCODE='P4F01'; END IF;
    END IF;

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
      IF public.erp_w4_iot_stage(NEW,OLD."Quantity",OLD."Status",false) THEN RETURN NEW; END IF;
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

    CREATE FUNCTION public.erp_w4_integrated_ready(tenant uuid,location uuid) RETURNS boolean LANGUAGE sql VOLATILE SET search_path=pg_catalog,public AS $$
      SELECT public.erp_w4_iot_ready(tenant,location)
        AND EXISTS(SELECT 1 FROM pg_attribute WHERE attrelid='public.iot_inventory_policies'::regclass AND attname='AccountingMode' AND NOT attisdropped)
        AND(SELECT count(*)=3 FROM pg_trigger t JOIN(VALUES
          ('erp_w4_quality_event_guard','erp_w4_quality_event_guard',31,false),('erp_w4_quality_event_complete','erp_w4_quality_event_complete',5,true),('erp_w4_quality_event_truncate','erp_w4_transfer_truncate',34,false)
        )v(name,fn,kind,deferred) ON t.tgname=v.name AND t.tgfoid=('public.'||v.fn||'()')::regprocedure AND t.tgtype=v.kind AND t.tgdeferrable=v.deferred AND t.tginitdeferred=v.deferred WHERE t.tgrelid='public.stock_events'::regclass AND NOT t.tgisinternal AND t.tgenabled='O')
    $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027193717_AddIntegratedInventoryReadiness') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261027193717_AddIntegratedInventoryReadiness', '8.0.11');
    END IF;
END $EF$;
COMMIT;

