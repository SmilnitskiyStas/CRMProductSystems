using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// Supplier-portal expansion — Phase 1 foundations (plan `1-partitioned-book.md`). Pure
    /// additive DDL, NO new tables, NO RLS policy changes — the two target tables (marketplace_orders,
    /// users) already carry tenant_isolation + provider_bypass + worker_bypass and the new nullable
    /// columns inherit them.
    ///
    /// Adds:
    ///   marketplace_orders.CreatedByUserName    varchar(255) NULL  (#4 — denormalized snapshot of
    ///     the client user who placed the order; avoids a cross-tenant users join under a supplier
    ///     session, same pattern as users.InvitedByName)
    ///   marketplace_orders.ExpectedDeliveryDate date NULL          (plan D5 — mutable supplier-set
    ///     expected delivery date; the reschedule endpoint + auto-order "in transit" integration
    ///     land in Phase 4, the column is landed now)
    ///   users.SupplierOrdersLastViewedAt        timestamptz NULL   (#3 — per-user seen-marker for
    ///     the "new order" badge; the badge endpoint lands in Phase 6a)
    /// </summary>
    public partial class AddSupplierExpansionFoundations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SupplierOrdersLastViewedAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserName",
                table: "marketplace_orders",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpectedDeliveryDate",
                table: "marketplace_orders",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SupplierOrdersLastViewedAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "CreatedByUserName",
                table: "marketplace_orders");

            migrationBuilder.DropColumn(
                name: "ExpectedDeliveryDate",
                table: "marketplace_orders");
        }
    }
}
