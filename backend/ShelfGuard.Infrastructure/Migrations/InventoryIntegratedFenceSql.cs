namespace ShelfGuard.Infrastructure.Migrations;

/// <summary>Cross-writer quality and POS-primary sensor semantics; previous migrations remain immutable.</summary>
internal static class InventoryIntegratedFenceSql
{
    internal static readonly string Upgrade=Build();
    private static string Function(string sql,string name)
    {
        var start=sql.IndexOf("CREATE FUNCTION public."+name+"(",StringComparison.Ordinal);
        if(start<0)throw new InvalidOperationException("Missing predecessor function: "+name);
        var end=sql.IndexOf("END $$;",start,StringComparison.Ordinal)+7;
        // SQL-language source snapshot functions have no BEGIN/END.
        if(name=="erp_w4_iot_source")end=sql.IndexOf("$$;",sql.IndexOf("AS $$",start,StringComparison.Ordinal)+5,StringComparison.Ordinal)+3;
        return sql[start..end].Replace("CREATE FUNCTION","CREATE OR REPLACE FUNCTION",StringComparison.Ordinal)+"\n";
    }
    private static string Build()
    {
        var source=InventoryIotFenceSql.Upgrade;
        var beforeStart=source.IndexOf("CREATE OR REPLACE FUNCTION public.erp_w4_stock_before",StringComparison.Ordinal);
        var beforeEnd=source.IndexOf("CREATE OR REPLACE FUNCTION public.erp_w4_stock_complete",beforeStart,StringComparison.Ordinal);
        var before=source[beforeStart..beforeEnd];
        var body=before.IndexOf("BEGIN",StringComparison.Ordinal)+5;
        before=before.Insert(body,"\n"+StockGuard+"\n");
        var prod=Function(InventoryProductionFenceSql.Upgrade,"erp_w4_production_update_stage")+Function(InventoryProductionFenceSql.Upgrade,"erp_w4_production_complete");
        prod=prod.Replace("'archived','sold_out','expired'","'archived','sold_out','expired','temp_violation'",StringComparison.Ordinal);
        var pos=Function(InventoryPosFenceSql.Upgrade,"erp_w4_pos_stage");
        pos=pos.Replace("AND l.\"Metadata\"->>'saleId'", "AND s.\"Status\" NOT IN('archived','expired','temp_violation') AND s.\"ExpiryDate\">(p.\"PostedAt\" AT TIME ZONE 'UTC')::date\n AND l.\"Metadata\"->>'saleId'",StringComparison.Ordinal);
        var transfer=Function(InventoryTransferFenceSql.Upgrade,"erp_w4_transfer_stage");
        transfer=transfer.Replace("AND ti.\"ProductId\"=s.\"ProductId\"", "AND(phase<>'transfer.dispatch.v1' OR s.\"Status\" NOT IN('archived','expired','temp_violation') AND s.\"ExpiryDate\">(p.\"PostedAt\" AT TIME ZONE 'UTC')::date)\n AND ti.\"ProductId\"=s.\"ProductId\"",StringComparison.Ordinal);
        var sensorSource=Function(source,"erp_w4_iot_source").Replace("jsonb_build_object('receipt',", "jsonb_build_object('posPrimary',t.\"Modules\" @> '[\"pos\"]'::jsonb,'receipt',",StringComparison.Ordinal);
        var sensorStage=Function(source,"erp_w4_iot_stage").Replace("AND (l.\"Metadata\"->'source'->>'authorized')::boolean", "AND (l.\"Metadata\"->'source'->'policy'->>'AccountingMode')='automatic_write_down' AND NOT(l.\"Metadata\"->'source'->>'posPrimary')::boolean AND (l.\"Metadata\"->'source'->>'authorized')::boolean",StringComparison.Ordinal);
        var sensorComplete=Function(source,"erp_w4_iot_complete").Replace("OR source IS DISTINCT FROM", "OR source->'policy'->>'AccountingMode' IS DISTINCT FROM 'automatic_write_down' OR(source->>'posPrimary')::boolean OR source IS DISTINCT FROM",StringComparison.Ordinal);
        var sensor=Function(source,"erp_w4_iot_ingest_weight")
            .Replace("ELSIF NOT public.erp_w4_iot_ready", "ELSIF NOT public.erp_w4_integrated_ready",StringComparison.Ordinal)
            .Replace("ELSIF policy.\"Id\" IS NULL THEN", "ELSIF (source->>'posPrimary')::boolean THEN reason:='pos_observation';\n ELSIF policy.\"AccountingMode\"='observation' THEN reason:='policy_observation';\n ELSIF policy.\"Id\" IS NULL THEN",StringComparison.Ordinal)
            .Replace("jsonb_build_object('ingestionId',receipt_id,", "jsonb_build_object('source',source,'canonicalPayload',canonical_payload,'requiresReview',NOT applied,'ingestionId',receipt_id,",StringComparison.Ordinal);
        return Schema+Quality+prod+pos+transfer+sensorSource+sensorStage+sensorComplete+sensor+before+Readiness;
    }
    private const string Schema="""
        ALTER TABLE public.iot_inventory_policies ADD COLUMN "AccountingMode" varchar(32) NOT NULL DEFAULT 'observation';
        ALTER TABLE public.iot_inventory_policies ADD CONSTRAINT "CK_iot_inventory_accounting_mode" CHECK("AccountingMode" IN('observation','automatic_write_down'));
        CREATE FUNCTION public.erp_w4_batch_usable(status text,quantity numeric,expiry date,today date) RETURNS boolean LANGUAGE sql IMMUTABLE AS $$
          SELECT quantity>0 AND expiry>today AND status NOT IN('expired','sold_out','archived','temp_violation')
        $$;
        """;
    private const string StockGuard="""
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
        """;
    private const string Quality="""
        CREATE FUNCTION public.erp_w4_quality_release_matches(old_stock public.product_stock,new_stock public.product_stock) RETURNS boolean LANGUAGE sql VOLATILE SET search_path=pg_catalog,public AS $$
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
        CREATE TRIGGER erp_w4_quality_event_truncate BEFORE TRUNCATE ON public.stock_events FOR EACH STATEMENT EXECUTE FUNCTION public.erp_w4_transfer_truncate();
        """;
    private const string Readiness="""
        CREATE FUNCTION public.erp_w4_integrated_ready(tenant uuid,location uuid) RETURNS boolean LANGUAGE sql VOLATILE SET search_path=pg_catalog,public AS $$
          SELECT public.erp_w4_iot_ready(tenant,location)
            AND EXISTS(SELECT 1 FROM pg_attribute WHERE attrelid='public.iot_inventory_policies'::regclass AND attname='AccountingMode' AND NOT attisdropped)
            AND(SELECT count(*)=3 FROM pg_trigger t JOIN(VALUES
              ('erp_w4_quality_event_guard','erp_w4_quality_event_guard',31,false),('erp_w4_quality_event_complete','erp_w4_quality_event_complete',5,true),('erp_w4_quality_event_truncate','erp_w4_transfer_truncate',34,false)
            )v(name,fn,kind,deferred) ON t.tgname=v.name AND t.tgfoid=('public.'||v.fn||'()')::regprocedure AND t.tgtype=v.kind AND t.tgdeferrable=v.deferred AND t.tginitdeferred=v.deferred WHERE t.tgrelid='public.stock_events'::regclass AND NOT t.tgisinternal AND t.tgenabled='O')
        $$;
        """;
}
