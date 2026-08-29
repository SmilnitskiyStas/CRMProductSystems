using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMobileCatalogPublicationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_mobile_catalog_settings_tenant",
                table: "mobile_catalog_settings");

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "mobile_catalog_settings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "mobile_catalog_settings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()");

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "mobile_catalog_settings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "mobile_catalog_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ImageUrlSnapshot",
                table: "mobile_catalog_items",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MobilePriceSnapshot",
                table: "mobile_catalog_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductNameSnapshot",
                table: "mobile_catalog_items",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "RegularPriceSnapshot",
                table: "mobile_catalog_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitSnapshot",
                table: "mobile_catalog_items",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
                UPDATE mobile_catalog_settings
                SET ""Status"" = CASE WHEN ""IsEnabled"" THEN 'published' ELSE 'draft' END,
                    ""PublishedAt"" = CASE WHEN ""IsEnabled"" THEN ""UpdatedAt"" ELSE NULL END;

                UPDATE mobile_catalog_items AS catalog_item
                SET ""ProductNameSnapshot"" = item.""Name"",
                    ""UnitSnapshot"" = item.""Unit"",
                    ""ImageUrlSnapshot"" = item.""ImageUrl"",
                    ""RegularPriceSnapshot"" = item.""PriceRetail"",
                    ""MobilePriceSnapshot"" = CASE
                        WHEN catalog_item.""MobileDiscountPercent"" IS NOT NULL AND item.""PriceRetail"" IS NOT NULL
                        THEN ROUND(item.""PriceRetail"" * (1 - catalog_item.""MobileDiscountPercent"" / 100), 2)
                        ELSE NULL
                    END
                FROM items AS item
                WHERE item.""Id"" = catalog_item.""ProductId"";
            ");

            migrationBuilder.CreateIndex(
                name: "idx_mobile_catalog_settings_tenant_status_publish",
                table: "mobile_catalog_settings",
                columns: new[] { "TenantId", "Status", "PublishAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_mobile_catalog_settings_tenant_status_publish",
                table: "mobile_catalog_settings");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "mobile_catalog_settings");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "mobile_catalog_settings");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "mobile_catalog_settings");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "mobile_catalog_settings");

            migrationBuilder.DropColumn(
                name: "ImageUrlSnapshot",
                table: "mobile_catalog_items");

            migrationBuilder.DropColumn(
                name: "MobilePriceSnapshot",
                table: "mobile_catalog_items");

            migrationBuilder.DropColumn(
                name: "ProductNameSnapshot",
                table: "mobile_catalog_items");

            migrationBuilder.DropColumn(
                name: "RegularPriceSnapshot",
                table: "mobile_catalog_items");

            migrationBuilder.DropColumn(
                name: "UnitSnapshot",
                table: "mobile_catalog_items");

            migrationBuilder.CreateIndex(
                name: "uq_mobile_catalog_settings_tenant",
                table: "mobile_catalog_settings",
                column: "TenantId",
                unique: true);
        }
    }
}
