using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockReceiptsTransfersTenantIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_stock_transfers_tenant_from_status",
                table: "stock_transfers",
                columns: new[] { "TenantId", "FromLocationId", "Status", "CreatedAt" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "idx_stock_transfers_tenant_to_status",
                table: "stock_transfers",
                columns: new[] { "TenantId", "ToLocationId", "Status", "CreatedAt" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "idx_stock_receipts_tenant_store_status",
                table: "stock_receipts",
                columns: new[] { "TenantId", "DestinationLocationId", "Status", "CreatedAt" },
                descending: new[] { false, false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_stock_transfers_tenant_from_status",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "idx_stock_transfers_tenant_to_status",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "idx_stock_receipts_tenant_store_status",
                table: "stock_receipts");
        }
    }
}
