using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBeautyControlPlane : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Message",
                table: "landing_leads",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BeautyTenantRef",
                table: "landing_leads",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BeautyTenantSlug",
                table: "landing_leads",
                type: "character varying(63)",
                maxLength: 63,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Contact",
                table: "landing_leads",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "landing_leads",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReporterRole",
                table: "landing_leads",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestType",
                table: "landing_leads",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "beauty_tenant_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BeautyTenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    BeautySlug = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    BeautyStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "active"),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_beauty_tenant_links", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_landing_leads_ExternalId",
                table: "landing_leads",
                column: "ExternalId",
                unique: true,
                filter: "\"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_beauty_tenant_links_BeautySlug",
                table: "beauty_tenant_links",
                column: "BeautySlug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_beauty_tenant_links_TenantId",
                table: "beauty_tenant_links",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "beauty_tenant_links");

            migrationBuilder.DropIndex(
                name: "IX_landing_leads_ExternalId",
                table: "landing_leads");

            migrationBuilder.DropColumn(
                name: "BeautyTenantRef",
                table: "landing_leads");

            migrationBuilder.DropColumn(
                name: "BeautyTenantSlug",
                table: "landing_leads");

            migrationBuilder.DropColumn(
                name: "Contact",
                table: "landing_leads");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "landing_leads");

            migrationBuilder.DropColumn(
                name: "ReporterRole",
                table: "landing_leads");

            migrationBuilder.DropColumn(
                name: "RequestType",
                table: "landing_leads");

            migrationBuilder.AlterColumn<string>(
                name: "Message",
                table: "landing_leads",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);
        }
    }
}
