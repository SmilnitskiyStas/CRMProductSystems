using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations;

public partial class AddPosHistoricalPricingSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Additive only. Historical values cannot be inferred from today's catalog.
        migrationBuilder.AddColumn<decimal>("VatRate", "pos_transaction_items", type: "decimal(5,2)", nullable: true);
        migrationBuilder.AddColumn<decimal>("VatAmount", "pos_transaction_items", type: "decimal(12,2)", nullable: true);
        migrationBuilder.AddColumn<decimal>("TaxableAmount", "pos_transaction_items", type: "decimal(12,2)", nullable: true);
        migrationBuilder.AddColumn<decimal>("LineTotal", "pos_transaction_items", type: "decimal(12,2)", nullable: true);
        migrationBuilder.AddColumn<decimal>("UnitCost", "pos_transaction_items", type: "decimal(12,2)", nullable: true);
        migrationBuilder.AddColumn<string>("ProductNameSnapshot", "pos_transaction_items", type: "text", nullable: true);
        migrationBuilder.AddColumn<string>("BarcodeSnapshot", "pos_transaction_items", type: "text", nullable: true);
        migrationBuilder.AddColumn<string>("PriceSnapshotHash", "pos_transaction_items", type: "character varying(64)", maxLength: 64, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Retain historical receipt snapshots. Roll back the application without dropping these columns.");
}
