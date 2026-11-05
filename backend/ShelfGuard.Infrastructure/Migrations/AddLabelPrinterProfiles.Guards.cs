using Microsoft.EntityFrameworkCore.Migrations;
namespace ShelfGuard.Infrastructure.Migrations;
public partial class AddLabelPrinterProfiles
{
    private static void InstallGuards(MigrationBuilder m) => m.Sql("""
        ALTER TABLE public.label_printer_profile_revisions ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.label_printer_profile_revisions FORCE ROW LEVEL SECURITY;
        CREATE POLICY tenant_isolation ON public.label_printer_profile_revisions
          USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
          WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
        CREATE TRIGGER printer_profile_immutable BEFORE UPDATE OR DELETE ON public.label_printer_profile_revisions FOR EACH ROW EXECUTE FUNCTION public.erp_v1_immutable();
        CREATE TRIGGER printer_profile_no_truncate BEFORE TRUNCATE ON public.label_printer_profile_revisions FOR EACH STATEMENT EXECUTE FUNCTION public.erp_v1_immutable();
        CREATE FUNCTION public.label_printer_v1_chain() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
        DECLARE previous public.label_printer_profile_revisions;
        BEGIN
          PERFORM pg_advisory_xact_lock(hashtextextended('labels.printers.v1:'||NEW."TenantId"::text,0));
          SELECT * INTO previous FROM public.label_printer_profile_revisions WHERE "TenantId"=NEW."TenantId" AND "ProfileKey"=NEW."ProfileKey" ORDER BY "Version" DESC LIMIT 1;
          IF NOT FOUND THEN
            IF NEW."Version"<>1 OR NEW."ProfileKey"<>NEW."Id" THEN RAISE EXCEPTION 'Invalid first printer profile revision' USING ERRCODE='23514'; END IF;
          ELSE
            IF NEW."Version"<>previous."Version"+1 OR NEW."LocationId"<>previous."LocationId" THEN RAISE EXCEPTION 'Invalid printer profile chain or location move' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER printer_profile_chain BEFORE INSERT ON public.label_printer_profile_revisions FOR EACH ROW EXECUTE FUNCTION public.label_printer_v1_chain();
        CREATE FUNCTION public.label_printer_v1_job() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
        DECLARE profile public.label_printer_profile_revisions;
        BEGIN
          IF NEW."InitialPayload"->>'protocol' IS DISTINCT FROM NEW."JobProtocol" THEN RAISE EXCEPTION 'Job protocol mismatch' USING ERRCODE='23514'; END IF;
          IF NEW."JobProtocol"='label-job.v2' THEN
            SELECT * INTO profile FROM public.label_printer_profile_revisions WHERE "TenantId"=NEW."TenantId" AND "Id"=NEW."PrinterRevisionId" AND "LocationId"=NEW."LocationId";
            IF NOT FOUND OR (NEW."InitialPayload"->'printer'->>'revisionId')::uuid IS DISTINCT FROM profile."Id"
              OR NEW."InitialPayload"->'printer'->>'contentHash' IS DISTINCT FROM profile."ContentHash"
              OR profile."Payload"->>'transport' IS DISTINCT FROM 'simulation' THEN
              RAISE EXCEPTION 'Invalid simulation printer binding' USING ERRCODE='23514';
            END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER label_printer_job BEFORE INSERT ON public.label_test_jobs FOR EACH ROW EXECUTE FUNCTION public.label_printer_v1_job();
        """);
}
