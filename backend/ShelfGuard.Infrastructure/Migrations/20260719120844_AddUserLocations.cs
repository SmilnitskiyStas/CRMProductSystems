using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_locations_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_locations_users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_user_locations_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_user_locations_tenant_location",
                table: "user_locations",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_user_locations_AssignedByUserId",
                table: "user_locations",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_locations_LocationId",
                table: "user_locations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_user_locations_UserId",
                table: "user_locations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "uq_user_locations_tenant_user_location",
                table: "user_locations",
                columns: new[] { "TenantId", "UserId", "LocationId" },
                unique: true);

            // ── RLS: user_locations (TASK-392 Stage 1) ───────────────────────────
            // Standard tenant_isolation + provider_bypass + worker_bypass triad — same
            // shape as every RLS table since FixAllRlsPoliciesNullIfEmptyString/
            // AddWorkerBypassRlsPolicy (see AddTenantRoles for the most recent precedent).
            // This is NOT the store_scope RESTRICTIVE policy that will later read this
            // table to gate product_stock/daily_sales/pos_shifts/etc — that's Stage 3, a
            // separate future migration on those OTHER tables. Here we only need this
            // table itself to be tenant-isolated like any other tenant-owned table.
            migrationBuilder.Sql(@"
                ALTER TABLE user_locations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE user_locations FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON user_locations
                  USING (""TenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                CREATE POLICY provider_bypass ON user_locations
                  USING (current_setting('app.role', true) = 'provider');
                CREATE POLICY worker_bypass ON user_locations
                  USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS worker_bypass ON user_locations;
                DROP POLICY IF EXISTS provider_bypass ON user_locations;
                DROP POLICY IF EXISTS tenant_isolation ON user_locations;
                ALTER TABLE user_locations DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "user_locations");
        }
    }
}
