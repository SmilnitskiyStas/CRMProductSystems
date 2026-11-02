using Microsoft.EntityFrameworkCore.Migrations;
namespace ShelfGuard.Infrastructure.Migrations;
public partial class AddErpPosSalePosting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(InventoryPosFenceSql.Upgrade);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Retain ERP POS receipts and ledger. Pause writers and reconcile before restore.");
}
