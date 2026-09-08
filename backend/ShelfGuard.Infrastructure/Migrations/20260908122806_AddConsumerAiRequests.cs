using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConsumerAiRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumer_ai_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromptExcerpt = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ResponseExcerpt = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TokensUsed = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumer_ai_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_consumer_ai_requests_consumer_accounts_ConsumerAccountId",
                        column: x => x.ConsumerAccountId,
                        principalTable: "consumer_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_consumer_ai_requests_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_consumer_ai_requests_ConsumerAccountId",
                table: "consumer_ai_requests",
                column: "ConsumerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_consumer_ai_requests_TenantId_CreatedAt",
                table: "consumer_ai_requests",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            // ── RLS: consumer_ai_requests (managed-AI Phase 4b) ──────────────────
            // Canonical triad + direct-column consumer_self_access. The INSERT runs inside an
            // ITenantSessionOverride block (app.tenant_id set), so tenant_isolation's WITH CHECK
            // passes; provider/worker read it for abuse review and retention pruning.
            migrationBuilder.Sql(@"
                ALTER TABLE consumer_ai_requests ENABLE ROW LEVEL SECURITY;
                ALTER TABLE consumer_ai_requests FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON consumer_ai_requests
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON consumer_ai_requests
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON consumer_ai_requests
                  USING (current_setting('app.role', true) = 'worker');
                CREATE POLICY consumer_self_access ON consumer_ai_requests
                  USING (""ConsumerAccountId"" = (NULLIF(current_setting('app.consumer_account_id', true), ''))::uuid);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS consumer_self_access ON consumer_ai_requests;
                DROP POLICY IF EXISTS worker_bypass ON consumer_ai_requests;
                DROP POLICY IF EXISTS provider_bypass ON consumer_ai_requests;
                DROP POLICY IF EXISTS tenant_isolation ON consumer_ai_requests;
            ");

            migrationBuilder.DropTable(
                name: "consumer_ai_requests");
        }
    }
}
