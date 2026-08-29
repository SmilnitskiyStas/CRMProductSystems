using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotionCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PromotionCampaignId",
                table: "discounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "promotion_campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Eyebrow = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Terms = table.Column<string>(type: "text", nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BackgroundColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AccentColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AudienceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AudienceTierIdsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_campaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "promotion_campaign_locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_campaign_locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_promotion_campaign_locations_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_promotion_campaign_locations_promotion_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "promotion_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "promotion_campaign_products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_campaign_products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_promotion_campaign_products_items_ProductId",
                        column: x => x.ProductId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_promotion_campaign_products_promotion_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "promotion_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_discounts_PromotionCampaignId",
                table: "discounts",
                column: "PromotionCampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_campaign_locations_CampaignId_LocationId",
                table: "promotion_campaign_locations",
                columns: new[] { "CampaignId", "LocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_promotion_campaign_locations_LocationId",
                table: "promotion_campaign_locations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_campaign_products_CampaignId_ProductId",
                table: "promotion_campaign_products",
                columns: new[] { "CampaignId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_promotion_campaign_products_ProductId",
                table: "promotion_campaign_products",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_campaigns_TenantId_Status_StartsAt",
                table: "promotion_campaigns",
                columns: new[] { "TenantId", "Status", "StartsAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_discounts_promotion_campaigns_PromotionCampaignId",
                table: "discounts",
                column: "PromotionCampaignId",
                principalTable: "promotion_campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql(@"
                ALTER TABLE promotion_campaigns ENABLE ROW LEVEL SECURITY;
                ALTER TABLE promotion_campaigns FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON promotion_campaigns USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON promotion_campaigns USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON promotion_campaigns USING (current_setting('app.role', true) = 'worker');

                ALTER TABLE promotion_campaign_locations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE promotion_campaign_locations FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON promotion_campaign_locations USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON promotion_campaign_locations USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON promotion_campaign_locations USING (current_setting('app.role', true) = 'worker');

                ALTER TABLE promotion_campaign_products ENABLE ROW LEVEL SECURITY;
                ALTER TABLE promotion_campaign_products FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON promotion_campaign_products USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON promotion_campaign_products USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON promotion_campaign_products USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_discounts_promotion_campaigns_PromotionCampaignId",
                table: "discounts");

            migrationBuilder.DropTable(
                name: "promotion_campaign_locations");

            migrationBuilder.DropTable(
                name: "promotion_campaign_products");

            migrationBuilder.DropTable(
                name: "promotion_campaigns");

            migrationBuilder.DropIndex(
                name: "IX_discounts_PromotionCampaignId",
                table: "discounts");

            migrationBuilder.DropColumn(
                name: "PromotionCampaignId",
                table: "discounts");
        }
    }
}
