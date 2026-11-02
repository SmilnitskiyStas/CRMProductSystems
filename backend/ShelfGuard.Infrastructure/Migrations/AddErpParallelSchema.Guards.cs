using Microsoft.EntityFrameworkCore.Migrations;

namespace ShelfGuard.Infrastructure.Migrations;

public partial class AddErpParallelSchema
{
    // Migration-owned SQL: subsequent schema versions must not change these historical guards.
    private static void InstallGuards(MigrationBuilder migrationBuilder)
    {
        var tables = new[] { "business_documents", "document_lines", "document_postings", "document_links", "document_approvals",
            "inventory_ledger_entries", "inventory_balance_projections", "inventory_reservations", "inventory_reconciliations",
            "outbox_events", "inbox_deduplication" };
        foreach (var table in tables)
            migrationBuilder.Sql($"""
                ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON {table}
                  USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
                  WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
                CREATE POLICY worker_bypass ON {table}
                  USING (current_setting('app.role',true)='worker')
                  WITH CHECK (current_setting('app.role',true)='worker');
                """);

        migrationBuilder.Sql("""
            CREATE FUNCTION public.erp_v1_immutable() RETURNS trigger LANGUAGE plpgsql
              SET search_path=pg_catalog,public AS $$
            BEGIN
              RAISE EXCEPTION 'ERP history in % is append-only; use a linked reversal', TG_TABLE_NAME USING ERRCODE='P0001';
            END $$;

            CREATE FUNCTION public.erp_v1_document_guard() RETURNS trigger LANGUAGE plpgsql
              SET search_path=pg_catalog,public AS $$
            BEGIN
              IF TG_OP='DELETE' THEN
                IF OLD."Status"<>'draft' OR EXISTS (SELECT 1 FROM public.document_postings p WHERE p."TenantId"=OLD."TenantId" AND p."DocumentId"=OLD."Id") THEN
                  RAISE EXCEPTION 'Only an unposted draft may be deleted' USING ERRCODE='P0001';
                END IF;
                RETURN OLD;
              END IF;
              IF ROW(NEW."Id",NEW."TenantId",NEW."DocumentKey",NEW."Version",NEW."DocumentType",NEW."SourceSystem",NEW."SourceId",NEW."Currency",NEW."LegalEntityId",NEW."ReversesDocumentId",NEW."CreatedAt",NEW."CreatedBy")
                IS DISTINCT FROM ROW(OLD."Id",OLD."TenantId",OLD."DocumentKey",OLD."Version",OLD."DocumentType",OLD."SourceSystem",OLD."SourceId",OLD."Currency",OLD."LegalEntityId",OLD."ReversesDocumentId",OLD."CreatedAt",OLD."CreatedBy") THEN
                RAISE EXCEPTION 'Document identity/version is immutable; create a new version' USING ERRCODE='P0001';
              END IF;
              IF NEW."Status"<>OLD."Status" AND NOT (
                OLD."Status"='draft' AND NEW."Status" IN ('submitted','cancelled') OR
                OLD."Status"='submitted' AND NEW."Status" IN ('draft','approved','cancelled') OR
                OLD."Status"='approved' AND NEW."Status" IN ('submitted','posted','cancelled') OR
                OLD."Status"='posted' AND NEW."Status"='reversed') THEN
                RAISE EXCEPTION 'Invalid document status transition' USING ERRCODE='23514';
              END IF;
              IF OLD."Status" IN ('posted','reversed','cancelled') THEN
                IF (to_jsonb(NEW)-ARRAY['Status','UpdatedAt']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['Status','UpdatedAt']) THEN
                  RAISE EXCEPTION 'Closed document snapshot is immutable' USING ERRCODE='P0001';
                END IF;
              ELSIF EXISTS (SELECT 1 FROM public.document_postings p WHERE p."TenantId"=OLD."TenantId" AND p."DocumentId"=OLD."Id") THEN
                IF NOT (OLD."Status"='approved' AND NEW."Status"='posted') OR
                  (to_jsonb(NEW)-ARRAY['Status','UpdatedAt','PostedAt']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['Status','UpdatedAt','PostedAt']) THEN
                  RAISE EXCEPTION 'Posting already staged; only finalization is allowed' USING ERRCODE='P0001';
                END IF;
              END IF;
              RETURN NEW;
            END $$;

            CREATE FUNCTION public.erp_v1_line_guard() RETURNS trigger LANGUAGE plpgsql
              SET search_path=pg_catalog,public AS $$
            DECLARE d public.business_documents;
            BEGIN
              IF TG_OP='UPDATE' AND ROW(NEW."Id",NEW."TenantId",NEW."DocumentId",NEW."LineNumber",NEW."ItemId")
                IS DISTINCT FROM ROW(OLD."Id",OLD."TenantId",OLD."DocumentId",OLD."LineNumber",OLD."ItemId") THEN
                RAISE EXCEPTION 'Line identity is immutable' USING ERRCODE='P0001';
              END IF;
              SELECT * INTO d FROM public.business_documents
                WHERE "Id"=COALESCE(NEW."DocumentId",OLD."DocumentId") AND "TenantId"=COALESCE(NEW."TenantId",OLD."TenantId") FOR UPDATE;
              IF NOT FOUND THEN RAISE EXCEPTION 'Document scope mismatch' USING ERRCODE='23503'; END IF;
              IF d."Status"<>'draft' OR EXISTS (SELECT 1 FROM public.document_postings p WHERE p."TenantId"=d."TenantId" AND p."DocumentId"=d."Id") THEN
                RAISE EXCEPTION 'Only draft lines may change' USING ERRCODE='P0001';
              END IF;
              IF TG_OP='DELETE' THEN RETURN OLD; END IF;
              RETURN NEW;
            END $$;

            CREATE FUNCTION public.erp_v1_posting_guard() RETURNS trigger LANGUAGE plpgsql
              SET search_path=pg_catalog,public AS $$
            DECLARE d public.business_documents;
            BEGIN
              SELECT * INTO d FROM public.business_documents WHERE "TenantId"=NEW."TenantId" AND "Id"=NEW."DocumentId" FOR UPDATE;
              IF NOT FOUND THEN RAISE EXCEPTION 'Document scope mismatch' USING ERRCODE='23503'; END IF;
              IF d."Status"<>'approved' THEN RAISE EXCEPTION 'Only an approved version may be posted' USING ERRCODE='P0001'; END IF;
              RETURN NEW;
            END $$;

            CREATE FUNCTION public.erp_v1_ledger_guard() RETURNS trigger LANGUAGE plpgsql
              SET search_path=pg_catalog,public AS $$
            DECLARE p public.document_postings; d public.business_documents; original public.inventory_ledger_entries;
            BEGIN
              SELECT * INTO p FROM public.document_postings WHERE "TenantId"=NEW."TenantId" AND "Id"=NEW."PostingId";
              IF NOT FOUND THEN RAISE EXCEPTION 'Posting scope mismatch' USING ERRCODE='23503'; END IF;
              SELECT * INTO d FROM public.business_documents WHERE "TenantId"=NEW."TenantId" AND "Id"=p."DocumentId" FOR UPDATE;
              IF d."Status"<>'approved' THEN RAISE EXCEPTION 'Posting is sealed' USING ERRCODE='P0001'; END IF;
              IF NEW."Currency"<>d."Currency" THEN RAISE EXCEPTION 'Ledger currency mismatch' USING ERRCODE='23514'; END IF;
              IF p."ReversesPostingId" IS NULL THEN
                IF NEW."ReversesEntryId" IS NOT NULL THEN RAISE EXCEPTION 'Ordinary posting cannot reverse entries' USING ERRCODE='23514'; END IF;
              ELSE
                SELECT * INTO original FROM public.inventory_ledger_entries WHERE "TenantId"=NEW."TenantId" AND "Id"=NEW."ReversesEntryId";
                IF NOT FOUND OR original."PostingId"<>p."ReversesPostingId" OR
                  ROW(NEW."ItemId",NEW."LocationId",NEW."BatchId",NEW."ZoneId",NEW."QuantityDelta",NEW."UnitCost",NEW."ValueDelta",NEW."Currency",NEW."BatchNumberSnapshot",NEW."ExpiryDateSnapshot")
                  IS DISTINCT FROM ROW(original."ItemId",original."LocationId",original."BatchId",original."ZoneId",-original."QuantityDelta",original."UnitCost",-original."ValueDelta",original."Currency",original."BatchNumberSnapshot",original."ExpiryDateSnapshot") THEN
                  RAISE EXCEPTION 'Reversal must exactly compensate its original entry' USING ERRCODE='23514';
                END IF;
              END IF;
              RETURN NEW;
            END $$;

            CREATE FUNCTION public.erp_v1_outbox_guard() RETURNS trigger LANGUAGE plpgsql
              SET search_path=pg_catalog,public AS $$
            DECLARE d public.business_documents;
            BEGIN
              IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Retain outbox history' USING ERRCODE='P0001'; END IF;
              IF TG_OP='UPDATE' THEN
                IF (to_jsonb(NEW)-ARRAY['Status','Attempts','AvailableAt','LockToken','LockedUntil','PublishedAt','LastError'])
                  IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['Status','Attempts','AvailableAt','LockToken','LockedUntil','PublishedAt','LastError']) THEN
                  RAISE EXCEPTION 'Outbox envelope is immutable' USING ERRCODE='P0001';
                END IF;
              ELSIF NEW."PostingId" IS NOT NULL THEN
                SELECT d0.* INTO d FROM public.document_postings p JOIN public.business_documents d0
                  ON d0."TenantId"=p."TenantId" AND d0."Id"=p."DocumentId"
                  WHERE p."TenantId"=NEW."TenantId" AND p."Id"=NEW."PostingId" FOR UPDATE OF d0;
                IF NOT FOUND THEN RAISE EXCEPTION 'Posting scope mismatch' USING ERRCODE='23503'; END IF;
                IF d."Status"<>'approved' THEN RAISE EXCEPTION 'Posting is sealed' USING ERRCODE='P0001'; END IF;
                IF NEW."EventType"<>'inventory.document_posted' OR NEW."SchemaVersion"<>1 OR NEW."AggregateType"<>'business_document'
                  OR NEW."AggregateId"<>d."DocumentKey" OR NEW."AggregateVersion"<>d."Version" OR NEW."EventOrdinal"<>1 THEN
                  RAISE EXCEPTION 'Posting outbox envelope mismatch' USING ERRCODE='23514';
                END IF;
              END IF;
              RETURN NEW;
            END $$;

            CREATE FUNCTION public.erp_v1_check_posting() RETURNS trigger LANGUAGE plpgsql
              SET search_path=pg_catalog,public AS $$
            DECLARE d public.business_documents; p public.document_postings; o public.outbox_events;
              original public.document_postings; original_document public.business_documents; entries_count bigint;
              tenant uuid; document_id uuid;
            BEGIN
              tenant:=NEW."TenantId";
              IF TG_TABLE_NAME='business_documents' THEN document_id:=NEW."Id"; ELSE document_id:=NEW."DocumentId"; END IF;
              SELECT * INTO d FROM public.business_documents WHERE "TenantId"=tenant AND "Id"=document_id;
              IF NOT FOUND THEN RETURN NULL; END IF;
              SELECT * INTO p FROM public.document_postings WHERE "TenantId"=tenant AND "DocumentId"=document_id;
              IF NOT FOUND THEN
                IF d."Status" IN ('posted','reversed') THEN RAISE EXCEPTION 'Posted document requires a posting' USING ERRCODE='23514'; END IF;
                RETURN NULL;
              END IF;
              IF d."Status" NOT IN ('posted','reversed') THEN RAISE EXCEPTION 'Posting must finalize the document in the same transaction' USING ERRCODE='23514'; END IF;
              SELECT COUNT(*) INTO entries_count FROM public.inventory_ledger_entries WHERE "TenantId"=tenant AND "PostingId"=p."Id";
              IF entries_count=0 THEN RAISE EXCEPTION 'Inventory posting requires ledger entries' USING ERRCODE='23514'; END IF;
              SELECT * INTO o FROM public.outbox_events WHERE "TenantId"=tenant AND "PostingId"=p."Id";
              IF NOT FOUND THEN RAISE EXCEPTION 'Posting requires an outbox event in the same transaction' USING ERRCODE='23514'; END IF;
              IF o."Payload"->>'documentId' IS DISTINCT FROM d."Id"::text OR o."Payload"->>'postingId' IS DISTINCT FROM p."Id"::text
                OR o."Payload"->>'documentKey' IS DISTINCT FROM d."DocumentKey"::text OR o."Payload"->>'documentVersion' IS DISTINCT FROM d."Version"::text
                OR (o."Payload"->>'reversesPostingId')::uuid IS DISTINCT FROM p."ReversesPostingId"
                OR jsonb_typeof(o."Payload"->'entries') IS DISTINCT FROM 'array' THEN
                RAISE EXCEPTION 'Posting event payload identity mismatch' USING ERRCODE='23514';
              END IF;
              IF jsonb_array_length(o."Payload"->'entries')<>entries_count OR
                (SELECT COUNT(DISTINCT e->>'entryId') FROM jsonb_array_elements(o."Payload"->'entries') e)<>entries_count OR EXISTS (
                SELECT 1 FROM jsonb_to_recordset(o."Payload"->'entries') AS e("entryId" uuid,"entryNumber" int,"itemId" uuid,"locationId" uuid,"batchId" uuid,
                  "zoneId" uuid,"quantityDelta" numeric,"unitCost" numeric,"valueDelta" numeric,"currency" text,"reversesEntryId" uuid)
                LEFT JOIN public.inventory_ledger_entries l ON l."Id"=e."entryId" AND l."TenantId"=tenant AND l."PostingId"=p."Id"
                WHERE l."Id" IS NULL OR ROW(e."entryNumber",e."itemId",e."locationId",e."batchId",e."zoneId",e."quantityDelta",e."unitCost",e."valueDelta",e."currency",e."reversesEntryId")
                  IS DISTINCT FROM ROW(l."EntryNumber",l."ItemId",l."LocationId",l."BatchId",l."ZoneId",l."QuantityDelta",l."UnitCost",l."ValueDelta",l."Currency",l."ReversesEntryId")) THEN
                RAISE EXCEPTION 'Posting event must describe exactly the committed ledger entries' USING ERRCODE='23514';
              END IF;
              IF p."ReversesPostingId" IS NOT NULL THEN
                SELECT * INTO original FROM public.document_postings WHERE "TenantId"=tenant AND "Id"=p."ReversesPostingId";
                SELECT * INTO original_document FROM public.business_documents WHERE "TenantId"=tenant AND "Id"=original."DocumentId";
                IF d."ReversesDocumentId" IS DISTINCT FROM original."DocumentId" OR original_document."Status"<>'reversed' OR
                  entries_count<>(SELECT COUNT(*) FROM public.inventory_ledger_entries WHERE "TenantId"=tenant AND "PostingId"=original."Id") THEN
                  RAISE EXCEPTION 'Full reversal must compensate all entries and mark the original document reversed' USING ERRCODE='23514';
                END IF;
              ELSIF d."ReversesDocumentId" IS NOT NULL THEN
                RAISE EXCEPTION 'Reversal document requires a reversal posting' USING ERRCODE='23514';
              END IF;
              IF d."Status"='reversed' AND NOT EXISTS (SELECT 1 FROM public.document_postings r WHERE r."TenantId"=tenant AND r."ReversesPostingId"=p."Id") THEN
                RAISE EXCEPTION 'Reversed status requires a compensating posting' USING ERRCODE='23514';
              END IF;
              RETURN NULL;
            END $$;

            CREATE TRIGGER erp_document_guard BEFORE UPDATE OR DELETE ON public.business_documents FOR EACH ROW EXECUTE FUNCTION public.erp_v1_document_guard();
            CREATE TRIGGER erp_line_guard BEFORE INSERT OR UPDATE OR DELETE ON public.document_lines FOR EACH ROW EXECUTE FUNCTION public.erp_v1_line_guard();
            CREATE TRIGGER erp_posting_guard BEFORE INSERT ON public.document_postings FOR EACH ROW EXECUTE FUNCTION public.erp_v1_posting_guard();
            CREATE TRIGGER erp_ledger_guard BEFORE INSERT ON public.inventory_ledger_entries FOR EACH ROW EXECUTE FUNCTION public.erp_v1_ledger_guard();
            CREATE TRIGGER erp_outbox_guard BEFORE INSERT OR UPDATE OR DELETE ON public.outbox_events FOR EACH ROW EXECUTE FUNCTION public.erp_v1_outbox_guard();
            CREATE CONSTRAINT TRIGGER erp_document_complete AFTER INSERT OR UPDATE ON public.business_documents
              DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.erp_v1_check_posting();
            CREATE CONSTRAINT TRIGGER erp_posting_complete AFTER INSERT ON public.document_postings
              DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.erp_v1_check_posting();
            """);
        foreach (var table in new[] { "document_postings", "document_links", "document_approvals", "inventory_ledger_entries", "inventory_reconciliations", "inbox_deduplication" })
            migrationBuilder.Sql($"CREATE TRIGGER erp_history_guard BEFORE UPDATE OR DELETE ON {table} FOR EACH ROW EXECUTE FUNCTION public.erp_v1_immutable();");
        foreach (var table in tables)
            migrationBuilder.Sql($"CREATE TRIGGER erp_no_truncate BEFORE TRUNCATE ON {table} FOR EACH STATEMENT EXECUTE FUNCTION public.erp_v1_immutable();");
    }
}
