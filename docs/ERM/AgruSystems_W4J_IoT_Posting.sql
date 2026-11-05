START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027181539_AddErpIotPosting') THEN
    CREATE TABLE public.iot_inventory_policies (
      "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(), "TenantId" uuid NOT NULL REFERENCES public.tenants("Id") ON DELETE RESTRICT,
      "DeviceId" uuid NOT NULL REFERENCES public.iot_devices("Id") ON DELETE RESTRICT, "Version" int NOT NULL CHECK("Version">0), "Enabled" boolean NOT NULL,
      "ActorId" uuid NOT NULL REFERENCES public.users("Id") ON DELETE RESTRICT, "ItemId" uuid NOT NULL, "LocationId" uuid NOT NULL, "ZoneId" uuid,
      "Unit" varchar(24) NOT NULL CHECK(length(btrim("Unit"))>0), "UnitWeightGrams" numeric(12,4) NOT NULL CHECK("UnitWeightGrams">0 AND "UnitWeightGrams"<=99999999.99),
      "Reason" varchar(500) NOT NULL CHECK(length(btrim("Reason")) BETWEEN 15 AND 500), "CreatedAt" timestamptz NOT NULL DEFAULT now(),
      UNIQUE("TenantId","DeviceId","Version"), FOREIGN KEY("TenantId","ItemId") REFERENCES public.items("TenantId","Id") ON DELETE RESTRICT,
      FOREIGN KEY("TenantId","LocationId") REFERENCES public.locations("TenantId","Id") ON DELETE RESTRICT,
      FOREIGN KEY("LocationId","ZoneId") REFERENCES public.location_zones("LocationId","Id") ON DELETE RESTRICT
    );
    ALTER TABLE public.iot_inventory_policies ENABLE ROW LEVEL SECURITY;
    ALTER TABLE public.iot_inventory_policies FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON public.iot_inventory_policies USING("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid) WITH CHECK("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
    CREATE POLICY worker_bypass ON public.iot_inventory_policies USING(current_setting('app.role',true)='worker') WITH CHECK(current_setting('app.role',true)='worker');
    REVOKE ALL ON public.iot_inventory_policies FROM PUBLIC;
    ALTER TABLE public.weight_readings ADD COLUMN "IngestionId" uuid REFERENCES public.iot_message_receipts("Id") ON DELETE RESTRICT;
    CREATE UNIQUE INDEX "IX_weight_readings_IngestionId" ON public.weight_readings("IngestionId") WHERE "IngestionId" IS NOT NULL;
    CREATE FUNCTION public.erp_w4_iot_policy_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    BEGIN
      IF TG_OP<>'INSERT' OR NOT pg_has_role(current_user,(SELECT relowner FROM pg_class WHERE oid='public.product_stock'::regclass),'USAGE') THEN RAISE EXCEPTION 'IoT authorization is owner-maintained append-only history' USING ERRCODE='P4F01'; END IF;
      PERFORM 1 FROM public.iot_devices WHERE "Id"=NEW."DeviceId" FOR UPDATE;
      IF NEW."Version"<>coalesce((SELECT max("Version") FROM public.iot_inventory_policies WHERE "TenantId"=NEW."TenantId" AND "DeviceId"=NEW."DeviceId"),0)+1
        OR NOT EXISTS(SELECT 1 FROM public.iot_devices d JOIN public.items i ON i."TenantId"=d."TenantId" AND i."Id"=NEW."ItemId" JOIN public.users u ON u."TenantId"=d."TenantId" AND u."Id"=NEW."ActorId"
          WHERE d."Id"=NEW."DeviceId" AND d."TenantId"=NEW."TenantId" AND d."LocationId"=NEW."LocationId" AND d."ZoneId" IS NOT DISTINCT FROM NEW."ZoneId" AND d."DeviceType"='weight_sensor' AND i."Unit"=NEW."Unit" AND u."IsActive" AND u."Role" IN('enterprise_admin','network_manager','store_manager')) THEN RAISE EXCEPTION 'IoT policy identity, unit, actor or version mismatch' USING ERRCODE='P4F01'; END IF;
      RETURN NEW;
    END $$;
    CREATE TRIGGER erp_w4_iot_policy_guard BEFORE INSERT OR UPDATE OR DELETE ON public.iot_inventory_policies FOR EACH ROW EXECUTE FUNCTION public.erp_w4_iot_policy_guard();
    CREATE TRIGGER erp_w4_iot_policy_truncate BEFORE TRUNCATE ON public.iot_inventory_policies FOR EACH STATEMENT EXECUTE FUNCTION public.erp_w4_iot_policy_guard();CREATE FUNCTION public.erp_w4_iot_source(receipt_id uuid,canonical_payload text) RETURNS jsonb LANGUAGE sql STABLE SET search_path=pg_catalog,public AS $$
      SELECT jsonb_build_object('receipt',to_jsonb(r),'payload',canonical_payload::jsonb,'policy',to_jsonb(p),
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
    CREATE FUNCTION public.erp_w4_iot_posting() RETURNS uuid LANGUAGE plpgsql STABLE SET search_path=pg_catalog,public AS $$
    DECLARE marker uuid;
    BEGIN
      BEGIN marker:=NULLIF(current_setting('app.erp_posting_id',true),'')::uuid; EXCEPTION WHEN invalid_text_representation THEN RETURN NULL; END;
      RETURN(SELECT p."Id" FROM public.document_postings p JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId" WHERE p."Id"=marker AND d."SourceSystem"='iot.weight.v1');
    END $$;
    CREATE FUNCTION public.erp_w4_iot_stage(s public.product_stock,before_qty numeric,before_status text,completed boolean) RETURNS boolean LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
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
          AND (l."Metadata"->'source'->>'authorized')::boolean AND l."Metadata"->'source'=public.erp_w4_iot_source(d."SourceId"::uuid,l."Metadata"->>'canonicalPayload')
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
    END $$;CREATE FUNCTION public.erp_w4_iot_ingest_weight(receipt_id uuid,canonical_payload text,configured boolean) RETURNS jsonb LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
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
      ELSIF NOT public.erp_w4_iot_ready(r."TenantId",device."LocationId") THEN reason:='erp_scope_not_ready';
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
      INSERT INTO public.stock_events("Id","TenantId","EventType","ProductId","StoreId","SourceDeviceId","QuantityDelta","Confidence","Meta","PerformedBy","CreatedAt") VALUES(event_id,r."TenantId",'sensor',policy."ItemId",device."LocationId",device."DeviceId",CASE WHEN applied THEN -units ELSE NULL END,confidence,jsonb_build_object('ingestionId',receipt_id,'policyId',policy."Id",'applied',applied,'units',units,'delta',delta,'reason',reason,'postingId',CASE WHEN applied THEN posting_id ELSE NULL END),CASE WHEN applied THEN policy."ActorId" ELSE NULL END,at);
      IF applied THEN UPDATE public.business_documents SET "Status"='posted',"PostedAt"=at WHERE "Id"=doc_id; END IF;
      RETURN jsonb_build_object('applied',applied,'reason',reason,'units',units,'postingId',CASE WHEN applied THEN posting_id ELSE NULL END);
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
      IF public.erp_w4_iot_stage(NEW,OLD."Quantity",OLD."Status",true) THEN IF NOT EXISTS(SELECT 1 FROM public.product_stock s WHERE s."Id"=NEW."Id" AND to_jsonb(s)=to_jsonb(NEW)) THEN RAISE EXCEPTION 'IoT batch changed before commit' USING ERRCODE='P4F01'; END IF; PERFORM public.erp_w4_iot_complete(posting); RETURN NULL; END IF;
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

    CREATE FUNCTION public.erp_w4_iot_complete(posting_id uuid) RETURNS void LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    DECLARE d public.business_documents;p public.document_postings;r public.iot_message_receipts;line public.document_lines; source jsonb; units numeric;ratio numeric;n bigint;
    BEGIN
      SELECT * INTO p FROM public.document_postings WHERE "Id"=posting_id;
      SELECT * INTO d FROM public.business_documents WHERE "TenantId"=p."TenantId" AND "Id"=p."DocumentId";
      SELECT * INTO r FROM public.iot_message_receipts WHERE "TenantId"=d."TenantId" AND "Id"::text=d."SourceId";
      SELECT * INTO line FROM public.document_lines WHERE "TenantId"=d."TenantId" AND "DocumentId"=d."Id" ORDER BY "LineNumber" LIMIT 1;
      source:=line."Metadata"->'source';ratio:=abs((source->'payload'->>'delta')::numeric)/(source->'policy'->>'UnitWeightGrams')::numeric;units:=round(ratio);
      SELECT count(*) INTO n FROM public.document_lines WHERE "TenantId"=d."TenantId" AND "DocumentId"=d."Id";
      IF d."Id" IS NULL OR r."Id" IS NULL OR d."Status"<>'posted' OR d."DocumentType"<>'sensor_write_down' OR d."SourceSystem"<>'iot.weight.v1' OR public.erp_w4_iot_posting() IS DISTINCT FROM p."Id" OR n NOT BETWEEN 1 AND 100
        OR source IS DISTINCT FROM public.erp_w4_iot_source(r."Id",line."Metadata"->>'canonicalPayload') OR NOT(source->>'authorized')::boolean
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
    CREATE FUNCTION public.erp_w4_iot_doc_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    BEGIN IF NEW."SourceSystem"='iot.weight.v1' AND NEW."Status"='posted' AND(TG_OP='INSERT' OR OLD."Status" IS DISTINCT FROM NEW."Status") THEN PERFORM public.erp_w4_iot_complete((SELECT "Id" FROM public.document_postings WHERE "TenantId"=NEW."TenantId" AND "DocumentId"=NEW."Id")); END IF;RETURN NULL;END $$;
    CREATE FUNCTION public.erp_w4_iot_source_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
    DECLARE id text;tenant uuid;
    BEGIN
      IF TG_OP='TRUNCATE' THEN IF EXISTS(SELECT 1 FROM public.business_documents WHERE "SourceSystem"='iot.weight.v1') THEN RAISE EXCEPTION 'Retain IoT posting source history' USING ERRCODE='P4F01'; END IF; RETURN NULL; END IF;
      IF TG_OP<>'INSERT' THEN
        IF TG_TABLE_NAME='iot_message_receipts' THEN id:=OLD."Id"::text;
        ELSIF TG_TABLE_NAME='weight_readings' THEN id:=OLD."IngestionId"::text;
        ELSE id:=OLD."Meta"->>'ingestionId'; END IF;
        PERFORM 1 FROM public.iot_message_receipts WHERE "Id"::text=id FOR UPDATE;
        IF EXISTS(SELECT 1 FROM public.business_documents WHERE "SourceSystem"='iot.weight.v1' AND "SourceId"=id) THEN RAISE EXCEPTION 'IoT posted source is immutable' USING ERRCODE='P4F01'; END IF;
      END IF;
      IF TG_OP<>'DELETE' AND TG_TABLE_NAME<>'iot_message_receipts' THEN
        IF TG_TABLE_NAME='weight_readings' THEN id:=NEW."IngestionId"::text; ELSE id:=NEW."Meta"->>'ingestionId'; END IF;
        PERFORM 1 FROM public.iot_message_receipts WHERE "Id"::text=id FOR UPDATE;
        IF EXISTS(SELECT 1 FROM public.business_documents WHERE "SourceSystem"='iot.weight.v1' AND "SourceId"=id)
          AND NOT EXISTS(SELECT 1 FROM public.business_documents d JOIN public.document_postings p ON p."TenantId"=d."TenantId" AND p."DocumentId"=d."Id" WHERE d."SourceSystem"='iot.weight.v1' AND d."SourceId"=id AND d."Status"='approved' AND p."Id"=public.erp_w4_iot_posting()) THEN RAISE EXCEPTION 'IoT source is sealed' USING ERRCODE='P4F01'; END IF;
      END IF;
      IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
    END $$;
    CREATE CONSTRAINT TRIGGER erp_w4_iot_doc_complete AFTER INSERT OR UPDATE ON public.business_documents DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.erp_w4_iot_doc_complete();
    CREATE TRIGGER erp_w4_iot_receipt_guard BEFORE UPDATE OR DELETE ON public.iot_message_receipts FOR EACH ROW EXECUTE FUNCTION public.erp_w4_iot_source_guard();
    CREATE TRIGGER erp_w4_iot_reading_guard BEFORE INSERT OR UPDATE OR DELETE ON public.weight_readings FOR EACH ROW EXECUTE FUNCTION public.erp_w4_iot_source_guard();
    CREATE TRIGGER erp_w4_iot_event_guard BEFORE INSERT OR UPDATE OR DELETE ON public.stock_events FOR EACH ROW EXECUTE FUNCTION public.erp_w4_iot_source_guard();
    CREATE TRIGGER erp_w4_iot_receipt_truncate BEFORE TRUNCATE ON public.iot_message_receipts FOR EACH STATEMENT EXECUTE FUNCTION public.erp_w4_iot_source_guard();
    CREATE TRIGGER erp_w4_iot_reading_truncate BEFORE TRUNCATE ON public.weight_readings FOR EACH STATEMENT EXECUTE FUNCTION public.erp_w4_iot_source_guard();
    CREATE FUNCTION public.erp_w4_iot_ready(tenant uuid,location uuid) RETURNS boolean LANGUAGE sql VOLATILE SET search_path=pg_catalog,public AS $$
      SELECT public.erp_w4_production_ready(tenant,location) AND(SELECT count(*)=8 FROM pg_trigger t JOIN(VALUES
        ('erp_w4_iot_policy_guard','iot_inventory_policies','erp_w4_iot_policy_guard',31,false),('erp_w4_iot_policy_truncate','iot_inventory_policies','erp_w4_iot_policy_guard',34,false),
        ('erp_w4_iot_doc_complete','business_documents','erp_w4_iot_doc_complete',21,true),('erp_w4_iot_receipt_guard','iot_message_receipts','erp_w4_iot_source_guard',27,false),
        ('erp_w4_iot_reading_guard','weight_readings','erp_w4_iot_source_guard',31,false),('erp_w4_iot_event_guard','stock_events','erp_w4_iot_source_guard',31,false),
        ('erp_w4_iot_receipt_truncate','iot_message_receipts','erp_w4_iot_source_guard',34,false),('erp_w4_iot_reading_truncate','weight_readings','erp_w4_iot_source_guard',34,false)
      )v(name,rel,fn,kind,deferred) ON t.tgname=v.name AND t.tgrelid=('public.'||v.rel)::regclass AND t.tgfoid=('public.'||v.fn||'()')::regprocedure AND t.tgtype=v.kind AND t.tgdeferrable=v.deferred AND t.tginitdeferred=v.deferred WHERE NOT t.tgisinternal AND t.tgenabled='O')
    $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027181539_AddErpIotPosting') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261027181539_AddErpIotPosting', '8.0.11');
    END IF;
END $EF$;
COMMIT;

