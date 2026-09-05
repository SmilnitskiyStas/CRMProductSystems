using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// Supplier-portal expansion — Phase 8 (TASK-695). <c>marketplace_orders.ConfirmedAt</c> —
    /// the timestamp of the new → confirmed transition, stamped next to
    /// <c>ConfirmedByUserId</c>/<c>ConfirmedByUserName</c> by
    /// <c>MarketplaceOrderService.UpdateOrderStatusAsync</c>. Nullable, no backfill: orders
    /// confirmed before this column existed keep a null value (the team-performance rollup
    /// windows those orders by <c>CreatedAt</c> and drops them from the timing means only).
    /// Feeds the supplier team-performance "avg hours to confirm" KPI.
    /// </summary>
    public partial class AddMarketplaceOrderConfirmedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfirmedAt",
                table: "marketplace_orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "marketplace_orders");
        }
    }
}
