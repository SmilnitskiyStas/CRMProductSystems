using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixUsersRlsForAuthFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fix RLS on users to allow login lookups when no tenant context is set.
            // TenantConnectionInterceptor now RESETs app.tenant_id for unauthenticated
            // requests (login endpoint), so current_setting returns NULL.
            // Without this change, tenant users (TenantId != NULL) are invisible during
            // login because the interceptor's null-UUID from a pooled connection blocks them.
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS tenant_isolation ON users;
                CREATE POLICY tenant_isolation ON users
                  USING (current_setting('app.tenant_id', true) IS NULL
                         OR ""TenantId"" = current_setting('app.tenant_id', true)::uuid
                         OR ""TenantId"" IS NULL);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore original policy (without the IS NULL auth bypass).
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS tenant_isolation ON users;
                CREATE POLICY tenant_isolation ON users
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid
                         OR ""TenantId"" IS NULL);
            ");
        }
    }
}
