using Microsoft.EntityFrameworkCore.Migrations;
namespace ShelfGuard.Infrastructure.Migrations;
public partial class AddPlannedPricing
{
    private static void InstallGuards(MigrationBuilder migrationBuilder)
    {
        foreach (var table in new[] { "price_lists", "price_list_items", "price_resolution_snapshots" })
            migrationBuilder.Sql($"""
                ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON public.{table}
                  USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
                  WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
                CREATE POLICY worker_bypass ON public.{table}
                  USING (current_setting('app.role',true)='worker')
                  WITH CHECK (current_setting('app.role',true)='worker');
                CREATE TRIGGER pricing_immutable BEFORE UPDATE OR DELETE ON public.{table}
                  FOR EACH ROW EXECUTE FUNCTION public.erp_v1_immutable();
                CREATE TRIGGER pricing_no_truncate BEFORE TRUNCATE ON public.{table}
                  FOR EACH STATEMENT EXECUTE FUNCTION public.erp_v1_immutable();
                """);
        migrationBuilder.Sql("""
            CREATE FUNCTION public.pricing_v1_header() RETURNS trigger LANGUAGE plpgsql
              SET search_path=pg_catalog,public AS $$
            DECLARE d public.business_documents; previous_version integer;
            BEGIN
              SELECT * INTO d FROM public.business_documents WHERE "TenantId"=NEW."TenantId" AND "Id"=NEW."DocumentId" FOR UPDATE;
              IF NOT FOUND OR d."Status"<>'draft' OR d."DocumentType"<>'price_plan' OR d."SourceSystem"<>'pricing.plan.v1'
                 OR d."DocumentKey"<>NEW."Id" OR d."CreatedBy" IS DISTINCT FROM NEW."CreatedBy" THEN
                RAISE EXCEPTION 'Planned price revision requires its own draft document' USING ERRCODE='23514';
              END IF;
              SELECT COALESCE(MAX("Version"),0) INTO previous_version FROM public.price_lists WHERE "TenantId"=NEW."TenantId" AND "ListKey"=NEW."ListKey";
              IF NEW."Version"<>previous_version+1 OR (previous_version=0 AND NEW."ListKey"<>NEW."Id") THEN
                RAISE EXCEPTION 'Price list revision chain mismatch' USING ERRCODE='23514';
              END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER pricing_header BEFORE INSERT ON public.price_lists FOR EACH ROW EXECUTE FUNCTION public.pricing_v1_header();

            CREATE FUNCTION public.pricing_v1_line() RETURNS trigger LANGUAGE plpgsql
              SET search_path=pg_catalog,public AS $$
            BEGIN
              PERFORM 1 FROM public.price_lists p JOIN public.business_documents d ON d."TenantId"=p."TenantId" AND d."Id"=p."DocumentId"
                WHERE p."TenantId"=NEW."TenantId" AND p."Id"=NEW."PriceListId" AND d."Status"='draft' FOR UPDATE OF d;
              IF NOT FOUND THEN RAISE EXCEPTION 'Cannot extend a sealed price revision' USING ERRCODE='23514'; END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER pricing_line BEFORE INSERT ON public.price_list_items FOR EACH ROW EXECUTE FUNCTION public.pricing_v1_line();

            CREATE FUNCTION public.pricing_v1_seal() RETURNS trigger LANGUAGE plpgsql
              SET search_path=pg_catalog,public AS $$
            DECLARE d public.business_documents; line_count integer;
            BEGIN
              SELECT * INTO d FROM public.business_documents WHERE "TenantId"=NEW."TenantId" AND "Id"=NEW."DocumentId";
              SELECT COUNT(*) INTO line_count FROM public.price_list_items WHERE "TenantId"=NEW."TenantId" AND "PriceListId"=NEW."Id";
              IF d."Status"<>'approved' OR line_count NOT BETWEEN 1 AND 100 OR
                line_count<>(SELECT COUNT(*) FROM public.document_lines WHERE "TenantId"=NEW."TenantId" AND "DocumentId"=d."Id") OR
                EXISTS(SELECT 1 FROM public.price_list_items l WHERE l."TenantId"=NEW."TenantId" AND l."PriceListId"=NEW."Id" AND NOT EXISTS(
                  SELECT 1 FROM public.document_lines dl WHERE dl."TenantId"=l."TenantId" AND dl."DocumentId"=d."Id" AND dl."ItemId"=l."ItemId"
                    AND dl."UnitPrice"=l."Price" AND dl."VatRate"=l."VatRate" AND dl."Unit"=l."Unit" AND dl."ItemNameSnapshot"=l."ItemName")) OR
                NOT EXISTS(SELECT 1 FROM public.document_approvals a WHERE a."TenantId"=d."TenantId" AND a."DocumentId"=d."Id" AND a."Decision"='approved' AND a."ContentHash"=d."ContentHash") THEN
                RAISE EXCEPTION 'Planned price receipt must match its approved document' USING ERRCODE='23514';
              END IF;
              RETURN NULL;
            END $$;
            CREATE CONSTRAINT TRIGGER pricing_seal AFTER INSERT ON public.price_lists
              DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.pricing_v1_seal();

            CREATE FUNCTION public.pricing_v1_document_seal() RETURNS trigger LANGUAGE plpgsql
              SET search_path=pg_catalog,public AS $$
            BEGIN
              IF OLD."Status"='approved' AND EXISTS(SELECT 1 FROM public.price_lists WHERE "TenantId"=OLD."TenantId" AND "DocumentId"=OLD."Id") THEN
                RAISE EXCEPTION 'Planned price document is sealed; create a new revision' USING ERRCODE='P0001';
              END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER pricing_document_seal BEFORE UPDATE ON public.business_documents
              FOR EACH ROW EXECUTE FUNCTION public.pricing_v1_document_seal();

            DO $$ DECLARE r record; BEGIN
              FOR r IN SELECT rolname FROM pg_roles WHERE NOT rolsuper AND NOT rolbypassrls
                AND has_table_privilege(oid,'public.business_documents','INSERT') LOOP
                EXECUTE format('GRANT SELECT,INSERT ON public.price_lists,public.price_list_items,public.price_resolution_snapshots TO %I',r.rolname);
              END LOOP;
            END $$;
            """);
    }
}
