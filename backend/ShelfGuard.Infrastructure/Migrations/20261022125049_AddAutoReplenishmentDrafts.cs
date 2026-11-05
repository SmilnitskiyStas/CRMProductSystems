using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoReplenishmentDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "stock_receipts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AddColumn<DateTime>(
                name: "TakenAt",
                table: "stock_receipts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TakenByUserId",
                table: "stock_receipts",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "stock_receipts");

            migrationBuilder.DropColumn(
                name: "TakenAt",
                table: "stock_receipts");

            migrationBuilder.DropColumn(
                name: "TakenByUserId",
                table: "stock_receipts");
        }
    }
}
