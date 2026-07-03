using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShelfGuard.Infrastructure.Data;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260623000000_AddServiceDeskProviderBypassRls")]
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

            // Idempotent: DROP IF EXISTS first — this migration may re-run on a DB
            // where the policy was already applied out-of-band (missing [Migration] attribute).
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS provider_bypass ON support_tickets;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS provider_bypass ON ticket_comments;");
        }
    }
}
