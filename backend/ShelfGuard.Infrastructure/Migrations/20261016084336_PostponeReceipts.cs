using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PostponeReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DelayReason",
                table: "stock_receipts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "OriginalExpectedAt",
                table: "stock_receipts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PostponeCount",
                table: "stock_receipts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PostponedAt",
                table: "stock_receipts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PostponedBy",
                table: "stock_receipts",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DelayReason",
                table: "stock_receipts");

            migrationBuilder.DropColumn(
                name: "OriginalExpectedAt",
                table: "stock_receipts");

            migrationBuilder.DropColumn(
                name: "PostponeCount",
                table: "stock_receipts");

            migrationBuilder.DropColumn(
                name: "PostponedAt",
                table: "stock_receipts");

            migrationBuilder.DropColumn(
                name: "PostponedBy",
                table: "stock_receipts");
        }
    }
}
