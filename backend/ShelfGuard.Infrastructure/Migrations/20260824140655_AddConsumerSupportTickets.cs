using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// TASK-613 (customer/loyalty domain expansion, approved plan `goofy-bubbling-naur.md`).
    /// Consumer↔tenant support channel: <c>consumer_support_tickets</c> +
    /// <c>consumer_support_ticket_messages</c>, mirroring the existing
    /// <c>supplier_support_tickets</c>/<c>supplier_support_ticket_messages</c> pair (see
    /// SupplierCooperation migration) but for consumer↔tenant instead of tenant↔supplier.
    /// Distinct from both ServiceDesk (tenant↔provider) and supplier support (tenant↔supplier).
    ///
    /// <c>consumer_support_tickets</c> has direct TenantId/ConsumerAccountId columns, so it
    /// gets the canonical triad + a direct-column <c>consumer_self_access</c> policy.
    /// <c>consumer_support_ticket_messages</c> has neither column directly (exactly one of
    /// SenderConsumerAccountId/SenderUserId is set per message — see entity remarks), so both
    /// tenant_isolation and consumer_self_access EXISTS-join through the owning ticket.
    /// </summary>
    public partial class AddConsumerSupportTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumer_support_tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "open"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumer_support_tickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_consumer_support_tickets_consumer_accounts_ConsumerAccountId",
                        column: x => x.ConsumerAccountId,
                        principalTable: "consumer_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_consumer_support_tickets_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_consumer_support_tickets_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "consumer_support_ticket_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderConsumerAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumer_support_ticket_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_consumer_support_ticket_messages_consumer_accounts_SenderCo~",
                        column: x => x.SenderConsumerAccountId,
                        principalTable: "consumer_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_consumer_support_ticket_messages_consumer_support_tickets_T~",
                        column: x => x.TicketId,
                        principalTable: "consumer_support_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_consumer_support_ticket_messages_users_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_consumer_support_ticket_messages_CreatedAt",
                table: "consumer_support_ticket_messages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_consumer_support_ticket_messages_SenderConsumerAccountId",
                table: "consumer_support_ticket_messages",
                column: "SenderConsumerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_consumer_support_ticket_messages_SenderUserId",
                table: "consumer_support_ticket_messages",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_consumer_support_ticket_messages_TicketId",
                table: "consumer_support_ticket_messages",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_consumer_support_tickets_ConsumerAccountId",
                table: "consumer_support_tickets",
                column: "ConsumerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_consumer_support_tickets_CustomerId",
                table: "consumer_support_tickets",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_consumer_support_tickets_TenantId",
                table: "consumer_support_tickets",
                column: "TenantId");

            // ── RLS: consumer_support_tickets ────────────────────────────────
            // Canonical triad + direct-column consumer_self_access.
            migrationBuilder.Sql(@"
                ALTER TABLE consumer_support_tickets ENABLE ROW LEVEL SECURITY;
                ALTER TABLE consumer_support_tickets FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON consumer_support_tickets
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON consumer_support_tickets
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON consumer_support_tickets
                  USING (current_setting('app.role', true) = 'worker');
                CREATE POLICY consumer_self_access ON consumer_support_tickets
                  USING (""ConsumerAccountId"" = (NULLIF(current_setting('app.consumer_account_id', true), ''))::uuid);
            ");

            // ── RLS: consumer_support_ticket_messages ────────────────────────
            // No direct TenantId/ConsumerAccountId column — both policies EXISTS-join
            // through the owning ticket (mirrors supplier_support_ticket_messages' own
            // tenant_isolation shape from SupplierCooperation).
            migrationBuilder.Sql(@"
                ALTER TABLE consumer_support_ticket_messages ENABLE ROW LEVEL SECURITY;
                ALTER TABLE consumer_support_ticket_messages FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON consumer_support_ticket_messages
                  USING (
                    EXISTS (
                      SELECT 1 FROM consumer_support_tickets t
                      WHERE t.""Id"" = consumer_support_ticket_messages.""TicketId""
                        AND t.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
                    )
                  );
                CREATE POLICY provider_bypass ON consumer_support_ticket_messages
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON consumer_support_ticket_messages
                  USING (current_setting('app.role', true) = 'worker');
                CREATE POLICY consumer_self_access ON consumer_support_ticket_messages
                  USING (
                    EXISTS (
                      SELECT 1 FROM consumer_support_tickets t
                      WHERE t.""Id"" = consumer_support_ticket_messages.""TicketId""
                        AND t.""ConsumerAccountId"" = (NULLIF(current_setting('app.consumer_account_id', true), ''))::uuid
                    )
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS consumer_self_access ON consumer_support_ticket_messages;
                DROP POLICY IF EXISTS worker_bypass ON consumer_support_ticket_messages;
                DROP POLICY IF EXISTS provider_bypass ON consumer_support_ticket_messages;
                DROP POLICY IF EXISTS tenant_isolation ON consumer_support_ticket_messages;
                ALTER TABLE consumer_support_ticket_messages DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS consumer_self_access ON consumer_support_tickets;
                DROP POLICY IF EXISTS worker_bypass ON consumer_support_tickets;
                DROP POLICY IF EXISTS provider_bypass ON consumer_support_tickets;
                DROP POLICY IF EXISTS tenant_isolation ON consumer_support_tickets;
                ALTER TABLE consumer_support_tickets DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "consumer_support_ticket_messages");

            migrationBuilder.DropTable(
                name: "consumer_support_tickets");
        }
    }
}
