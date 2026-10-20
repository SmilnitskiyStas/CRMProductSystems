using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotionRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PromotionLabel",
                table: "pos_transaction_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PromotionRuleId",
                table: "pos_transaction_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "promotion_rule_applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisterId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    Units = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_rule_applications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_promotion_rule_applications_pos_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "pos_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_promotion_rule_applications_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "promotion_rule_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    DefinitionJson = table.Column<string>(type: "jsonb", nullable: false),
                    Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChangeComment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_rule_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_promotion_rule_versions_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "promotion_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "draft"),
                    CurrentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_promotion_rules_promotion_rule_versions_CurrentVersionId",
                        column: x => x.CurrentVersionId,
                        principalTable: "promotion_rule_versions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_promotion_rules_promotion_rule_versions_PublishedVersionId",
                        column: x => x.PublishedVersionId,
                        principalTable: "promotion_rule_versions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_promotion_rules_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pos_transaction_items_PromotionRuleId",
                table: "pos_transaction_items",
                column: "PromotionRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_rule_applications_RuleId",
                table: "promotion_rule_applications",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_rule_applications_TenantId_RuleId_AppliedAt",
                table: "promotion_rule_applications",
                columns: new[] { "TenantId", "RuleId", "AppliedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_promotion_rule_applications_TransactionId",
                table: "promotion_rule_applications",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_rule_applications_VersionId",
                table: "promotion_rule_applications",
                column: "VersionId");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_rule_versions_RuleId_VersionNumber",
                table: "promotion_rule_versions",
                columns: new[] { "RuleId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_promotion_rule_versions_TenantId",
                table: "promotion_rule_versions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_rules_CurrentVersionId",
                table: "promotion_rules",
                column: "CurrentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_rules_PublishedVersionId",
                table: "promotion_rules",
                column: "PublishedVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_rules_TenantId_Status",
                table: "promotion_rules",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_pos_transaction_items_promotion_rules_PromotionRuleId",
                table: "pos_transaction_items",
                column: "PromotionRuleId",
                principalTable: "promotion_rules",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_promotion_rule_applications_promotion_rule_versions_Version~",
                table: "promotion_rule_applications",
                column: "VersionId",
                principalTable: "promotion_rule_versions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_promotion_rule_applications_promotion_rules_RuleId",
                table: "promotion_rule_applications",
                column: "RuleId",
                principalTable: "promotion_rules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_promotion_rule_versions_promotion_rules_RuleId",
                table: "promotion_rule_versions",
                column: "RuleId",
                principalTable: "promotion_rules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // ── Versions are immutable: editing a rule always inserts a new version ──────────
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION promotion_rule_versions_immutable() RETURNS trigger AS $$
                BEGIN
                  RAISE EXCEPTION 'promotion_rule_versions rows are immutable (insert a new version)';
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_promotion_rule_versions_immutable BEFORE UPDATE ON promotion_rule_versions
                  FOR EACH ROW EXECUTE FUNCTION promotion_rule_versions_immutable();
            ");

            // ── RLS: canonical triad on all three tables (store-scope RLS not needed) ────────
            foreach (var table in new[] { "promotion_rules", "promotion_rule_versions", "promotion_rule_applications" })
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON {table}
                      USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                    CREATE POLICY provider_bypass ON {table}
                      USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                    CREATE POLICY worker_bypass ON {table}
                      USING (current_setting('app.role', true) = 'worker');
                ");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "promotion_rules", "promotion_rule_versions", "promotion_rule_applications" })
            {
                migrationBuilder.Sql($@"
                    DROP POLICY IF EXISTS worker_bypass ON {table};
                    DROP POLICY IF EXISTS provider_bypass ON {table};
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                ");
            }
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_promotion_rule_versions_immutable ON promotion_rule_versions;
                DROP FUNCTION IF EXISTS promotion_rule_versions_immutable();
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_pos_transaction_items_promotion_rules_PromotionRuleId",
                table: "pos_transaction_items");

            migrationBuilder.DropForeignKey(
                name: "FK_promotion_rules_promotion_rule_versions_CurrentVersionId",
                table: "promotion_rules");

            migrationBuilder.DropForeignKey(
                name: "FK_promotion_rules_promotion_rule_versions_PublishedVersionId",
                table: "promotion_rules");

            migrationBuilder.DropTable(
                name: "promotion_rule_applications");

            migrationBuilder.DropTable(
                name: "promotion_rule_versions");

            migrationBuilder.DropTable(
                name: "promotion_rules");

            migrationBuilder.DropIndex(
                name: "IX_pos_transaction_items_PromotionRuleId",
                table: "pos_transaction_items");

            migrationBuilder.DropColumn(
                name: "PromotionLabel",
                table: "pos_transaction_items");

            migrationBuilder.DropColumn(
                name: "PromotionRuleId",
                table: "pos_transaction_items");
        }
    }
}
