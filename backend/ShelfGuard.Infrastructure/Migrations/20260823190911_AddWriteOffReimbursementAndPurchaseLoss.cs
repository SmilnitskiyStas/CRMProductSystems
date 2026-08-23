using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWriteOffReimbursementAndPurchaseLoss : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TotalLossAmountPurchase",
                table: "write_offs",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalReimbursementAmount",
                table: "write_offs",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReturnedToSupplier",
                table: "write_off_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "LossAmountPurchase",
                table: "write_off_items",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReimbursementAmount",
                table: "write_off_items",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReimbursementType",
                table: "write_off_items",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReimbursementValue",
                table: "write_off_items",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPricePurchase",
                table: "write_off_items",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultReimbursementType",
                table: "items",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultReimbursementValue",
                table: "items",
                type: "numeric(12,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalLossAmountPurchase",
                table: "write_offs");

            migrationBuilder.DropColumn(
                name: "TotalReimbursementAmount",
                table: "write_offs");

            migrationBuilder.DropColumn(
                name: "IsReturnedToSupplier",
                table: "write_off_items");

            migrationBuilder.DropColumn(
                name: "LossAmountPurchase",
                table: "write_off_items");

            migrationBuilder.DropColumn(
                name: "ReimbursementAmount",
                table: "write_off_items");

            migrationBuilder.DropColumn(
                name: "ReimbursementType",
                table: "write_off_items");

            migrationBuilder.DropColumn(
                name: "ReimbursementValue",
                table: "write_off_items");

            migrationBuilder.DropColumn(
                name: "UnitPricePurchase",
                table: "write_off_items");

            migrationBuilder.DropColumn(
                name: "DefaultReimbursementType",
                table: "items");

            migrationBuilder.DropColumn(
                name: "DefaultReimbursementValue",
                table: "items");
        }
    }
}
