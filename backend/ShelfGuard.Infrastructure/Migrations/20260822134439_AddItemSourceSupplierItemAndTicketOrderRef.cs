using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemSourceSupplierItemAndTicketOrderRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MarketplaceOrderId",
                table: "supplier_support_tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceSupplierItemId",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_support_tickets_MarketplaceOrderId",
                table: "supplier_support_tickets",
                column: "MarketplaceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_items_SourceSupplierItemId",
                table: "items",
                column: "SourceSupplierItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_items_supplier_items_SourceSupplierItemId",
                table: "items",
                column: "SourceSupplierItemId",
                principalTable: "supplier_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_support_tickets_marketplace_orders_MarketplaceOrde~",
                table: "supplier_support_tickets",
                column: "MarketplaceOrderId",
                principalTable: "marketplace_orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_items_supplier_items_SourceSupplierItemId",
                table: "items");

            migrationBuilder.DropForeignKey(
                name: "FK_supplier_support_tickets_marketplace_orders_MarketplaceOrde~",
                table: "supplier_support_tickets");

            migrationBuilder.DropIndex(
                name: "IX_supplier_support_tickets_MarketplaceOrderId",
                table: "supplier_support_tickets");

            migrationBuilder.DropIndex(
                name: "IX_items_SourceSupplierItemId",
                table: "items");

            migrationBuilder.DropColumn(
                name: "MarketplaceOrderId",
                table: "supplier_support_tickets");

            migrationBuilder.DropColumn(
                name: "SourceSupplierItemId",
                table: "items");
        }
    }
}
