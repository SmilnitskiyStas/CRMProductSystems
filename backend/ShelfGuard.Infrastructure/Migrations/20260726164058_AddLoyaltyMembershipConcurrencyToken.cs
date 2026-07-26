using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoyaltyMembershipConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TASK-414 (security review TASK-412, finding B): LoyaltyMembership.Balance now
            // uses Postgres's built-in `xmin` system column as an EF Core optimistic-
            // concurrency token (see AppDbContext: e.Property<uint>("xmin").IsRowVersion() on
            // the LoyaltyMembership entity) — same fix shape as
            // 20260715054917_AddProductStockXminConcurrencyToken (TASK-356). Every Postgres
            // table already has `xmin`; it is a reserved system column and cannot be added via
            // ALTER TABLE ADD COLUMN (fails with "column name is reserved"). The scaffolded
            // migration mistook the new shadow property for a real column; there is no schema
            // change to apply, and no backfill/default is needed for existing rows — xmin is
            // already populated by Postgres itself on every row that has ever existed.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op — see Up().
        }
    }
}
