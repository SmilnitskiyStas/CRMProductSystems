using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpandProviderBypassRlsForTeam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // provider_admin and provider_agent must also access support tickets cross-tenant.
            // Drop the provider-only bypass policies and recreate them for the full provider team.

            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS provider_bypass ON support_tickets;
                CREATE POLICY provider_bypass ON support_tickets
                    USING (current_setting('app.role', true) IN ('provider', 'provider_admin', 'provider_agent'));
            ");

            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS provider_bypass ON ticket_comments;
                CREATE POLICY provider_bypass ON ticket_comments
                    USING (current_setting('app.role', true) IN ('provider', 'provider_admin', 'provider_agent'));
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS provider_bypass ON support_tickets;
                CREATE POLICY provider_bypass ON support_tickets
                    USING (current_setting('app.role', true) = 'provider');
            ");

            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS provider_bypass ON ticket_comments;
                CREATE POLICY provider_bypass ON ticket_comments
                    USING (current_setting('app.role', true) = 'provider');
            ");
        }
    }
}
