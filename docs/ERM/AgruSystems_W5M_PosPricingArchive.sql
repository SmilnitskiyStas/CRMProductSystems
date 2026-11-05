START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261029084857_AddPosPricingArchive') THEN
    ALTER TABLE pos_transactions ADD "PricingArchiveRequired" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261029084857_AddPosPricingArchive') THEN
    ALTER TABLE pos_transactions ADD CONSTRAINT "AK_pos_transactions_TenantId_Id" UNIQUE ("TenantId", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261029084857_AddPosPricingArchive') THEN
    CREATE TABLE pos_pricing_snapshots (
        "TransactionId" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "StoreId" uuid NOT NULL,
        "OriginTransactionId" bigint NOT NULL DEFAULT (pg_current_xact_id()::text::bigint),
        "Protocol" character varying(40) NOT NULL,
        "Channel" character varying(30) NOT NULL,
        "EvaluatedAt" timestamp with time zone NOT NULL,
        "RecordedAt" timestamp with time zone NOT NULL,
        "ContentHash" character varying(64) NOT NULL,
        "Payload" json NOT NULL,
        CONSTRAINT "PK_pos_pricing_snapshots" PRIMARY KEY ("TransactionId"),
        CONSTRAINT "CK_pos_pricing_snapshot_hash" CHECK ("ContentHash" ~ '^[0-9a-f]{64}$'),
        CONSTRAINT "CK_pos_pricing_snapshot_protocol" CHECK ("Protocol"='pos.pricing.archive.v1' AND "Channel" IN ('pos-online','pos-offline-legacy','pos-offline-pr1')),
        CONSTRAINT "FK_pos_pricing_snapshots_locations_TenantId_StoreId" FOREIGN KEY ("TenantId", "StoreId") REFERENCES locations ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_pos_pricing_snapshots_pos_transactions_TenantId_Transaction~" FOREIGN KEY ("TenantId", "TransactionId") REFERENCES pos_transactions ("TenantId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261029084857_AddPosPricingArchive') THEN
    CREATE INDEX "IX_pos_pricing_snapshots_TenantId_StoreId_EvaluatedAt" ON pos_pricing_snapshots ("TenantId", "StoreId", "EvaluatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261029084857_AddPosPricingArchive') THEN
    CREATE UNIQUE INDEX "IX_pos_pricing_snapshots_TenantId_TransactionId" ON pos_pricing_snapshots ("TenantId", "TransactionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261029084857_AddPosPricingArchive') THEN
    -- Keep the W4 v1 source projection byte-equivalent for pre-migration documents.
    CREATE OR REPLACE FUNCTION public.erp_w4_pos_snapshot(sale_id uuid) RETURNS jsonb LANGUAGE sql STABLE
      SET search_path=pg_catalog,public AS $$
      SELECT jsonb_build_object('header',to_jsonb(t)-ARRAY['FiscalNumber','Status','RetryCount','PricingArchiveRequired'],
        'items',coalesce((SELECT jsonb_agg(to_jsonb(i) ORDER BY i."Id") FROM public.pos_transaction_items i WHERE i."TransactionId"=t."Id"),'[]'::jsonb))
      FROM public.pos_transactions t WHERE t."Id"=sale_id
    $$;
    ALTER TABLE public.pos_pricing_snapshots ENABLE ROW LEVEL SECURITY;
    ALTER TABLE public.pos_pricing_snapshots FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON public.pos_pricing_snapshots
      USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
      WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
    CREATE POLICY worker_bypass ON public.pos_pricing_snapshots
      USING (current_setting('app.role',true)='worker')
      WITH CHECK (current_setting('app.role',true)='worker');
    CREATE TRIGGER pricing_archive_immutable BEFORE UPDATE OR DELETE ON public.pos_pricing_snapshots
      FOR EACH ROW EXECUTE FUNCTION public.erp_v1_immutable();
    CREATE TRIGGER pricing_archive_no_truncate BEFORE TRUNCATE ON public.pos_pricing_snapshots
      FOR EACH STATEMENT EXECUTE FUNCTION public.erp_v1_immutable();

    CREATE FUNCTION public.pos_pricing_v1_header() RETURNS trigger LANGUAGE plpgsql
      SET search_path=pg_catalog,public AS $$
    BEGIN
      IF NEW."PricingArchiveRequired" IS DISTINCT FROM OLD."PricingArchiveRequired" OR
        (OLD."PricingArchiveRequired" AND
         ROW(NEW."TenantId",NEW."LocationId",NEW."TotalAmount",NEW."TaxAmount",NEW."TenderedAmount",NEW."PaymentType")
         IS DISTINCT FROM ROW(OLD."TenantId",OLD."LocationId",OLD."TotalAmount",OLD."TaxAmount",OLD."TenderedAmount",OLD."PaymentType")) THEN
        RAISE EXCEPTION 'Pricing decision header is sealed' USING ERRCODE='P0001';
      END IF;
      RETURN NEW;
    END $$;
    CREATE TRIGGER pricing_archive_header BEFORE UPDATE ON public.pos_transactions
      FOR EACH ROW EXECUTE FUNCTION public.pos_pricing_v1_header();

    CREATE FUNCTION public.pos_pricing_v1_items() RETURNS trigger LANGUAGE plpgsql
      SET search_path=pg_catalog,public AS $$
    BEGIN
      IF TG_OP='DELETE' THEN
        IF EXISTS(SELECT 1 FROM public.pos_transactions WHERE "Id"=OLD."TransactionId" AND "PricingArchiveRequired") THEN
          RAISE EXCEPTION 'Archived pricing line cannot be deleted' USING ERRCODE='P0001';
        END IF;
        RETURN OLD;
      END IF;
      IF EXISTS(SELECT 1 FROM public.pos_transactions WHERE "Id" IN (OLD."TransactionId",NEW."TransactionId") AND "PricingArchiveRequired") AND
         (to_jsonb(NEW)-'ProductStockId') IS DISTINCT FROM (to_jsonb(OLD)-'ProductStockId') THEN
        RAISE EXCEPTION 'Archived pricing line is sealed' USING ERRCODE='P0001';
      END IF;
      RETURN NEW;
    END $$;
    CREATE TRIGGER pricing_archive_items BEFORE UPDATE OR DELETE ON public.pos_transaction_items
      FOR EACH ROW EXECUTE FUNCTION public.pos_pricing_v1_items();
    CREATE TRIGGER pricing_archive_items_no_truncate BEFORE TRUNCATE ON public.pos_transaction_items
      FOR EACH STATEMENT EXECUTE FUNCTION public.erp_v1_immutable();
    CREATE FUNCTION public.pos_pricing_v1_item_insert() RETURNS trigger LANGUAGE plpgsql
      SET search_path=pg_catalog,public AS $$
    BEGIN
      IF EXISTS(SELECT 1 FROM public.pos_transactions WHERE "Id"=NEW."TransactionId" AND "PricingArchiveRequired") AND
         NOT EXISTS(SELECT 1 FROM public.pos_pricing_snapshots a WHERE a."TransactionId"=NEW."TransactionId"
           AND a."OriginTransactionId"=pg_current_xact_id()::text::bigint) THEN
        RAISE EXCEPTION 'Cannot append a line after pricing archive commit' USING ERRCODE='23514';
      END IF;
      RETURN NULL;
    END $$;
    CREATE CONSTRAINT TRIGGER pricing_archive_item_insert AFTER INSERT ON public.pos_transaction_items
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.pos_pricing_v1_item_insert();

    CREATE FUNCTION public.pos_pricing_v1_insert() RETURNS trigger LANGUAGE plpgsql
      SET search_path=pg_catalog,public AS $$
    BEGIN
      NEW."OriginTransactionId":=pg_current_xact_id()::text::bigint;
      IF NEW."ContentHash"<>encode(sha256(convert_to(NEW."Payload"::text,'UTF8')),'hex') THEN
        RAISE EXCEPTION 'Pricing archive hash mismatch' USING ERRCODE='23514';
      END IF;
      IF (NEW."Payload"->>'version') IS DISTINCT FROM '1' OR
         (NEW."Payload"->>'policy') IS DISTINCT FROM 'pos-compatibility.v1' OR
         (NEW."Payload"->>'channel') IS DISTINCT FROM NEW."Channel" OR
         (NEW."Payload"->>'evaluatedAt') IS NULL OR
         -- Npgsql binary timestamps truncate 100 ns ticks, PostgreSQL JSON timestamp input rounds them.
         abs(extract(epoch FROM ((NEW."Payload"->>'evaluatedAt')::timestamptz-NEW."EvaluatedAt")))>0.000001 OR
         json_typeof(NEW."Payload"->'result'->'segments') IS DISTINCT FROM 'array' THEN
        RAISE EXCEPTION 'Invalid pricing archive envelope or hash' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END $$;
    CREATE TRIGGER pricing_archive_insert BEFORE INSERT ON public.pos_pricing_snapshots
      FOR EACH ROW EXECUTE FUNCTION public.pos_pricing_v1_insert();

    CREATE FUNCTION public.pos_pricing_v1_seal() RETURNS trigger LANGUAGE plpgsql
      SET search_path=pg_catalog,public AS $$
    DECLARE id uuid; t public.pos_transactions; a public.pos_pricing_snapshots; p jsonb;
    BEGIN
      IF TG_TABLE_NAME='pos_transactions' THEN id:=NEW."Id"; ELSE id:=NEW."TransactionId"; END IF;
      SELECT * INTO t FROM public.pos_transactions WHERE "Id"=id;
      SELECT * INTO a FROM public.pos_pricing_snapshots WHERE "TransactionId"=id;
      IF NOT t."PricingArchiveRequired" THEN
        IF a."TransactionId" IS NOT NULL THEN
          RAISE EXCEPTION 'Cannot backfill a legacy pricing decision' USING ERRCODE='23514';
        END IF;
        RETURN NULL;
      END IF;
      IF a."TransactionId" IS NULL OR a."TenantId"<>t."TenantId" OR a."StoreId"<>t."LocationId" THEN
        RAISE EXCEPTION 'Sale requires its pricing archive in the same commit' USING ERRCODE='23514';
      END IF;
      p:=a."Payload"::jsonb;
      IF (p->'result'->>'paid')::numeric IS DISTINCT FROM t."TotalAmount" OR
         (p->'result'->>'vat')::numeric IS DISTINCT FROM t."TaxAmount" OR
         (p->'result'->>'gross')::numeric IS DISTINCT FROM
           (SELECT SUM("LineTotal") FROM public.pos_transaction_items WHERE "TransactionId"=id) THEN
        RAISE EXCEPTION 'Pricing archive money does not match sale' USING ERRCODE='23514';
      END IF;
      IF EXISTS (
        WITH expected AS (
          SELECT jsonb_build_array((s->>'itemId')::uuid,(s->>'quantity')::numeric,
            (s->>'retailPrice')::numeric,(s->>'discount')::numeric,(s->>'finalPrice')::numeric,
            (s->>'gross')::numeric,(s->>'vatRate')::numeric,(s->>'taxable')::numeric,(s->>'vat')::numeric,
            (s->>'ruleId')::uuid,(s->>'ruleVersionId')::uuid,s->>'label') AS v
          FROM jsonb_array_elements(p->'result'->'segments') s
        ), actual AS (
          SELECT jsonb_build_array(i."ProductId",i."Quantity",i."PriceRetail",i."DiscountAmount",i."PriceFinal",
            i."LineTotal",i."VatRate",i."TaxableAmount",i."VatAmount",i."PromotionRuleId",r."VersionId",i."PromotionLabel") AS v
          FROM public.pos_transaction_items i LEFT JOIN public.promotion_rule_applications r
            ON r."TenantId"=t."TenantId" AND r."TransactionId"=id AND r."RuleId"=i."PromotionRuleId"
          WHERE i."TransactionId"=id
        )
        (SELECT v FROM expected EXCEPT ALL SELECT v FROM actual)
        UNION ALL
        (SELECT v FROM actual EXCEPT ALL SELECT v FROM expected)
      ) THEN RAISE EXCEPTION 'Pricing archive segments do not match sale' USING ERRCODE='23514'; END IF;
      RETURN NULL;
    END $$;
    CREATE CONSTRAINT TRIGGER pricing_archive_required AFTER INSERT ON public.pos_transactions
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.pos_pricing_v1_seal();
    CREATE CONSTRAINT TRIGGER pricing_archive_seal AFTER INSERT ON public.pos_pricing_snapshots
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.pos_pricing_v1_seal();
    DO $$ DECLARE r record; BEGIN
      FOR r IN SELECT rolname FROM pg_roles WHERE NOT rolsuper AND NOT rolbypassrls
        AND has_table_privilege(oid,'public.pos_transactions','INSERT') LOOP
        EXECUTE format('GRANT SELECT,INSERT ON public.pos_pricing_snapshots TO %I',r.rolname);
      END LOOP;
    END $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261029084857_AddPosPricingArchive') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261029084857_AddPosPricingArchive', '8.0.11');
    END IF;
END $EF$;
COMMIT;

