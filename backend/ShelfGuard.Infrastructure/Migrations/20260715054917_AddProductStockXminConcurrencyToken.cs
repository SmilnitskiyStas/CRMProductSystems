using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductStockXminConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TASK-356: ProductStock.Quantity now uses Postgres's built-in `xmin` system
            // column as an EF Core optimistic-concurrency token (see AppDbContext:
            // e.Property<uint>("xmin").IsRowVersion() on the ProductStock entity). Every
            // Postgres table already has `xmin` — it is a reserved system column and
            // cannot be added via ALTER TABLE ADD COLUMN (that would fail with
            // "column name is reserved"). The scaffolded migration mistook the new
            // shadow property for a real column; there is no schema change to apply.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op — see Up().
        }
    }
}
