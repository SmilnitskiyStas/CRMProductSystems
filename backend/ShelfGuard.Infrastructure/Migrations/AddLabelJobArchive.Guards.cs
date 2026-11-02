using Microsoft.EntityFrameworkCore.Migrations;
namespace ShelfGuard.Infrastructure.Migrations;
public partial class AddLabelJobArchive
{
    private static void InstallGuards(MigrationBuilder m)
    {
        foreach (var table in new[] { "label_test_jobs", "label_test_job_events", "label_test_job_receipts", "label_test_job_uploads" })
            m.Sql($"""
                ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON public.{table}
                  USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
                  WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
                CREATE TRIGGER label_archive_immutable BEFORE UPDATE OR DELETE ON public.{table}
                  FOR EACH ROW EXECUTE FUNCTION public.erp_v1_immutable();
                CREATE TRIGGER label_archive_no_truncate BEFORE TRUNCATE ON public.{table}
                  FOR EACH STATEMENT EXECUTE FUNCTION public.erp_v1_immutable();
                """);
        m.Sql("""
            CREATE FUNCTION public.label_archive_v1_event() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
            DECLARE previous public.label_test_job_events; owner_id uuid;
            BEGIN
              PERFORM pg_advisory_xact_lock(hashtextextended('labels.jobs.job:'||NEW."TenantId"::text||':'||NEW."JobId"::text,0));
              SELECT "CreatedBy" INTO owner_id FROM public.label_test_jobs WHERE "TenantId"=NEW."TenantId" AND "Id"=NEW."JobId";
              IF owner_id IS DISTINCT FROM NEW."ReportedBy" OR owner_id IS DISTINCT FROM NULLIF(current_setting('app.user_id',true),'')::uuid THEN
                RAISE EXCEPTION 'Label report owner mismatch' USING ERRCODE='23514';
              END IF;
              SELECT * INTO previous FROM public.label_test_job_events WHERE "TenantId"=NEW."TenantId" AND "JobId"=NEW."JobId" ORDER BY "Seq" DESC LIMIT 1;
              IF NOT FOUND THEN
                IF NEW."Seq"<>1 OR NEW."Status"<>'queued' OR NEW."Attempt"<>0 THEN RAISE EXCEPTION 'Label history must start queued' USING ERRCODE='23514'; END IF;
              ELSE
                IF NEW."Seq"<>previous."Seq"+1 OR NOT (
                  NEW."Status"='running' AND previous."Status" IN ('queued','failed','unknown') AND NEW."Attempt"=previous."Attempt"+1 OR
                  NEW."Status"='cancelled' AND previous."Status" IN ('queued','failed') AND NEW."Attempt"=previous."Attempt" OR
                  NEW."Status"='unknown' AND previous."Status"='running' AND NEW."Attempt"=previous."Attempt" OR
                  NEW."Status" IN ('failed','succeeded') AND previous."Status" IN ('running','unknown') AND NEW."Attempt"=previous."Attempt"
                ) THEN RAISE EXCEPTION 'Invalid label history transition' USING ERRCODE='23514'; END IF;
              END IF;
              IF (NEW."Payload"->>'seq')::integer IS DISTINCT FROM NEW."Seq" OR NEW."Payload"->>'status' IS DISTINCT FROM NEW."Status"
                OR (NEW."Payload"->>'attempt')::integer IS DISTINCT FROM NEW."Attempt" OR (NEW."Payload"->>'at')::timestamptz IS DISTINCT FROM NEW."OccurredAt" THEN
                RAISE EXCEPTION 'Label event payload mismatch' USING ERRCODE='23514';
              END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER label_archive_event_chain BEFORE INSERT ON public.label_test_job_events FOR EACH ROW EXECUTE FUNCTION public.label_archive_v1_event();

            CREATE FUNCTION public.label_archive_v1_receipt() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
            DECLARE j public.label_test_jobs; event_state text;
            BEGIN
              SELECT * INTO j FROM public.label_test_jobs WHERE "TenantId"=NEW."TenantId" AND "Id"=NEW."JobId";
              SELECT "Status" INTO event_state FROM public.label_test_job_events WHERE "TenantId"=NEW."TenantId" AND "JobId"=NEW."JobId" AND "Seq"=NEW."EventSeq";
              IF event_state IS DISTINCT FROM 'succeeded' OR j."CommandHash" IS DISTINCT FROM NEW."CommandHash" OR j."ArtifactHash" IS DISTINCT FROM NEW."ArtifactHash" OR j."Copies" IS DISTINCT FROM NEW."Copies"
                OR NEW."Payload"->>'adapter' IS DISTINCT FROM 'label-simulator.v1' OR NEW."Payload"->>'status' IS DISTINCT FROM 'accepted'
                OR NEW."Payload"->>'commandHash' IS DISTINCT FROM NEW."CommandHash" OR NEW."Payload"->>'artifactHash' IS DISTINCT FROM NEW."ArtifactHash"
                OR (NEW."Payload"->>'requestId')::uuid IS DISTINCT FROM NEW."JobId" OR (NEW."Payload"->>'copies')::integer IS DISTINCT FROM NEW."Copies" THEN
                RAISE EXCEPTION 'Invalid simulator report receipt' USING ERRCODE='23514';
              END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER label_archive_receipt BEFORE INSERT ON public.label_test_job_receipts FOR EACH ROW EXECUTE FUNCTION public.label_archive_v1_receipt();

            CREATE FUNCTION public.label_archive_v1_seal() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
            DECLARE job_id uuid; j public.label_test_jobs; head public.label_test_job_events; has_receipt boolean;
            BEGIN
              IF TG_TABLE_NAME='label_test_jobs' THEN job_id=NEW."Id"; ELSE job_id=NEW."JobId"; END IF;
              SELECT * INTO j FROM public.label_test_jobs WHERE "TenantId"=NEW."TenantId" AND "Id"=job_id;
              SELECT * INTO head FROM public.label_test_job_events WHERE "TenantId"=NEW."TenantId" AND "JobId"=job_id ORDER BY "Seq" DESC LIMIT 1;
              SELECT EXISTS(SELECT 1 FROM public.label_test_job_receipts WHERE "TenantId"=NEW."TenantId" AND "JobId"=job_id AND "EventSeq"=head."Seq") INTO has_receipt;
              IF head."Seq" IS NULL OR head."CheckpointPayload" IS NULL OR (head."Status"='succeeded') IS DISTINCT FROM has_receipt
                OR head."CheckpointPayload"->>'commandHash' IS DISTINCT FROM j."CommandHash"
                OR head."CheckpointPayload"->>'status' IS DISTINCT FROM head."Status"
                OR json_array_length(head."CheckpointPayload"->'events') IS DISTINCT FROM head."Seq"
                OR (head."CheckpointPayload"->>'attempts')::integer IS DISTINCT FROM head."Attempt" THEN
                RAISE EXCEPTION 'Incomplete label report checkpoint' USING ERRCODE='23514';
              END IF;
              RETURN NULL;
            END $$;
            CREATE CONSTRAINT TRIGGER label_archive_job_sealed AFTER INSERT ON public.label_test_jobs DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.label_archive_v1_seal();
            CREATE CONSTRAINT TRIGGER label_archive_event_sealed AFTER INSERT ON public.label_test_job_events DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.label_archive_v1_seal();
            """);
    }
}
