using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPosTxCustomerMigrationIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_pos_tx_customer_migration",
                table: "pos_transactions",
                columns: new[] { "TenantId", "CustomerId", "CreatedAt" },
                filter: "\"CustomerId\" IS NOT NULL AND \"Status\" <> 'fiscalization_failed'")
                .Annotation("Npgsql:IndexInclude", new[] { "LocationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_pos_tx_customer_migration",
                table: "pos_transactions");
        }
    }
}
