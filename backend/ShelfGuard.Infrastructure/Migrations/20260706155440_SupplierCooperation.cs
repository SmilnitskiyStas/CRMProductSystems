using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SupplierCooperation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_agreements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SupplierTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "pending"),
                    RequestMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ContractNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ContractFilePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    VchasnoDocumentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TerminatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_agreements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_agreements_tenants_ClientTenantId",
                        column: x => x.ClientTenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_agreements_tenants_SupplierTenantId",
                        column: x => x.SupplierTenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supplier_agreements_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "supplier_contract_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Edrpou = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Iban = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    BankName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    LegalAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DirectorName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ServiceName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ServiceDescription = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SignatureImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    StampImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsVatPayer = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_contract_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_contract_settings_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supplier_support_tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SupplierTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "open"),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_support_tickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_support_tickets_tenants_ClientTenantId",
                        column: x => x.ClientTenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_support_tickets_tenants_SupplierTenantId",
                        column: x => x.SupplierTenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supplier_support_tickets_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "marketplace_orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrderNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "new"),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "numeric(14,2)", nullable: false, defaultValue: 0m),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketplace_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_marketplace_orders_supplier_agreements_AgreementId",
                        column: x => x.AgreementId,
                        principalTable: "supplier_agreements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_marketplace_orders_tenants_ClientTenantId",
                        column: x => x.ClientTenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_marketplace_orders_tenants_SupplierTenantId",
                        column: x => x.SupplierTenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_marketplace_orders_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "supplier_support_ticket_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_support_ticket_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_support_ticket_messages_supplier_support_tickets_T~",
                        column: x => x.TicketId,
                        principalTable: "supplier_support_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "marketplace_order_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    ItemName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Qty = table.Column<decimal>(type: "numeric(12,3)", nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(14,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketplace_order_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_marketplace_order_items_marketplace_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "marketplace_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_marketplace_order_items_supplier_items_SupplierItemId",
                        column: x => x.SupplierItemId,
                        principalTable: "supplier_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_order_items_OrderId",
                table: "marketplace_order_items",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_order_items_SupplierItemId",
                table: "marketplace_order_items",
                column: "SupplierItemId");

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_orders_AgreementId",
                table: "marketplace_orders",
                column: "AgreementId");

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_orders_ClientTenantId",
                table: "marketplace_orders",
                column: "ClientTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_orders_CreatedByUserId",
                table: "marketplace_orders",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_orders_SupplierTenantId",
                table: "marketplace_orders",
                column: "SupplierTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_agreements_ClientTenantId",
                table: "supplier_agreements",
                column: "ClientTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_agreements_CreatedByUserId",
                table: "supplier_agreements",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_agreements_SupplierTenantId",
                table: "supplier_agreements",
                column: "SupplierTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_agreements_SupplierTenantId_ClientTenantId",
                table: "supplier_agreements",
                columns: new[] { "SupplierTenantId", "ClientTenantId" },
                unique: true,
                filter: "\"Status\" NOT IN ('rejected', 'terminated')");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_contract_settings_TenantId",
                table: "supplier_contract_settings",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_support_ticket_messages_CreatedAt",
                table: "supplier_support_ticket_messages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_support_ticket_messages_TicketId",
                table: "supplier_support_ticket_messages",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_support_tickets_ClientTenantId",
                table: "supplier_support_tickets",
                column: "ClientTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_support_tickets_CreatedByUserId",
                table: "supplier_support_tickets",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_support_tickets_SupplierTenantId",
                table: "supplier_support_tickets",
                column: "SupplierTenantId");

            // ── RLS: supplier_contract_settings ─────────────────────────────────
            // Single-tenant pattern (like supplier_roles): only the owning supplier
            // tenant sees its own requisites. NULLIF guard: current_setting(..., true)
            // returns '' after RESET, which breaks the ::uuid cast without it.
            migrationBuilder.Sql(@"
                ALTER TABLE supplier_contract_settings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE supplier_contract_settings FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON supplier_contract_settings
                  USING (""TenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                CREATE POLICY provider_bypass ON supplier_contract_settings
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: supplier_agreements ────────────────────────────────────────
            // Two-tenant pattern (like supplier_chat_sessions): either side of the
            // agreement (supplier or client tenant) can see the row.
            migrationBuilder.Sql(@"
                ALTER TABLE supplier_agreements ENABLE ROW LEVEL SECURITY;
                ALTER TABLE supplier_agreements FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON supplier_agreements
                  USING (""SupplierTenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                         OR ""ClientTenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                CREATE POLICY provider_bypass ON supplier_agreements
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: marketplace_orders ─────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE marketplace_orders ENABLE ROW LEVEL SECURITY;
                ALTER TABLE marketplace_orders FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON marketplace_orders
                  USING (""SupplierTenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                         OR ""ClientTenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                CREATE POLICY provider_bypass ON marketplace_orders
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: marketplace_order_items ────────────────────────────────────
            // Items carry denormalised copies of the parent order's tenant pair, so
            // the policy filters directly on the row without a join (simpler and
            // cheaper than the EXISTS-through-parent pattern of chat messages).
            migrationBuilder.Sql(@"
                ALTER TABLE marketplace_order_items ENABLE ROW LEVEL SECURITY;
                ALTER TABLE marketplace_order_items FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON marketplace_order_items
                  USING (""SupplierTenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                         OR ""ClientTenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                CREATE POLICY provider_bypass ON marketplace_order_items
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: supplier_support_tickets ───────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE supplier_support_tickets ENABLE ROW LEVEL SECURITY;
                ALTER TABLE supplier_support_tickets FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON supplier_support_tickets
                  USING (""SupplierTenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                         OR ""ClientTenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                CREATE POLICY provider_bypass ON supplier_support_tickets
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: supplier_support_ticket_messages ───────────────────────────
            // No direct tenant column — isolate via the owning ticket's tenant pair
            // (mirrors supplier_chat_messages).
            migrationBuilder.Sql(@"
                ALTER TABLE supplier_support_ticket_messages ENABLE ROW LEVEL SECURITY;
                ALTER TABLE supplier_support_ticket_messages FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON supplier_support_ticket_messages
                  USING (EXISTS (
                    SELECT 1 FROM supplier_support_tickets t
                    WHERE t.""Id"" = ""TicketId""
                      AND (t.""SupplierTenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                           OR t.""ClientTenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                  ));
                CREATE POLICY provider_bypass ON supplier_support_ticket_messages
                  USING (current_setting('app.role', true) = 'provider');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS provider_bypass ON supplier_support_ticket_messages;
                DROP POLICY IF EXISTS tenant_isolation ON supplier_support_ticket_messages;
                ALTER TABLE supplier_support_ticket_messages DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS provider_bypass ON supplier_support_tickets;
                DROP POLICY IF EXISTS tenant_isolation ON supplier_support_tickets;
                ALTER TABLE supplier_support_tickets DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS provider_bypass ON marketplace_order_items;
                DROP POLICY IF EXISTS tenant_isolation ON marketplace_order_items;
                ALTER TABLE marketplace_order_items DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS provider_bypass ON marketplace_orders;
                DROP POLICY IF EXISTS tenant_isolation ON marketplace_orders;
                ALTER TABLE marketplace_orders DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS provider_bypass ON supplier_agreements;
                DROP POLICY IF EXISTS tenant_isolation ON supplier_agreements;
                ALTER TABLE supplier_agreements DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS provider_bypass ON supplier_contract_settings;
                DROP POLICY IF EXISTS tenant_isolation ON supplier_contract_settings;
                ALTER TABLE supplier_contract_settings DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "marketplace_order_items");

            migrationBuilder.DropTable(
                name: "supplier_contract_settings");

            migrationBuilder.DropTable(
                name: "supplier_support_ticket_messages");

            migrationBuilder.DropTable(
                name: "marketplace_orders");

            migrationBuilder.DropTable(
                name: "supplier_support_tickets");

            migrationBuilder.DropTable(
                name: "supplier_agreements");
        }
    }
}
