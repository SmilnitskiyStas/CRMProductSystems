using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceDeskProviderBypassRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // support_tickets and ticket_comments were created with only a tenant-scoped
            // RLS policy. The provider role (no tenant_id in JWT → app.tenant_id = null UUID)
            // was blocked from reading any rows — causing the provider support panel to
            // always return an empty list. Add provider_bypass policy matching the pattern
            // used by other cross-tenant tables (notification_settings, activity_logs, etc.).

            migrationBuilder.Sql(@"
                CREATE POLICY provider_bypass ON support_tickets
                    USING (current_setting('app.role', true) = 'provider');
            ");

            migrationBuilder.Sql(@"
                CREATE POLICY provider_bypass ON ticket_comments
                    USING (current_setting('app.role', true) = 'provider');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS provider_bypass ON support_tickets;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS provider_bypass ON ticket_comments;");
        }
    }
}
