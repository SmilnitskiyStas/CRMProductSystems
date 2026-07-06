using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierRolesAndTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SupplierRoleId",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "supplier_roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BaseRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Permissions = table.Column<List<string>>(type: "text[]", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "supplier_tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientTenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedToUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_tasks_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supplier_tasks_tenants_ClientTenantId",
                        column: x => x.ClientTenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_supplier_tasks_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_tasks_users_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_supplier_tasks_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_SupplierRoleId",
                table: "users",
                column: "SupplierRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_roles_TenantId",
                table: "supplier_roles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_tasks_AssignedToUserId",
                table: "supplier_tasks",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_tasks_ClientTenantId",
                table: "supplier_tasks",
                column: "ClientTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_tasks_CreatedByUserId",
                table: "supplier_tasks",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_tasks_Status",
                table: "supplier_tasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_tasks_SupplierId",
                table: "supplier_tasks",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_tasks_TenantId",
                table: "supplier_tasks",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_users_supplier_roles_SupplierRoleId",
                table: "users",
                column: "SupplierRoleId",
                principalTable: "supplier_roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── RLS: supplier_roles ─────────────────────────────────────────────
            // NULLIF guard: current_setting(..., true) returns '' after RESET, which
            // breaks the ::uuid cast without this guard (see known-issues.md).
            migrationBuilder.Sql(@"
                ALTER TABLE supplier_roles ENABLE ROW LEVEL SECURITY;
                ALTER TABLE supplier_roles FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON supplier_roles
                  USING (""TenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                CREATE POLICY provider_bypass ON supplier_roles
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: supplier_tasks ─────────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE supplier_tasks ENABLE ROW LEVEL SECURITY;
                ALTER TABLE supplier_tasks FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON supplier_tasks
                  USING (""TenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                CREATE POLICY provider_bypass ON supplier_tasks
                  USING (current_setting('app.role', true) = 'provider');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_users_supplier_roles_SupplierRoleId",
                table: "users");

            migrationBuilder.DropTable(
                name: "supplier_roles");

            migrationBuilder.DropTable(
                name: "supplier_tasks");

            migrationBuilder.DropIndex(
                name: "IX_users_SupplierRoleId",
                table: "users");

            migrationBuilder.DropColumn(
                name: "SupplierRoleId",
                table: "users");
        }
    }
}
