using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_configs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Service = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Config = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_configs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_integration_configs_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_integration_configs_TenantId_Service",
                table: "integration_configs",
                columns: new[] { "TenantId", "Service" },
                unique: true);

            // ── RLS: tenant isolation ────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE integration_configs ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON integration_configs
                    USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);

                CREATE POLICY provider_bypass ON integration_configs
                    USING (current_setting('app.role', true) = 'provider');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS provider_bypass ON integration_configs;
                DROP POLICY IF EXISTS tenant_isolation ON integration_configs;
            ");

            migrationBuilder.DropTable(
                name: "integration_configs");
        }
    }
}
