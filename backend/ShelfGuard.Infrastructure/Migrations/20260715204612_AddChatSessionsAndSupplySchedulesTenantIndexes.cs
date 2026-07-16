using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatSessionsAndSupplySchedulesTenantIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_supply_schedules_tenant",
                table: "supply_schedules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "idx_chat_sessions_tenant_updated",
                table: "chat_sessions",
                columns: new[] { "TenantId", "UpdatedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_supply_schedules_tenant",
                table: "supply_schedules");

            migrationBuilder.DropIndex(
                name: "idx_chat_sessions_tenant_updated",
                table: "chat_sessions");
        }
    }
}
