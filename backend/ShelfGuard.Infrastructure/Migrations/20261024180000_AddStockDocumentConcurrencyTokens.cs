using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations;

public partial class AddStockDocumentConcurrencyTokens : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // EF metadata only: PostgreSQL already maintains xmin on stock_receipts and
        // stock_transfers. Never ADD/DROP this reserved system column or rewrite documents.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No schema or data change to reverse. Roll back the application to remove the check.
    }
}
