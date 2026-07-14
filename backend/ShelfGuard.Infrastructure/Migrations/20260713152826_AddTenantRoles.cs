using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantRoleId",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tenant_roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Capabilities = table.Column<List<string>>(type: "text[]", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_roles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tenant_roles_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_TenantRoleId",
                table: "users",
                column: "TenantRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_roles_CreatedByUserId",
                table: "tenant_roles",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "uq_tenant_roles_tenant_name_active",
                table: "tenant_roles",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.AddForeignKey(
                name: "FK_users_tenant_roles_TenantRoleId",
                table: "users",
                column: "TenantRoleId",
                principalTable: "tenant_roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── RLS: tenant_roles (ADR-020, TASK-345) ────────────────────────────
            // Strict tenant_isolation (no permissive "IS NULL → allow all" fallback —
            // that variant only exists on a fixed legacy set predating
            // FixAllRlsPoliciesNullIfEmptyString and is not used for new tables) +
            // provider_bypass + worker_bypass, all three from day one per the
            // mandatory RLS rule (2026-07-12 incident: worker_bypass retrofitted onto
            // 73 tables after being missed on every table since FORCE RLS was turned
            // on — see 20260712175141_AddWorkerBypassRlsPolicy). NULLIF guard:
            // current_setting(..., true) returns '' after RESET, which breaks the
            // ::uuid cast without it.
            migrationBuilder.Sql(@"
                ALTER TABLE tenant_roles ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenant_roles FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON tenant_roles
                  USING (""TenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                CREATE POLICY provider_bypass ON tenant_roles
                  USING (current_setting('app.role', true) = 'provider');
                CREATE POLICY worker_bypass ON tenant_roles
                  USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS worker_bypass ON tenant_roles;
                DROP POLICY IF EXISTS provider_bypass ON tenant_roles;
                DROP POLICY IF EXISTS tenant_isolation ON tenant_roles;
                ALTER TABLE tenant_roles DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_users_tenant_roles_TenantRoleId",
                table: "users");

            migrationBuilder.DropTable(
                name: "tenant_roles");

            migrationBuilder.DropIndex(
                name: "IX_users_TenantRoleId",
                table: "users");

            migrationBuilder.DropColumn(
                name: "TenantRoleId",
                table: "users");
        }
    }
}
