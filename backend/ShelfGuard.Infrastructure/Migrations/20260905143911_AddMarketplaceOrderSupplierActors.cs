using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketplaceOrderSupplierActors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedByUserId",
                table: "marketplace_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmedByUserName",
                table: "marketplace_orders",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ShippedByUserId",
                table: "marketplace_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippedByUserName",
                table: "marketplace_orders",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmedByUserId",
                table: "marketplace_orders");

            migrationBuilder.DropColumn(
                name: "ConfirmedByUserName",
                table: "marketplace_orders");

            migrationBuilder.DropColumn(
                name: "ShippedByUserId",
                table: "marketplace_orders");

            migrationBuilder.DropColumn(
                name: "ShippedByUserName",
                table: "marketplace_orders");
        }
    }
}
