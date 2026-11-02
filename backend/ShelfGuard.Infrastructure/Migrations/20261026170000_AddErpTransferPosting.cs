using Microsoft.EntityFrameworkCore.Migrations;
namespace ShelfGuard.Infrastructure.Migrations;
public partial class AddErpTransferPosting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(InventoryTransferFenceSql.Upgrade);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Retain transfer postings and fences; pause and reconcile before maintenance restore.");
}
