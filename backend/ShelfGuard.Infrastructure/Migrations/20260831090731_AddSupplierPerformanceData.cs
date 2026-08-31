using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// Pure DDL for supplier performance data (TASK-649). NO new tables, NO RLS policy changes —
    /// the 4 target tables (locations, marketplace_orders, supplier_profiles, supplier_metrics)
    /// already have tenant_isolation + provider_bypass + worker_bypass and new columns inherit them.
    ///
    /// Adds:
    ///   locations.RegionCode              varchar(20) NULL   (structured Ukraine region code)
    ///   marketplace_orders.DestinationRegionCode  varchar(20) NULL   (snapshot at order creation)
    ///   supplier_profiles.DeliveryCoverage        jsonb NULL         (supersedes DeliveryRegions)
    ///   supplier_metrics.DeliveryByRegion         jsonb NULL
    ///   supplier_metrics.DeliverySampleSize       int NULL
    ///   supplier_metrics.ResponseSampleSize       int NULL
    ///   supplier_metrics.AggregatesComputedAt     timestamptz NULL
    /// Indexes:
    ///   IX_supplier_chat_messages_SessionId_SenderTenantId_CreatedAt  (worker response-time scan)
    ///   ix_marketplace_orders_metrics  ("SupplierTenantId","DeliveredAt") WHERE "Status" = 'delivered'
    ///     — partial; hand-written via raw SQL because EF does not emit the WHERE filter here.
    /// </summary>
    public partial class AddSupplierPerformanceData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryCoverage",
                table: "supplier_profiles",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AggregatesComputedAt",
                table: "supplier_metrics",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryByRegion",
                table: "supplier_metrics",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliverySampleSize",
                table: "supplier_metrics",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResponseSampleSize",
                table: "supplier_metrics",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationRegionCode",
                table: "marketplace_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegionCode",
                table: "locations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_chat_messages_SessionId_SenderTenantId_CreatedAt",
                table: "supplier_chat_messages",
                columns: new[] { "SessionId", "SenderTenantId", "CreatedAt" });

            // Partial index for the nightly supplier-metrics worker job's delivered-order scan.
            // Written by hand: EF's CreateIndex(filter:) support exists, but this index is not
            // tracked in the model snapshot (same treatment as the project's other raw-SQL
            // indexes/policies), so the DDL lives here directly.
            migrationBuilder.Sql(
                "CREATE INDEX ix_marketplace_orders_metrics ON marketplace_orders (\"SupplierTenantId\", \"DeliveredAt\") WHERE \"Status\" = 'delivered';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_marketplace_orders_metrics;");

            migrationBuilder.DropIndex(
                name: "IX_supplier_chat_messages_SessionId_SenderTenantId_CreatedAt",
                table: "supplier_chat_messages");

            migrationBuilder.DropColumn(
                name: "DeliveryCoverage",
                table: "supplier_profiles");

            migrationBuilder.DropColumn(
                name: "AggregatesComputedAt",
                table: "supplier_metrics");

            migrationBuilder.DropColumn(
                name: "DeliveryByRegion",
                table: "supplier_metrics");

            migrationBuilder.DropColumn(
                name: "DeliverySampleSize",
                table: "supplier_metrics");

            migrationBuilder.DropColumn(
                name: "ResponseSampleSize",
                table: "supplier_metrics");

            migrationBuilder.DropColumn(
                name: "DestinationRegionCode",
                table: "marketplace_orders");

            migrationBuilder.DropColumn(
                name: "RegionCode",
                table: "locations");
        }
    }
}
