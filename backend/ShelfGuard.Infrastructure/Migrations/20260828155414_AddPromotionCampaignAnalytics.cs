using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotionCampaignAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "promotion_campaign_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_campaign_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_promotion_campaign_events_consumer_accounts_ConsumerAccount~",
                        column: x => x.ConsumerAccountId,
                        principalTable: "consumer_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_promotion_campaign_events_locations_StoreId",
                        column: x => x.StoreId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_promotion_campaign_events_promotion_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "promotion_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_promotion_campaign_events_analytics",
                table: "promotion_campaign_events",
                columns: new[] { "TenantId", "CampaignId", "EventType", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "idx_promotion_campaign_events_store",
                table: "promotion_campaign_events",
                columns: new[] { "TenantId", "StoreId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_promotion_campaign_events_CampaignId",
                table: "promotion_campaign_events",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_campaign_events_ConsumerAccountId",
                table: "promotion_campaign_events",
                column: "ConsumerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_campaign_events_StoreId",
                table: "promotion_campaign_events",
                column: "StoreId");

            migrationBuilder.Sql(@"
                ALTER TABLE promotion_campaign_events ENABLE ROW LEVEL SECURITY;
                ALTER TABLE promotion_campaign_events FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON promotion_campaign_events USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON promotion_campaign_events USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON promotion_campaign_events USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "promotion_campaign_events");
        }
    }
}
