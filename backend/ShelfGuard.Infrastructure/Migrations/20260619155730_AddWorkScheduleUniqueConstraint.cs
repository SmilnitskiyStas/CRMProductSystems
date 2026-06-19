using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkScheduleUniqueConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_work_schedules_tenant_location",
                table: "work_schedules");

            migrationBuilder.CreateIndex(
                name: "uq_work_schedules_tenant_location_week",
                table: "work_schedules",
                columns: new[] { "TenantId", "LocationId", "WeekStart" },
                unique: true,
                filter: "\"Status\" <> 'archived'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_work_schedules_tenant_location_week",
                table: "work_schedules");

            migrationBuilder.CreateIndex(
                name: "idx_work_schedules_tenant_location",
                table: "work_schedules",
                columns: new[] { "TenantId", "LocationId", "WeekStart" });
        }
    }
}
