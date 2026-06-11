using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2Cannibalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "promo_cannibalization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AffectedProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderCoefficient = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "ai_suggested"),
                    IsApplied = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promo_cannibalization", x => x.Id);
                    table.ForeignKey(
                        name: "FK_promo_cannibalization_catalog_products_AffectedProductId",
                        column: x => x.AffectedProductId,
                        principalTable: "catalog_products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_promo_cannibalization_discounts_DiscountId",
                        column: x => x.DiscountId,
                        principalTable: "discounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_promo_cannibalization_AffectedProductId",
                table: "promo_cannibalization",
                column: "AffectedProductId");

            migrationBuilder.CreateIndex(
                name: "IX_promo_cannibalization_DiscountId_AffectedProductId",
                table: "promo_cannibalization",
                columns: new[] { "DiscountId", "AffectedProductId" },
                unique: true);

            migrationBuilder.Sql(@"
                ALTER TABLE promo_cannibalization ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON promo_cannibalization
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON promo_cannibalization
                  USING (current_setting('app.role', true) = 'provider');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "promo_cannibalization");
        }
    }
}
