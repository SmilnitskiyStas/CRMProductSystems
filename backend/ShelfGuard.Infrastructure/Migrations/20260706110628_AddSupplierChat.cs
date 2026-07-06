using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_chat_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SupplierTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_chat_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_chat_sessions_tenants_ClientTenantId",
                        column: x => x.ClientTenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_chat_sessions_tenants_SupplierTenantId",
                        column: x => x.SupplierTenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supplier_chat_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_chat_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_chat_messages_supplier_chat_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "supplier_chat_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_chat_messages_CreatedAt",
                table: "supplier_chat_messages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_chat_messages_SessionId",
                table: "supplier_chat_messages",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_chat_sessions_ClientTenantId",
                table: "supplier_chat_sessions",
                column: "ClientTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_chat_sessions_SupplierTenantId",
                table: "supplier_chat_sessions",
                column: "SupplierTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_chat_sessions_SupplierTenantId_ClientTenantId",
                table: "supplier_chat_sessions",
                columns: new[] { "SupplierTenantId", "ClientTenantId" },
                unique: true);

            // ── RLS: supplier_chat_sessions ─────────────────────────────────────
            // NULLIF guard: current_setting(..., true) returns '' after RESET, which
            // breaks the ::uuid cast without this guard (see known-issues.md).
            // Either side of the conversation (supplier or client tenant) can see
            // the session — hence the OR across both tenant columns.
            migrationBuilder.Sql(@"
                ALTER TABLE supplier_chat_sessions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE supplier_chat_sessions FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON supplier_chat_sessions
                  USING (""SupplierTenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                         OR ""ClientTenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                CREATE POLICY provider_bypass ON supplier_chat_sessions
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: supplier_chat_messages ─────────────────────────────────────
            // No direct tenant column — isolate via the owning session's tenant pair.
            migrationBuilder.Sql(@"
                ALTER TABLE supplier_chat_messages ENABLE ROW LEVEL SECURITY;
                ALTER TABLE supplier_chat_messages FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON supplier_chat_messages
                  USING (EXISTS (
                    SELECT 1 FROM supplier_chat_sessions s
                    WHERE s.""Id"" = ""SessionId""
                      AND (s.""SupplierTenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                           OR s.""ClientTenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                  ));
                CREATE POLICY provider_bypass ON supplier_chat_messages
                  USING (current_setting('app.role', true) = 'provider');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS provider_bypass ON supplier_chat_messages;
                DROP POLICY IF EXISTS tenant_isolation ON supplier_chat_messages;
                ALTER TABLE supplier_chat_messages DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS provider_bypass ON supplier_chat_sessions;
                DROP POLICY IF EXISTS tenant_isolation ON supplier_chat_sessions;
                ALTER TABLE supplier_chat_sessions DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "supplier_chat_messages");

            migrationBuilder.DropTable(
                name: "supplier_chat_sessions");
        }
    }
}
