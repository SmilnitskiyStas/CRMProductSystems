using Microsoft.EntityFrameworkCore.Migrations;
namespace ShelfGuard.Infrastructure.Migrations;
public partial class AddLabelTemplateLibrary
{
    private static void InstallGuards(MigrationBuilder m)
    {
        foreach (var table in new[] { "label_template_revisions", "label_store_assignments" })
            m.Sql($"""
                ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON public.{table}
                  USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
                  WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
                CREATE TRIGGER label_immutable BEFORE UPDATE OR DELETE ON public.{table}
                  FOR EACH ROW EXECUTE FUNCTION public.erp_v1_immutable();
                CREATE TRIGGER label_no_truncate BEFORE TRUNCATE ON public.{table}
                  FOR EACH STATEMENT EXECUTE FUNCTION public.erp_v1_immutable();
                """);
        m.Sql("""
            CREATE FUNCTION public.labels_v1_chain() RETURNS trigger LANGUAGE plpgsql
              SET search_path=pg_catalog,public AS $$
            DECLARE previous_version integer;
            BEGIN
              PERFORM pg_advisory_xact_lock(hashtextextended('labels.templates.v1:'||NEW."TenantId"::text,0));
              IF TG_TABLE_NAME='label_template_revisions' THEN
                SELECT COALESCE(MAX("Version"),0) INTO previous_version FROM public.label_template_revisions
                  WHERE "TenantId"=NEW."TenantId" AND "TemplateKey"=NEW."TemplateKey";
                IF previous_version=0 AND NEW."TemplateKey"<>NEW."Id" THEN
                  RAISE EXCEPTION 'Invalid first template revision' USING ERRCODE='23514';
                END IF;
              ELSE
                SELECT COALESCE(MAX("Version"),0) INTO previous_version FROM public.label_store_assignments
                  WHERE "TenantId"=NEW."TenantId" AND "LocationId"=NEW."LocationId";
              END IF;
              IF NEW."Version"<>previous_version+1 THEN
                RAISE EXCEPTION 'Invalid label revision sequence' USING ERRCODE='23514';
              END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER label_template_chain BEFORE INSERT ON public.label_template_revisions
              FOR EACH ROW EXECUTE FUNCTION public.labels_v1_chain();
            CREATE TRIGGER label_assignment_chain BEFORE INSERT ON public.label_store_assignments
              FOR EACH ROW EXECUTE FUNCTION public.labels_v1_chain();
            """);
    }
}
