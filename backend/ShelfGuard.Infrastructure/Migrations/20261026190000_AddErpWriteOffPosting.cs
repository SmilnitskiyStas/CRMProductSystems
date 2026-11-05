using Microsoft.EntityFrameworkCore.Migrations;
namespace ShelfGuard.Infrastructure.Migrations;
public partial class AddErpWriteOffPosting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(InventoryWriteOffFenceSql.Upgrade);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Retain write-off decisions and inventory history. Pause and reconcile before maintenance restore.");
}
