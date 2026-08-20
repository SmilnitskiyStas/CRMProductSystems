using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketplaceOrderDelayReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DelayReason",
                table: "marketplace_orders",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DelayReason",
                table: "marketplace_orders");
        }
    }
}
