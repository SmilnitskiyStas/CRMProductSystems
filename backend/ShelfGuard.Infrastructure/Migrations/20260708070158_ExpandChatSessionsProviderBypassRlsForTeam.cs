using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpandChatSessionsProviderBypassRlsForTeam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // provider_admin and provider_agent must also access the Live Chat
            // inbox cross-tenant (BUG-021, follow-up to BUG-020). Widen the
            // bypass policy to the full provider team, matching the pattern
            // already used for support_tickets/ticket_comments
            // (see 20260623010000_ExpandProviderBypassRlsForTeam).
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS provider_bypass ON chat_sessions;
                CREATE POLICY provider_bypass ON chat_sessions
                    USING (current_setting('app.role', true) IN ('provider', 'provider_admin', 'provider_agent'));
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS provider_bypass ON chat_sessions;
                CREATE POLICY provider_bypass ON chat_sessions
                    USING (current_setting('app.role', true) = 'provider');
            ");
        }
    }
}
