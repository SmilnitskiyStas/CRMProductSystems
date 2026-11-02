START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    ALTER TABLE product_stock ADD CONSTRAINT "AK_product_stock_TenantId_Id_ProductId_LocationId" UNIQUE ("TenantId", "Id", "ProductId", "LocationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    ALTER TABLE locations ADD CONSTRAINT "AK_locations_TenantId_Id" UNIQUE ("TenantId", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    ALTER TABLE location_zones ADD CONSTRAINT "AK_location_zones_LocationId_Id" UNIQUE ("LocationId", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    ALTER TABLE legal_entities ADD CONSTRAINT "AK_legal_entities_TenantId_Id" UNIQUE ("TenantId", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    ALTER TABLE items ADD CONSTRAINT "AK_items_TenantId_Id" UNIQUE ("TenantId", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TABLE business_documents (
        "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "TenantId" uuid NOT NULL,
        "DocumentKey" uuid NOT NULL,
        "Version" integer NOT NULL,
        "DocumentType" character varying(64) NOT NULL,
        "Number" character varying(100),
        "Status" character varying(20) NOT NULL DEFAULT 'draft',
        "LegalEntityId" uuid,
        "SourceSystem" character varying(64) NOT NULL,
        "SourceId" character varying(200),
        "Currency" character varying(3) NOT NULL,
        "ReversesDocumentId" uuid,
        "CreatedBy" uuid,
        "ContentHash" character varying(64),
        "OccurredAt" timestamp with time zone,
        "PostedAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
        "UpdatedAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
        CONSTRAINT "PK_business_documents" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_business_documents_TenantId_Id" UNIQUE ("TenantId", "Id"),
        CONSTRAINT "CK_business_documents_currency" CHECK ("Currency" ~ '^[A-Z]{3}$'),
        CONSTRAINT "CK_business_documents_hash" CHECK ("ContentHash" IS NULL OR "ContentHash" ~ '^[0-9a-f]{64}$'),
        CONSTRAINT "CK_business_documents_identity" CHECK ("DocumentKey"<>'00000000-0000-0000-0000-000000000000'::uuid AND length(btrim("DocumentType"))>0 AND length(btrim("SourceSystem"))>0 AND ("ReversesDocumentId" IS NULL OR "ReversesDocumentId"<>"Id")),
        CONSTRAINT "CK_business_documents_posting_snapshot" CHECK (("Status" IN ('posted','reversed')) = ("PostedAt" IS NOT NULL) AND ("Status" NOT IN ('posted','reversed') OR "ContentHash" IS NOT NULL)),
        CONSTRAINT "CK_business_documents_scope" CHECK ("Id"<>'00000000-0000-0000-0000-000000000000'::uuid AND "TenantId"<>'00000000-0000-0000-0000-000000000000'::uuid),
        CONSTRAINT "CK_business_documents_status" CHECK ("Status" IN ('draft','submitted','approved','posted','cancelled','reversed')),
        CONSTRAINT "CK_business_documents_version" CHECK ("Version">0),
        CONSTRAINT "FK_business_documents_business_documents_TenantId_ReversesDocu~" FOREIGN KEY ("TenantId", "ReversesDocumentId") REFERENCES business_documents ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_business_documents_legal_entities_TenantId_LegalEntityId" FOREIGN KEY ("TenantId", "LegalEntityId") REFERENCES legal_entities ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_business_documents_tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES tenants ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TABLE inbox_deduplication (
        "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "TenantId" uuid NOT NULL,
        "Consumer" character varying(128) NOT NULL,
        "MessageId" uuid NOT NULL,
        "PayloadHash" character varying(64) NOT NULL,
        "Result" jsonb,
        "ProcessedAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
        CONSTRAINT "PK_inbox_deduplication" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_inbox_deduplication_identity" CHECK ("MessageId"<>'00000000-0000-0000-0000-000000000000'::uuid AND length(btrim("Consumer"))>0 AND "PayloadHash" ~ '^[0-9a-f]{64}$'),
        CONSTRAINT "CK_inbox_deduplication_scope" CHECK ("Id"<>'00000000-0000-0000-0000-000000000000'::uuid AND "TenantId"<>'00000000-0000-0000-0000-000000000000'::uuid),
        CONSTRAINT "FK_inbox_deduplication_tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES tenants ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TABLE inventory_reconciliations (
        "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "TenantId" uuid NOT NULL,
        "RunId" uuid NOT NULL,
        "ItemId" uuid NOT NULL,
        "LocationId" uuid NOT NULL,
        "BatchId" uuid NOT NULL,
        "LegacyQuantity" numeric(18,4),
        "LedgerQuantity" numeric(18,4),
        "Difference" numeric(18,4) GENERATED ALWAYS AS ("LegacyQuantity"-"LedgerQuantity") STORED,
        "Status" character varying(20) NOT NULL,
        "Explanation" character varying(2000),
        "AsOf" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
        CONSTRAINT "PK_inventory_reconciliations" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_inventory_reconciliations_finite" CHECK (("LegacyQuantity" IS NULL OR "LegacyQuantity"<>'NaN'::numeric) AND ("LedgerQuantity" IS NULL OR "LedgerQuantity"<>'NaN'::numeric) AND ("Difference" IS NULL OR "Difference"<>'NaN'::numeric)),
        CONSTRAINT "CK_inventory_reconciliations_result" CHECK (("Status"='unverifiable' AND ("LegacyQuantity" IS NULL OR "LedgerQuantity" IS NULL)) OR ("LegacyQuantity" IS NOT NULL AND "LedgerQuantity" IS NOT NULL AND (("Status"='matched' AND "LegacyQuantity"="LedgerQuantity") OR ("Status"='discrepancy' AND "LegacyQuantity"<>"LedgerQuantity")))),
        CONSTRAINT "CK_inventory_reconciliations_scope" CHECK ("Id"<>'00000000-0000-0000-0000-000000000000'::uuid AND "TenantId"<>'00000000-0000-0000-0000-000000000000'::uuid),
        CONSTRAINT "FK_inventory_reconciliations_items_TenantId_ItemId" FOREIGN KEY ("TenantId", "ItemId") REFERENCES items ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_inventory_reconciliations_locations_TenantId_LocationId" FOREIGN KEY ("TenantId", "LocationId") REFERENCES locations ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_inventory_reconciliations_product_stock_TenantId_BatchId_It~" FOREIGN KEY ("TenantId", "BatchId", "ItemId", "LocationId") REFERENCES product_stock ("TenantId", "Id", "ProductId", "LocationId") ON DELETE RESTRICT,
        CONSTRAINT "FK_inventory_reconciliations_tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES tenants ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TABLE document_approvals (
        "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "TenantId" uuid NOT NULL,
        "DocumentId" uuid NOT NULL,
        "Stage" integer NOT NULL,
        "ApproverId" uuid NOT NULL,
        "Decision" character varying(20) NOT NULL,
        "ContentHash" character varying(64) NOT NULL,
        "Reason" character varying(2000),
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
        CONSTRAINT "PK_document_approvals" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_document_approvals_decision" CHECK ("Stage">0 AND "ApproverId"<>'00000000-0000-0000-0000-000000000000'::uuid AND "Decision" IN ('approved','rejected') AND "ContentHash" ~ '^[0-9a-f]{64}$'),
        CONSTRAINT "CK_document_approvals_scope" CHECK ("Id"<>'00000000-0000-0000-0000-000000000000'::uuid AND "TenantId"<>'00000000-0000-0000-0000-000000000000'::uuid),
        CONSTRAINT "FK_document_approvals_business_documents_TenantId_DocumentId" FOREIGN KEY ("TenantId", "DocumentId") REFERENCES business_documents ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_document_approvals_tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES tenants ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TABLE document_lines (
        "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "TenantId" uuid NOT NULL,
        "DocumentId" uuid NOT NULL,
        "LineNumber" integer NOT NULL,
        "ItemId" uuid NOT NULL,
        "FromLocationId" uuid,
        "ToLocationId" uuid,
        "Quantity" numeric(18,4) NOT NULL,
        "Unit" character varying(24) NOT NULL,
        "UnitPrice" numeric(18,4),
        "VatRate" numeric(5,2),
        "VatAmount" numeric(18,2),
        "UnitCost" numeric(18,4),
        "ItemNameSnapshot" character varying(512) NOT NULL,
        "BarcodeSnapshot" character varying(128),
        "BatchNumberSnapshot" character varying(100),
        "ExpiryDateSnapshot" date,
        "Metadata" jsonb,
        CONSTRAINT "PK_document_lines" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_document_lines_TenantId_DocumentId_Id_ItemId" UNIQUE ("TenantId", "DocumentId", "Id", "ItemId"),
        CONSTRAINT "CK_document_lines_finite" CHECK (("Quantity" IS NULL OR "Quantity"<>'NaN'::numeric) AND ("UnitPrice" IS NULL OR "UnitPrice"<>'NaN'::numeric) AND ("VatRate" IS NULL OR "VatRate"<>'NaN'::numeric) AND ("VatAmount" IS NULL OR "VatAmount"<>'NaN'::numeric) AND ("UnitCost" IS NULL OR "UnitCost"<>'NaN'::numeric)),
        CONSTRAINT "CK_document_lines_scope" CHECK ("Id"<>'00000000-0000-0000-0000-000000000000'::uuid AND "TenantId"<>'00000000-0000-0000-0000-000000000000'::uuid),
        CONSTRAINT "CK_document_lines_snapshot" CHECK (length(btrim("Unit"))>0 AND length(btrim("ItemNameSnapshot"))>0),
        CONSTRAINT "CK_document_lines_values" CHECK ("LineNumber">0 AND "Quantity">0 AND ("UnitPrice" IS NULL OR "UnitPrice">=0) AND ("UnitCost" IS NULL OR "UnitCost">=0) AND ("VatRate" IS NULL OR "VatRate" BETWEEN 0 AND 100) AND ("VatAmount" IS NULL OR "VatAmount">=0)),
        CONSTRAINT "FK_document_lines_business_documents_TenantId_DocumentId" FOREIGN KEY ("TenantId", "DocumentId") REFERENCES business_documents ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_document_lines_items_TenantId_ItemId" FOREIGN KEY ("TenantId", "ItemId") REFERENCES items ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_document_lines_locations_TenantId_FromLocationId" FOREIGN KEY ("TenantId", "FromLocationId") REFERENCES locations ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_document_lines_locations_TenantId_ToLocationId" FOREIGN KEY ("TenantId", "ToLocationId") REFERENCES locations ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_document_lines_tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES tenants ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TABLE document_links (
        "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "TenantId" uuid NOT NULL,
        "FromDocumentId" uuid NOT NULL,
        "ToDocumentId" uuid NOT NULL,
        "RelationType" character varying(50) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
        CONSTRAINT "PK_document_links" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_document_links_relation" CHECK ("FromDocumentId"<>"ToDocumentId" AND length(btrim("RelationType"))>0),
        CONSTRAINT "CK_document_links_scope" CHECK ("Id"<>'00000000-0000-0000-0000-000000000000'::uuid AND "TenantId"<>'00000000-0000-0000-0000-000000000000'::uuid),
        CONSTRAINT "FK_document_links_business_documents_TenantId_FromDocumentId" FOREIGN KEY ("TenantId", "FromDocumentId") REFERENCES business_documents ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_document_links_business_documents_TenantId_ToDocumentId" FOREIGN KEY ("TenantId", "ToDocumentId") REFERENCES business_documents ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_document_links_tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES tenants ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TABLE document_postings (
        "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "TenantId" uuid NOT NULL,
        "DocumentId" uuid NOT NULL,
        "IdempotencyKey" character varying(160) NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        "ReversesPostingId" uuid,
        "PerformedBy" uuid,
        "PostedAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
        CONSTRAINT "PK_document_postings" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_document_postings_TenantId_DocumentId_Id" UNIQUE ("TenantId", "DocumentId", "Id"),
        CONSTRAINT "AK_document_postings_TenantId_Id" UNIQUE ("TenantId", "Id"),
        CONSTRAINT "CK_document_postings_identity" CHECK (length(btrim("IdempotencyKey"))>0 AND "RequestHash" ~ '^[0-9a-f]{64}$' AND ("ReversesPostingId" IS NULL OR "ReversesPostingId"<>"Id")),
        CONSTRAINT "CK_document_postings_scope" CHECK ("Id"<>'00000000-0000-0000-0000-000000000000'::uuid AND "TenantId"<>'00000000-0000-0000-0000-000000000000'::uuid),
        CONSTRAINT "FK_document_postings_business_documents_TenantId_DocumentId" FOREIGN KEY ("TenantId", "DocumentId") REFERENCES business_documents ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_document_postings_document_postings_TenantId_ReversesPostin~" FOREIGN KEY ("TenantId", "ReversesPostingId") REFERENCES document_postings ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_document_postings_tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES tenants ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TABLE inventory_reservations (
        "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "TenantId" uuid NOT NULL,
        "DocumentId" uuid NOT NULL,
        "DocumentLineId" uuid NOT NULL,
        "ItemId" uuid NOT NULL,
        "LocationId" uuid NOT NULL,
        "BatchId" uuid,
        "IdempotencyKey" character varying(160) NOT NULL,
        "Quantity" numeric(18,4) NOT NULL,
        "RemainingQuantity" numeric(18,4) NOT NULL,
        "Status" character varying(20) NOT NULL,
        "ExpiresAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
        "UpdatedAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
        CONSTRAINT "PK_inventory_reservations" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_inventory_reservations_finite" CHECK (("Quantity" IS NULL OR "Quantity"<>'NaN'::numeric) AND ("RemainingQuantity" IS NULL OR "RemainingQuantity"<>'NaN'::numeric)),
        CONSTRAINT "CK_inventory_reservations_quantities" CHECK ("Quantity">0 AND "RemainingQuantity">=0 AND "RemainingQuantity"<="Quantity" AND length(btrim("IdempotencyKey"))>0),
        CONSTRAINT "CK_inventory_reservations_scope" CHECK ("Id"<>'00000000-0000-0000-0000-000000000000'::uuid AND "TenantId"<>'00000000-0000-0000-0000-000000000000'::uuid),
        CONSTRAINT "CK_inventory_reservations_status" CHECK (("Status"='active' AND "RemainingQuantity">0) OR ("Status" IN ('consumed','released','expired') AND "RemainingQuantity"=0)),
        CONSTRAINT "FK_inventory_reservations_business_documents_TenantId_Document~" FOREIGN KEY ("TenantId", "DocumentId") REFERENCES business_documents ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_inventory_reservations_document_lines_TenantId_DocumentId_D~" FOREIGN KEY ("TenantId", "DocumentId", "DocumentLineId", "ItemId") REFERENCES document_lines ("TenantId", "DocumentId", "Id", "ItemId") ON DELETE RESTRICT,
        CONSTRAINT "FK_inventory_reservations_items_TenantId_ItemId" FOREIGN KEY ("TenantId", "ItemId") REFERENCES items ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_inventory_reservations_locations_TenantId_LocationId" FOREIGN KEY ("TenantId", "LocationId") REFERENCES locations ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_inventory_reservations_product_stock_TenantId_BatchId_ItemI~" FOREIGN KEY ("TenantId", "BatchId", "ItemId", "LocationId") REFERENCES product_stock ("TenantId", "Id", "ProductId", "LocationId") ON DELETE RESTRICT,
        CONSTRAINT "FK_inventory_reservations_tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES tenants ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TABLE inventory_ledger_entries (
        "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "TenantId" uuid NOT NULL,
        "DocumentId" uuid NOT NULL,
        "PostingId" uuid NOT NULL,
        "DocumentLineId" uuid NOT NULL,
        "EntryNumber" integer NOT NULL,
        "ItemId" uuid NOT NULL,
        "LocationId" uuid NOT NULL,
        "BatchId" uuid NOT NULL,
        "ZoneId" uuid,
        "QuantityDelta" numeric(18,4) NOT NULL,
        "QuantityBefore" numeric(18,4),
        "QuantityAfter" numeric(18,4),
        "UnitCost" numeric(18,4),
        "ValueDelta" numeric(18,4),
        "Currency" character varying(3) NOT NULL,
        "BatchNumberSnapshot" character varying(100),
        "ExpiryDateSnapshot" date,
        "ReversesEntryId" uuid,
        "OccurredAt" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
        CONSTRAINT "PK_inventory_ledger_entries" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_inventory_ledger_entries_TenantId_BatchId_Id" UNIQUE ("TenantId", "BatchId", "Id"),
        CONSTRAINT "AK_inventory_ledger_entries_TenantId_Id" UNIQUE ("TenantId", "Id"),
        CONSTRAINT "CK_inventory_ledger_entries_balance" CHECK (("QuantityBefore" IS NULL AND "QuantityAfter" IS NULL) OR ("QuantityBefore" IS NOT NULL AND "QuantityAfter" IS NOT NULL AND "QuantityBefore">=0 AND "QuantityAfter">=0 AND "QuantityAfter"="QuantityBefore"+"QuantityDelta")),
        CONSTRAINT "CK_inventory_ledger_entries_delta" CHECK ("EntryNumber">0 AND "QuantityDelta"<>0 AND "Currency" ~ '^[A-Z]{3}$' AND ("ReversesEntryId" IS NULL OR "ReversesEntryId"<>"Id")),
        CONSTRAINT "CK_inventory_ledger_entries_finite" CHECK (("QuantityDelta" IS NULL OR "QuantityDelta"<>'NaN'::numeric) AND ("QuantityBefore" IS NULL OR "QuantityBefore"<>'NaN'::numeric) AND ("QuantityAfter" IS NULL OR "QuantityAfter"<>'NaN'::numeric) AND ("UnitCost" IS NULL OR "UnitCost"<>'NaN'::numeric) AND ("ValueDelta" IS NULL OR "ValueDelta"<>'NaN'::numeric)),
        CONSTRAINT "CK_inventory_ledger_entries_scope" CHECK ("Id"<>'00000000-0000-0000-0000-000000000000'::uuid AND "TenantId"<>'00000000-0000-0000-0000-000000000000'::uuid),
        CONSTRAINT "CK_inventory_ledger_entries_value" CHECK (("UnitCost" IS NULL OR "UnitCost">=0) AND ("ValueDelta" IS NULL OR ("UnitCost" IS NOT NULL AND "ValueDelta"=round("QuantityDelta"*"UnitCost",4)))),
        CONSTRAINT "FK_inventory_ledger_entries_document_lines_TenantId_DocumentId~" FOREIGN KEY ("TenantId", "DocumentId", "DocumentLineId", "ItemId") REFERENCES document_lines ("TenantId", "DocumentId", "Id", "ItemId") ON DELETE RESTRICT,
        CONSTRAINT "FK_inventory_ledger_entries_document_postings_TenantId_Documen~" FOREIGN KEY ("TenantId", "DocumentId", "PostingId") REFERENCES document_postings ("TenantId", "DocumentId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_inventory_ledger_entries_inventory_ledger_entries_TenantId_~" FOREIGN KEY ("TenantId", "ReversesEntryId") REFERENCES inventory_ledger_entries ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_inventory_ledger_entries_items_TenantId_ItemId" FOREIGN KEY ("TenantId", "ItemId") REFERENCES items ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_inventory_ledger_entries_location_zones_LocationId_ZoneId" FOREIGN KEY ("LocationId", "ZoneId") REFERENCES location_zones ("LocationId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_inventory_ledger_entries_locations_TenantId_LocationId" FOREIGN KEY ("TenantId", "LocationId") REFERENCES locations ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_inventory_ledger_entries_product_stock_TenantId_BatchId_Ite~" FOREIGN KEY ("TenantId", "BatchId", "ItemId", "LocationId") REFERENCES product_stock ("TenantId", "Id", "ProductId", "LocationId") ON DELETE RESTRICT,
        CONSTRAINT "FK_inventory_ledger_entries_tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES tenants ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TABLE outbox_events (
        "DeliveryScope" character varying(20) NOT NULL DEFAULT 'live',
        "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "TenantId" uuid NOT NULL,
        "PostingId" uuid,
        "EventType" character varying(128) NOT NULL,
        "SchemaVersion" integer NOT NULL,
        "AggregateType" character varying(64) NOT NULL,
        "AggregateId" uuid NOT NULL,
        "AggregateVersion" integer NOT NULL,
        "EventOrdinal" integer NOT NULL,
        "Payload" jsonb NOT NULL,
        "CorrelationId" uuid,
        "CausationId" uuid,
        "OccurredAt" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
        "Status" character varying(20) NOT NULL DEFAULT 'pending',
        "Attempts" integer NOT NULL,
        "AvailableAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
        "LockToken" uuid,
        "LockedUntil" timestamp with time zone,
        "PublishedAt" timestamp with time zone,
        "LastError" character varying(2000),
        CONSTRAINT "PK_outbox_events" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_outbox_events_delivery" CHECK ("Attempts">=0 AND (("DeliveryScope"='live' AND "Status" IN ('pending','processing','published','dead')) OR ("DeliveryScope"='historical' AND "Status"='suppressed' AND "Attempts"=0)) AND (("Status"='processing' AND "LockToken" IS NOT NULL AND "LockedUntil" IS NOT NULL) OR ("Status"<>'processing' AND "LockToken" IS NULL AND "LockedUntil" IS NULL)) AND (("Status"='published')=("PublishedAt" IS NOT NULL))),
        CONSTRAINT "CK_outbox_events_envelope" CHECK ("AggregateId"<>'00000000-0000-0000-0000-000000000000'::uuid AND "SchemaVersion">0 AND "AggregateVersion">0 AND "EventOrdinal">0 AND length(btrim("EventType"))>0 AND length(btrim("AggregateType"))>0 AND jsonb_typeof("Payload")='object'),
        CONSTRAINT "CK_outbox_events_scope" CHECK ("Id"<>'00000000-0000-0000-0000-000000000000'::uuid AND "TenantId"<>'00000000-0000-0000-0000-000000000000'::uuid),
        CONSTRAINT "FK_outbox_events_document_postings_TenantId_PostingId" FOREIGN KEY ("TenantId", "PostingId") REFERENCES document_postings ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_outbox_events_tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES tenants ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TABLE inventory_balance_projections (
        "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "TenantId" uuid NOT NULL,
        "ItemId" uuid NOT NULL,
        "LocationId" uuid NOT NULL,
        "BatchId" uuid NOT NULL,
        "Quantity" numeric(18,4) NOT NULL,
        "ReservedQuantity" numeric(18,4) NOT NULL,
        "LastLedgerEntryId" uuid,
        "UpdatedAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
        CONSTRAINT "PK_inventory_balance_projections" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_inventory_balance_projections_finite" CHECK (("Quantity" IS NULL OR "Quantity"<>'NaN'::numeric) AND ("ReservedQuantity" IS NULL OR "ReservedQuantity"<>'NaN'::numeric)),
        CONSTRAINT "CK_inventory_balance_projections_quantities" CHECK ("Quantity">=0 AND "ReservedQuantity">=0 AND "ReservedQuantity"<="Quantity"),
        CONSTRAINT "CK_inventory_balance_projections_scope" CHECK ("Id"<>'00000000-0000-0000-0000-000000000000'::uuid AND "TenantId"<>'00000000-0000-0000-0000-000000000000'::uuid),
        CONSTRAINT "FK_inventory_balance_projections_inventory_ledger_entries_Tena~" FOREIGN KEY ("TenantId", "BatchId", "LastLedgerEntryId") REFERENCES inventory_ledger_entries ("TenantId", "BatchId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_inventory_balance_projections_items_TenantId_ItemId" FOREIGN KEY ("TenantId", "ItemId") REFERENCES items ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_inventory_balance_projections_locations_TenantId_LocationId" FOREIGN KEY ("TenantId", "LocationId") REFERENCES locations ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_inventory_balance_projections_product_stock_TenantId_BatchI~" FOREIGN KEY ("TenantId", "BatchId", "ItemId", "LocationId") REFERENCES product_stock ("TenantId", "Id", "ProductId", "LocationId") ON DELETE RESTRICT,
        CONSTRAINT "FK_inventory_balance_projections_tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES tenants ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE UNIQUE INDEX "IX_business_documents_TenantId_DocumentKey_Version" ON business_documents ("TenantId", "DocumentKey", "Version");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_business_documents_TenantId_DocumentType_Number" ON business_documents ("TenantId", "DocumentType", "Number");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_business_documents_TenantId_LegalEntityId" ON business_documents ("TenantId", "LegalEntityId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_business_documents_TenantId_ReversesDocumentId" ON business_documents ("TenantId", "ReversesDocumentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE UNIQUE INDEX "IX_business_documents_TenantId_SourceSystem_SourceId_Version" ON business_documents ("TenantId", "SourceSystem", "SourceId", "Version") WHERE "SourceId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_business_documents_TenantId_Status_CreatedAt" ON business_documents ("TenantId", "Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE UNIQUE INDEX "IX_document_approvals_TenantId_DocumentId_Stage_ApproverId" ON document_approvals ("TenantId", "DocumentId", "Stage", "ApproverId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE UNIQUE INDEX "IX_document_lines_TenantId_DocumentId_LineNumber" ON document_lines ("TenantId", "DocumentId", "LineNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_document_lines_TenantId_FromLocationId" ON document_lines ("TenantId", "FromLocationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_document_lines_TenantId_ItemId" ON document_lines ("TenantId", "ItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_document_lines_TenantId_ToLocationId" ON document_lines ("TenantId", "ToLocationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE UNIQUE INDEX "IX_document_links_TenantId_FromDocumentId_ToDocumentId_Relatio~" ON document_links ("TenantId", "FromDocumentId", "ToDocumentId", "RelationType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_document_links_TenantId_ToDocumentId" ON document_links ("TenantId", "ToDocumentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE UNIQUE INDEX "IX_document_postings_TenantId_DocumentId" ON document_postings ("TenantId", "DocumentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE UNIQUE INDEX "IX_document_postings_TenantId_IdempotencyKey" ON document_postings ("TenantId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE UNIQUE INDEX "IX_document_postings_TenantId_ReversesPostingId" ON document_postings ("TenantId", "ReversesPostingId") WHERE "ReversesPostingId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE UNIQUE INDEX "IX_inbox_deduplication_TenantId_Consumer_MessageId" ON inbox_deduplication ("TenantId", "Consumer", "MessageId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE UNIQUE INDEX "IX_inventory_balance_projections_TenantId_BatchId" ON inventory_balance_projections ("TenantId", "BatchId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_inventory_balance_projections_TenantId_BatchId_ItemId_Locat~" ON inventory_balance_projections ("TenantId", "BatchId", "ItemId", "LocationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_inventory_balance_projections_TenantId_BatchId_LastLedgerEn~" ON inventory_balance_projections ("TenantId", "BatchId", "LastLedgerEntryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_inventory_balance_projections_TenantId_ItemId" ON inventory_balance_projections ("TenantId", "ItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_inventory_balance_projections_TenantId_LocationId_ItemId" ON inventory_balance_projections ("TenantId", "LocationId", "ItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_inventory_ledger_entries_LocationId_ZoneId" ON inventory_ledger_entries ("LocationId", "ZoneId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_inventory_ledger_entries_TenantId_BatchId_ItemId_LocationId" ON inventory_ledger_entries ("TenantId", "BatchId", "ItemId", "LocationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_inventory_ledger_entries_TenantId_DocumentId_DocumentLineId~" ON inventory_ledger_entries ("TenantId", "DocumentId", "DocumentLineId", "ItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_inventory_ledger_entries_TenantId_DocumentId_PostingId" ON inventory_ledger_entries ("TenantId", "DocumentId", "PostingId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_inventory_ledger_entries_TenantId_ItemId" ON inventory_ledger_entries ("TenantId", "ItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_inventory_ledger_entries_TenantId_LocationId_ItemId_BatchId~" ON inventory_ledger_entries ("TenantId", "LocationId", "ItemId", "BatchId", "OccurredAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE UNIQUE INDEX "IX_inventory_ledger_entries_TenantId_PostingId_EntryNumber" ON inventory_ledger_entries ("TenantId", "PostingId", "EntryNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE UNIQUE INDEX "IX_inventory_ledger_entries_TenantId_ReversesEntryId" ON inventory_ledger_entries ("TenantId", "ReversesEntryId") WHERE "ReversesEntryId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_inventory_reconciliations_TenantId_BatchId_ItemId_LocationId" ON inventory_reconciliations ("TenantId", "BatchId", "ItemId", "LocationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_inventory_reconciliations_TenantId_ItemId" ON inventory_reconciliations ("TenantId", "ItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_inventory_reconciliations_TenantId_LocationId" ON inventory_reconciliations ("TenantId", "LocationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE UNIQUE INDEX "IX_inventory_reconciliations_TenantId_RunId_BatchId" ON inventory_reconciliations ("TenantId", "RunId", "BatchId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_inventory_reconciliations_TenantId_Status_AsOf" ON inventory_reconciliations ("TenantId", "Status", "AsOf");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_inventory_reservations_Status_ExpiresAt" ON inventory_reservations ("Status", "ExpiresAt") WHERE "Status"='active';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_inventory_reservations_TenantId_BatchId_ItemId_LocationId" ON inventory_reservations ("TenantId", "BatchId", "ItemId", "LocationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_inventory_reservations_TenantId_DocumentId_DocumentLineId_I~" ON inventory_reservations ("TenantId", "DocumentId", "DocumentLineId", "ItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE UNIQUE INDEX "IX_inventory_reservations_TenantId_IdempotencyKey" ON inventory_reservations ("TenantId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_inventory_reservations_TenantId_ItemId" ON inventory_reservations ("TenantId", "ItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_inventory_reservations_TenantId_LocationId_ItemId_Status" ON inventory_reservations ("TenantId", "LocationId", "ItemId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_outbox_events_AvailableAt_CreatedAt" ON outbox_events ("AvailableAt", "CreatedAt") WHERE "Status"='pending';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_outbox_events_LockedUntil" ON outbox_events ("LockedUntil") WHERE "Status"='processing';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE UNIQUE INDEX "IX_outbox_events_TenantId_AggregateType_AggregateId_AggregateV~" ON outbox_events ("TenantId", "AggregateType", "AggregateId", "AggregateVersion", "EventOrdinal");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE UNIQUE INDEX "IX_outbox_events_TenantId_PostingId" ON outbox_events ("TenantId", "PostingId") WHERE "PostingId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE INDEX "IX_outbox_events_TenantId_Status_CreatedAt" ON outbox_events ("TenantId", "Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE UNIQUE INDEX "UX_business_documents_active_version" ON business_documents ("TenantId", "DocumentKey") WHERE "Status"='posted';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    ALTER TABLE business_documents ENABLE ROW LEVEL SECURITY;
    ALTER TABLE business_documents FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON business_documents
      USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
      WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
    CREATE POLICY worker_bypass ON business_documents
      USING (current_setting('app.role',true)='worker')
      WITH CHECK (current_setting('app.role',true)='worker');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    ALTER TABLE document_lines ENABLE ROW LEVEL SECURITY;
    ALTER TABLE document_lines FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON document_lines
      USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
      WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
    CREATE POLICY worker_bypass ON document_lines
      USING (current_setting('app.role',true)='worker')
      WITH CHECK (current_setting('app.role',true)='worker');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    ALTER TABLE document_postings ENABLE ROW LEVEL SECURITY;
    ALTER TABLE document_postings FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON document_postings
      USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
      WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
    CREATE POLICY worker_bypass ON document_postings
      USING (current_setting('app.role',true)='worker')
      WITH CHECK (current_setting('app.role',true)='worker');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    ALTER TABLE document_links ENABLE ROW LEVEL SECURITY;
    ALTER TABLE document_links FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON document_links
      USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
      WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
    CREATE POLICY worker_bypass ON document_links
      USING (current_setting('app.role',true)='worker')
      WITH CHECK (current_setting('app.role',true)='worker');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    ALTER TABLE document_approvals ENABLE ROW LEVEL SECURITY;
    ALTER TABLE document_approvals FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON document_approvals
      USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
      WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
    CREATE POLICY worker_bypass ON document_approvals
      USING (current_setting('app.role',true)='worker')
      WITH CHECK (current_setting('app.role',true)='worker');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    ALTER TABLE inventory_ledger_entries ENABLE ROW LEVEL SECURITY;
    ALTER TABLE inventory_ledger_entries FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON inventory_ledger_entries
      USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
      WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
    CREATE POLICY worker_bypass ON inventory_ledger_entries
      USING (current_setting('app.role',true)='worker')
      WITH CHECK (current_setting('app.role',true)='worker');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    ALTER TABLE inventory_balance_projections ENABLE ROW LEVEL SECURITY;
    ALTER TABLE inventory_balance_projections FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON inventory_balance_projections
      USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
      WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
    CREATE POLICY worker_bypass ON inventory_balance_projections
      USING (current_setting('app.role',true)='worker')
      WITH CHECK (current_setting('app.role',true)='worker');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    ALTER TABLE inventory_reservations ENABLE ROW LEVEL SECURITY;
    ALTER TABLE inventory_reservations FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON inventory_reservations
      USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
      WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
    CREATE POLICY worker_bypass ON inventory_reservations
      USING (current_setting('app.role',true)='worker')
      WITH CHECK (current_setting('app.role',true)='worker');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    ALTER TABLE inventory_reconciliations ENABLE ROW LEVEL SECURITY;
    ALTER TABLE inventory_reconciliations FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON inventory_reconciliations
      USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
      WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
    CREATE POLICY worker_bypass ON inventory_reconciliations
      USING (current_setting('app.role',true)='worker')
      WITH CHECK (current_setting('app.role',true)='worker');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    ALTER TABLE outbox_events ENABLE ROW LEVEL SECURITY;
    ALTER TABLE outbox_events FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON outbox_events
      USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
      WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
    CREATE POLICY worker_bypass ON outbox_events
      USING (current_setting('app.role',true)='worker')
      WITH CHECK (current_setting('app.role',true)='worker');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    ALTER TABLE inbox_deduplication ENABLE ROW LEVEL SECURITY;
    ALTER TABLE inbox_deduplication FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON inbox_deduplication
      USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
      WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
    CREATE POLICY worker_bypass ON inbox_deduplication
      USING (current_setting('app.role',true)='worker')
      WITH CHECK (current_setting('app.role',true)='worker');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TRIGGER erp_history_guard BEFORE UPDATE OR DELETE ON document_postings FOR EACH ROW EXECUTE FUNCTION public.erp_v1_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TRIGGER erp_history_guard BEFORE UPDATE OR DELETE ON document_links FOR EACH ROW EXECUTE FUNCTION public.erp_v1_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TRIGGER erp_history_guard BEFORE UPDATE OR DELETE ON document_approvals FOR EACH ROW EXECUTE FUNCTION public.erp_v1_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TRIGGER erp_history_guard BEFORE UPDATE OR DELETE ON inventory_ledger_entries FOR EACH ROW EXECUTE FUNCTION public.erp_v1_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TRIGGER erp_history_guard BEFORE UPDATE OR DELETE ON inventory_reconciliations FOR EACH ROW EXECUTE FUNCTION public.erp_v1_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TRIGGER erp_history_guard BEFORE UPDATE OR DELETE ON inbox_deduplication FOR EACH ROW EXECUTE FUNCTION public.erp_v1_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TRIGGER erp_no_truncate BEFORE TRUNCATE ON business_documents FOR EACH STATEMENT EXECUTE FUNCTION public.erp_v1_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TRIGGER erp_no_truncate BEFORE TRUNCATE ON document_lines FOR EACH STATEMENT EXECUTE FUNCTION public.erp_v1_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TRIGGER erp_no_truncate BEFORE TRUNCATE ON document_postings FOR EACH STATEMENT EXECUTE FUNCTION public.erp_v1_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TRIGGER erp_no_truncate BEFORE TRUNCATE ON document_links FOR EACH STATEMENT EXECUTE FUNCTION public.erp_v1_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TRIGGER erp_no_truncate BEFORE TRUNCATE ON document_approvals FOR EACH STATEMENT EXECUTE FUNCTION public.erp_v1_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TRIGGER erp_no_truncate BEFORE TRUNCATE ON inventory_ledger_entries FOR EACH STATEMENT EXECUTE FUNCTION public.erp_v1_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TRIGGER erp_no_truncate BEFORE TRUNCATE ON inventory_balance_projections FOR EACH STATEMENT EXECUTE FUNCTION public.erp_v1_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TRIGGER erp_no_truncate BEFORE TRUNCATE ON inventory_reservations FOR EACH STATEMENT EXECUTE FUNCTION public.erp_v1_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TRIGGER erp_no_truncate BEFORE TRUNCATE ON inventory_reconciliations FOR EACH STATEMENT EXECUTE FUNCTION public.erp_v1_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TRIGGER erp_no_truncate BEFORE TRUNCATE ON outbox_events FOR EACH STATEMENT EXECUTE FUNCTION public.erp_v1_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    CREATE TRIGGER erp_no_truncate BEFORE TRUNCATE ON inbox_deduplication FOR EACH STATEMENT EXECUTE FUNCTION public.erp_v1_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024220000_AddErpParallelSchema') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261024220000_AddErpParallelSchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

