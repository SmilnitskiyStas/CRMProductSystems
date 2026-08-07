using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPosTransactionItemProductCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pos_transaction_items_ProductId",
                table: "pos_transaction_items");

            migrationBuilder.CreateIndex(
                name: "idx_pos_transaction_items_product_covering",
                table: "pos_transaction_items",
                columns: new[] { "ProductId", "TransactionId" })
                .Annotation("Npgsql:IndexInclude", new[] { "Quantity", "PriceFinal" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_pos_transaction_items_product_covering",
                table: "pos_transaction_items");

            migrationBuilder.CreateIndex(
                name: "IX_pos_transaction_items_ProductId",
                table: "pos_transaction_items",
                column: "ProductId");
        }
    }
}
