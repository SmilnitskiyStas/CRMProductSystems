START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104155307_AddLandingLeadCriticality') THEN
    ALTER TABLE landing_leads ADD "CriticalReason" character varying(50);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104155307_AddLandingLeadCriticality') THEN
    ALTER TABLE landing_leads ADD "IsCritical" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104155307_AddLandingLeadCriticality') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261104155307_AddLandingLeadCriticality', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104223156_AddBeautyControlPlane') THEN
    ALTER TABLE landing_leads ALTER COLUMN "Message" TYPE character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104223156_AddBeautyControlPlane') THEN
    ALTER TABLE landing_leads ADD "BeautyTenantRef" character varying(64);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104223156_AddBeautyControlPlane') THEN
    ALTER TABLE landing_leads ADD "BeautyTenantSlug" character varying(63);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104223156_AddBeautyControlPlane') THEN
    ALTER TABLE landing_leads ADD "Contact" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104223156_AddBeautyControlPlane') THEN
    ALTER TABLE landing_leads ADD "ExternalId" character varying(64);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104223156_AddBeautyControlPlane') THEN
    ALTER TABLE landing_leads ADD "ReporterRole" character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104223156_AddBeautyControlPlane') THEN
    ALTER TABLE landing_leads ADD "RequestType" character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104223156_AddBeautyControlPlane') THEN
    CREATE TABLE beauty_tenant_links (
        "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "TenantId" uuid NOT NULL,
        "BeautyTenantId" uuid,
        "BeautySlug" character varying(63) NOT NULL,
        "BeautyStatus" character varying(20) NOT NULL DEFAULT 'active',
        "LastSyncedAt" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
        CONSTRAINT "PK_beauty_tenant_links" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104223156_AddBeautyControlPlane') THEN
    CREATE UNIQUE INDEX "IX_landing_leads_ExternalId" ON landing_leads ("ExternalId") WHERE "ExternalId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104223156_AddBeautyControlPlane') THEN
    CREATE UNIQUE INDEX "IX_beauty_tenant_links_BeautySlug" ON beauty_tenant_links ("BeautySlug");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104223156_AddBeautyControlPlane') THEN
    CREATE UNIQUE INDEX "IX_beauty_tenant_links_TenantId" ON beauty_tenant_links ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104223156_AddBeautyControlPlane') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261104223156_AddBeautyControlPlane', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261105090000_AddBeautyTenantLinksRls') THEN
    ALTER TABLE beauty_tenant_links ENABLE ROW LEVEL SECURITY;
    ALTER TABLE beauty_tenant_links FORCE ROW LEVEL SECURITY;

    DROP POLICY IF EXISTS tenant_isolation ON beauty_tenant_links;
    CREATE POLICY tenant_isolation ON beauty_tenant_links
      USING ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
      WITH CHECK ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

    DROP POLICY IF EXISTS provider_bypass ON beauty_tenant_links;
    CREATE POLICY provider_bypass ON beauty_tenant_links
      USING (current_setting('app.role', true) = 'provider');

    DROP POLICY IF EXISTS worker_bypass ON beauty_tenant_links;
    CREATE POLICY worker_bypass ON beauty_tenant_links
      USING (current_setting('app.role', true) = 'worker');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261105090000_AddBeautyTenantLinksRls') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261105090000_AddBeautyTenantLinksRls', '8.0.11');
    END IF;
END $EF$;
COMMIT;

