using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatSessionsProviderBypassRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // chat_sessions was created with only a tenant-scoped RLS policy. The
            // provider role (no tenant_id in JWT → app.tenant_id = null UUID) was
            // blocked from reading any rows — causing the provider support chat
            // inbox to always return an empty list. Add provider_bypass policy
            // matching the pattern used for support_tickets/ticket_comments
            // (see 20260623000000_AddServiceDeskProviderBypassRls).
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS provider_bypass ON chat_sessions;
                CREATE POLICY provider_bypass ON chat_sessions
                    USING (current_setting('app.role', true) = 'provider');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS provider_bypass ON chat_sessions;");
        }
    }
}
