using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkforceSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "draft"),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_schedules_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_work_schedules_users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "schedule_shifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    BreakMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 60),
                    RoleOverride = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "scheduled"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_shifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_schedule_shifts_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_schedule_shifts_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_schedule_shifts_work_schedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "work_schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_schedule_shifts_date",
                table: "schedule_shifts",
                columns: new[] { "TenantId", "LocationId", "ShiftDate" });

            migrationBuilder.CreateIndex(
                name: "idx_schedule_shifts_schedule",
                table: "schedule_shifts",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "idx_schedule_shifts_user_date",
                table: "schedule_shifts",
                columns: new[] { "TenantId", "UserId", "ShiftDate" });

            migrationBuilder.CreateIndex(
                name: "IX_schedule_shifts_LocationId",
                table: "schedule_shifts",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_shifts_UserId",
                table: "schedule_shifts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "idx_work_schedules_tenant_location",
                table: "work_schedules",
                columns: new[] { "TenantId", "LocationId", "WeekStart" });

            migrationBuilder.CreateIndex(
                name: "IX_work_schedules_CreatedBy",
                table: "work_schedules",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_work_schedules_LocationId",
                table: "work_schedules",
                column: "LocationId");

            // ── RLS: work_schedules ──────────────────────────────────────────
            migrationBuilder.Sql("ALTER TABLE work_schedules ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY work_schedules_tenant ON work_schedules
                    USING ("TenantId" = current_setting('app.tenant_id')::uuid);
                """);

            // ── RLS: schedule_shifts ─────────────────────────────────────────
            migrationBuilder.Sql("ALTER TABLE schedule_shifts ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY schedule_shifts_tenant ON schedule_shifts
                    USING ("TenantId" = current_setting('app.tenant_id')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "schedule_shifts");

            migrationBuilder.DropTable(
                name: "work_schedules");
        }
    }
}
