using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPermissionGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_permission_grants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    NotifiedExpiringAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotifiedExpiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_permission_grants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_permission_grants_users_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_permission_grants_users_RevokedByUserId",
                        column: x => x.RevokedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_user_permission_grants_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_user_permission_grants_expires_active",
                table: "user_permission_grants",
                column: "ExpiresAt",
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "idx_user_permission_grants_tenant_user",
                table: "user_permission_grants",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_user_permission_grants_GrantedByUserId",
                table: "user_permission_grants",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_permission_grants_RevokedByUserId",
                table: "user_permission_grants",
                column: "RevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_permission_grants_UserId",
                table: "user_permission_grants",
                column: "UserId");

            // ── RLS: user_permission_grants ──────────────────────────────────────
            // Standard tenant_isolation + provider_bypass pattern (ADR-019, TASK-341).
            // NULLIF guard: current_setting(..., true) returns '' after RESET, which
            // breaks the ::uuid cast without it.
            migrationBuilder.Sql(@"
                ALTER TABLE user_permission_grants ENABLE ROW LEVEL SECURITY;
                ALTER TABLE user_permission_grants FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON user_permission_grants
                  USING (""TenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                CREATE POLICY provider_bypass ON user_permission_grants
                  USING (current_setting('app.role', true) = 'provider');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS provider_bypass ON user_permission_grants;
                DROP POLICY IF EXISTS tenant_isolation ON user_permission_grants;
                ALTER TABLE user_permission_grants DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "user_permission_grants");
        }
    }
}
