using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    // TASK-363 (Block 12 pre-launch audit): chat_messages and support_messages were the only
    // two tables in the entire Chat/ServiceDesk/Support family with RLS completely disabled
    // (relrowsecurity=false — no policy at all, confirmed live against the dev DB). Their
    // parent tables (chat_sessions, support_tickets) and the analogous marketplace tables
    // (supplier_chat_messages, supplier_support_ticket_messages — added in AddSupplierChat /
    // 20260706110628) all correctly carry RLS. Application-code review (ChatService.cs,
    // SupportService.cs) found every current query path already scopes correctly via the
    // parent's TenantId before ever touching these child tables, so this is a defense-in-depth
    // gap, not a live exploit — but it is the exact safety net this audit series has repeatedly
    // needed elsewhere (Block 2's fail-open RLS bug, several worker jobs missing filters).
    // Neither table has its own TenantId column (messages only carry SessionId/TicketId), so
    // this mirrors supplier_chat_messages' EXISTS-subquery-via-parent pattern rather than a
    // plain column comparison.
    public partial class AddChatAndSupportMessagesRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── RLS: chat_messages ──────────────────────────────────────────────
            // provider_bypass role set mirrors chat_sessions' own provider_bypass exactly
            // (provider + provider_admin + provider_agent — the Chat feature is answered by
            // provider_agent too, per ProviderPermissions.SystemRoleDefaults).
            migrationBuilder.Sql(@"
                ALTER TABLE chat_messages ENABLE ROW LEVEL SECURITY;
                ALTER TABLE chat_messages FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON chat_messages
                  USING (EXISTS (
                    SELECT 1 FROM chat_sessions s
                    WHERE s.""Id"" = ""SessionId""
                      AND s.""TenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                  ));
                CREATE POLICY provider_bypass ON chat_messages
                  USING (current_setting('app.role', true) = ANY (ARRAY['provider', 'provider_admin', 'provider_agent']));
                CREATE POLICY worker_bypass ON chat_messages
                  USING (current_setting('app.role', true) = 'worker');
            ");

            // ── RLS: support_messages ────────────────────────────────────────────
            // provider_bypass role set mirrors support_tickets' own provider_bypass exactly.
            migrationBuilder.Sql(@"
                ALTER TABLE support_messages ENABLE ROW LEVEL SECURITY;
                ALTER TABLE support_messages FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON support_messages
                  USING (EXISTS (
                    SELECT 1 FROM support_tickets t
                    WHERE t.""Id"" = ""TicketId""
                      AND t.""TenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                  ));
                CREATE POLICY provider_bypass ON support_messages
                  USING (current_setting('app.role', true) = ANY (ARRAY['provider', 'provider_admin', 'provider_agent']));
                CREATE POLICY worker_bypass ON support_messages
                  USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS worker_bypass ON support_messages;
                DROP POLICY IF EXISTS provider_bypass ON support_messages;
                DROP POLICY IF EXISTS tenant_isolation ON support_messages;
                ALTER TABLE support_messages DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS worker_bypass ON chat_messages;
                DROP POLICY IF EXISTS provider_bypass ON chat_messages;
                DROP POLICY IF EXISTS tenant_isolation ON chat_messages;
                ALTER TABLE chat_messages DISABLE ROW LEVEL SECURITY;
            ");
        }
    }
}
