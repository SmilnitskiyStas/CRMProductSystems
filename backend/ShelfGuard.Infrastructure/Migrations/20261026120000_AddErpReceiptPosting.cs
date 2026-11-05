using Microsoft.EntityFrameworkCore.Migrations;

namespace ShelfGuard.Infrastructure.Migrations;

public partial class AddErpReceiptPosting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(InventoryReceiptFenceSql.Upgrade);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Retain receipt postings and inventory fences. Pause and reconcile before maintenance rollback.");
}
