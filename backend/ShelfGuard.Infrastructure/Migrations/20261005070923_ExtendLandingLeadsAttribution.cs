using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExtendLandingLeadsAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdminNote",
                table: "landing_leads",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Locale",
                table: "landing_leads",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PageUrl",
                table: "landing_leads",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessedAt",
                table: "landing_leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessedByUserId",
                table: "landing_leads",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Referrer",
                table: "landing_leads",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmCampaign",
                table: "landing_leads",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmMedium",
                table: "landing_leads",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmSource",
                table: "landing_leads",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_landing_leads_IsProcessed",
                table: "landing_leads",
                column: "IsProcessed");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_landing_leads_IsProcessed",
                table: "landing_leads");

            migrationBuilder.DropColumn(
                name: "AdminNote",
                table: "landing_leads");

            migrationBuilder.DropColumn(
                name: "Locale",
                table: "landing_leads");

            migrationBuilder.DropColumn(
                name: "PageUrl",
                table: "landing_leads");

            migrationBuilder.DropColumn(
                name: "ProcessedAt",
                table: "landing_leads");

            migrationBuilder.DropColumn(
                name: "ProcessedByUserId",
                table: "landing_leads");

            migrationBuilder.DropColumn(
                name: "Referrer",
                table: "landing_leads");

            migrationBuilder.DropColumn(
                name: "UtmCampaign",
                table: "landing_leads");

            migrationBuilder.DropColumn(
                name: "UtmMedium",
                table: "landing_leads");

            migrationBuilder.DropColumn(
                name: "UtmSource",
                table: "landing_leads");
        }
    }
}
