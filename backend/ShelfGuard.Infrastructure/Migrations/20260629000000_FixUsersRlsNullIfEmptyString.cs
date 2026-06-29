using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixUsersRlsNullIfEmptyString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // After switching the app DB user from superuser to shelfguard_app (non-superuser),
            // RLS policies are now actually evaluated. The login endpoint resets app.tenant_id
            // via RESET, which returns '' (empty string) — not NULL — in PostgreSQL.
            // The previous policy cast '' directly to uuid, causing:
            //   "invalid input syntax for type uuid: """
            // Fix: use NULLIF to treat '' the same as NULL before the cast.
            migrationBuilder.Sql(@"
                ALTER POLICY tenant_isolation ON users USING (
                  NULLIF(current_setting('app.tenant_id', true), '') IS NULL
                  OR ""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
                  OR ""TenantId"" IS NULL
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER POLICY tenant_isolation ON users USING (
                  current_setting('app.tenant_id', true) IS NULL
                  OR ""TenantId"" = current_setting('app.tenant_id', true)::uuid
                  OR ""TenantId"" IS NULL
                );
            ");
        }
    }
}
