START TRANSACTION;

DROP POLICY IF EXISTS worker_bypass ON beauty_tenant_links;
DROP POLICY IF EXISTS provider_bypass ON beauty_tenant_links;
DROP POLICY IF EXISTS tenant_isolation ON beauty_tenant_links;
ALTER TABLE beauty_tenant_links NO FORCE ROW LEVEL SECURITY;
ALTER TABLE beauty_tenant_links DISABLE ROW LEVEL SECURITY;

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20261105090000_AddBeautyTenantLinksRls';

COMMIT;

START TRANSACTION;

DROP TABLE beauty_tenant_links;

DROP INDEX "IX_landing_leads_ExternalId";

ALTER TABLE landing_leads DROP COLUMN "BeautyTenantRef";

ALTER TABLE landing_leads DROP COLUMN "BeautyTenantSlug";

ALTER TABLE landing_leads DROP COLUMN "Contact";

ALTER TABLE landing_leads DROP COLUMN "ExternalId";

ALTER TABLE landing_leads DROP COLUMN "ReporterRole";

ALTER TABLE landing_leads DROP COLUMN "RequestType";

ALTER TABLE landing_leads ALTER COLUMN "Message" TYPE character varying(1000);

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20261104223156_AddBeautyControlPlane';

COMMIT;

START TRANSACTION;

ALTER TABLE landing_leads DROP COLUMN "CriticalReason";

ALTER TABLE landing_leads DROP COLUMN "IsCritical";

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20261104155307_AddLandingLeadCriticality';

COMMIT;

