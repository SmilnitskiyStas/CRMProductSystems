using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMobileConfigurationDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mobile_configuration_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    MobileConfigurationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "draft"),
                    ConfigurationJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_configuration_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mobile_configuration_versions_users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "mobile_configurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DraftVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_configurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mobile_configurations_mobile_configuration_versions_DraftVe~",
                        column: x => x.DraftVersionId,
                        principalTable: "mobile_configuration_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mobile_configurations_mobile_configuration_versions_Publish~",
                        column: x => x.PublishedVersionId,
                        principalTable: "mobile_configuration_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mobile_configurations_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mobile_themes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    MobileConfigurationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogoUrl = table.Column<string>(type: "text", nullable: true),
                    PrimaryColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SecondaryColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BackgroundColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SurfaceColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TextPrimaryColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TextSecondaryColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ButtonRadius = table.Column<int>(type: "integer", nullable: false),
                    CardRadius = table.Column<int>(type: "integer", nullable: false),
                    SpacingPreset = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_themes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mobile_themes_mobile_configurations_MobileConfigurationId",
                        column: x => x.MobileConfigurationId,
                        principalTable: "mobile_configurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_mobile_configuration_versions_tenant_status",
                table: "mobile_configuration_versions",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_mobile_configuration_versions_CreatedBy",
                table: "mobile_configuration_versions",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "uq_mobile_configuration_versions_config_version",
                table: "mobile_configuration_versions",
                columns: new[] { "MobileConfigurationId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mobile_configurations_DraftVersionId",
                table: "mobile_configurations",
                column: "DraftVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_mobile_configurations_PublishedVersionId",
                table: "mobile_configurations",
                column: "PublishedVersionId");

            migrationBuilder.CreateIndex(
                name: "uq_mobile_configurations_tenant",
                table: "mobile_configurations",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_mobile_themes_config",
                table: "mobile_themes",
                column: "MobileConfigurationId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_mobile_configuration_versions_mobile_configurations_MobileC~",
                table: "mobile_configuration_versions",
                column: "MobileConfigurationId",
                principalTable: "mobile_configurations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // ── RLS: mobile_configurations ─────────────────────────────────
            // Canonical triad. provider_bypass covers both 'provider'/'provider_admin' —
            // current convention since 20260714150000_ExpandProviderBypassToProviderAdmin
            // (see database-schema.md, not the stale single-value template at the top of
            // that doc). worker_bypass included from day one per the TASK-343 lesson —
            // no BullMQ job writes here today, but the next one that does must not
            // silently no-op the way pre-TASK-343 tables did.
            migrationBuilder.Sql(@"
                ALTER TABLE mobile_configurations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE mobile_configurations FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON mobile_configurations
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON mobile_configurations
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON mobile_configurations
                  USING (current_setting('app.role', true) = 'worker');
            ");

            // ── RLS: mobile_configuration_versions ─────────────────────────
            // TenantId is a direct denormalized column here (not derived via join), so this
            // uses the same direct-column shape as mobile_configurations, not an EXISTS
            // subquery through the parent.
            migrationBuilder.Sql(@"
                ALTER TABLE mobile_configuration_versions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE mobile_configuration_versions FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON mobile_configuration_versions
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON mobile_configuration_versions
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON mobile_configuration_versions
                  USING (current_setting('app.role', true) = 'worker');
            ");

            // ── RLS: mobile_themes ──────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE mobile_themes ENABLE ROW LEVEL SECURITY;
                ALTER TABLE mobile_themes FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON mobile_themes
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON mobile_themes
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON mobile_themes
                  USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_mobile_configuration_versions_mobile_configurations_MobileC~",
                table: "mobile_configuration_versions");

            migrationBuilder.DropTable(
                name: "mobile_themes");

            migrationBuilder.DropTable(
                name: "mobile_configurations");

            migrationBuilder.DropTable(
                name: "mobile_configuration_versions");
        }
    }
}
