using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMobileConfigConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TASK-544: MobileConfiguration.PublishedVersionId/DraftVersionId (the row a publish
            // repoints) and MobileConfigurationVersion.ConfigurationJson/Status (the row a publish
            // mutates draft -> published) now use Postgres's built-in `xmin` system column as an EF
            // Core optimistic-concurrency token (see AppDbContext:
            // e.Property<uint>("xmin").IsRowVersion() on both entities) — same fix shape as
            // 20260715054917_AddProductStockXminConcurrencyToken (TASK-356) and
            // 20260726164058_AddLoyaltyMembershipConcurrencyToken (TASK-414). Every Postgres table
            // already has `xmin`; it is a reserved system column and cannot be added via ALTER
            // TABLE ADD COLUMN (fails with "column name is reserved"). The scaffolded migration
            // mistook the new shadow properties for real columns; there is no schema change to
            // apply, and no backfill/default is needed for existing rows — xmin is already
            // populated by Postgres itself on every row that has ever existed.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op — see Up().
        }
    }
}
