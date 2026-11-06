using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShelfGuard.Infrastructure.Data;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// beauty_tenant_links (20261104223156_AddBeautyControlPlane) carries a TenantId but shipped without
    /// RLS. Adds the standard triad (tenant_isolation with NULLIF fail-closed guard, provider_bypass,
    /// worker_bypass) + ENABLE/FORCE. SQL-only, additive: no model change, so no Designer/Snapshot update.
    /// Legit access: ProviderOnly endpoints (app.role = 'provider' -> provider_bypass); a tenant session
    /// sees only its own link. The unauthenticated HMAC inbound feedback path (app.tenant_id reset) now
    /// gets zero rows from the link lookup (slug enrichment degrades to null; the lead is still saved).
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261105090000_AddBeautyTenantLinksRls")]
    public partial class AddBeautyTenantLinksRls : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
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
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS worker_bypass ON beauty_tenant_links;
                DROP POLICY IF EXISTS provider_bypass ON beauty_tenant_links;
                DROP POLICY IF EXISTS tenant_isolation ON beauty_tenant_links;
                ALTER TABLE beauty_tenant_links NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE beauty_tenant_links DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
