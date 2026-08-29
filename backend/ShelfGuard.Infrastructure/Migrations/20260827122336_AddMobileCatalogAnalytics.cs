using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMobileCatalogAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mobile_catalog_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConsumerAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EventType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_catalog_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mobile_catalog_events_consumer_accounts_ConsumerAccountId",
                        column: x => x.ConsumerAccountId,
                        principalTable: "consumer_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_mobile_catalog_events_items_ProductId",
                        column: x => x.ProductId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_mobile_catalog_events_locations_StoreId",
                        column: x => x.StoreId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mobile_catalog_events_mobile_catalog_settings_CatalogId",
                        column: x => x.CatalogId,
                        principalTable: "mobile_catalog_settings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_mobile_catalog_events_analytics",
                table: "mobile_catalog_events",
                columns: new[] { "TenantId", "CatalogId", "EventType", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "idx_mobile_catalog_events_attribution",
                table: "mobile_catalog_events",
                columns: new[] { "TenantId", "ConsumerAccountId", "ProductId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_mobile_catalog_events_CatalogId",
                table: "mobile_catalog_events",
                column: "CatalogId");

            migrationBuilder.CreateIndex(
                name: "IX_mobile_catalog_events_ConsumerAccountId",
                table: "mobile_catalog_events",
                column: "ConsumerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_mobile_catalog_events_ProductId",
                table: "mobile_catalog_events",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_mobile_catalog_events_StoreId",
                table: "mobile_catalog_events",
                column: "StoreId");

            migrationBuilder.Sql(@"
                ALTER TABLE mobile_catalog_events ENABLE ROW LEVEL SECURITY;
                ALTER TABLE mobile_catalog_events FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON mobile_catalog_events USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON mobile_catalog_events USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON mobile_catalog_events USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mobile_catalog_events");
        }
    }
}
