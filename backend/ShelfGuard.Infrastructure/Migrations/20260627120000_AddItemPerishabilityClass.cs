using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShelfGuard.Infrastructure.Data;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260627120000_AddItemPerishabilityClass")]
    public partial class AddItemPerishabilityClass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: this migration was originally applied to prod out-of-band
            // (missing [Migration] attribute), so it may re-run on an already-migrated DB.
            migrationBuilder.Sql(@"
ALTER TABLE items ADD COLUMN IF NOT EXISTS ""PerishabilityClass"" character varying(20) NOT NULL DEFAULT 'standard';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PerishabilityClass",
                table: "items");
        }
    }
}
