using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provider_roles",
                columns: table => new
                {
                    Id          = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BaseRole    = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Permissions = table.Column<List<string>>(type: "text[]", nullable: false),
                    IsSystem    = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt   = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_roles", x => x.Id);
                });

            // Seed system built-in roles
            migrationBuilder.Sql(@"
INSERT INTO provider_roles (""Id"", ""DisplayName"", ""BaseRole"", ""Permissions"", ""IsSystem"") VALUES
  (gen_random_uuid(), 'Адмін провайдера', 'provider_admin',
   ARRAY['team_management','view_clients','manage_clients','service_desk','live_chat','admin_panel','marketplace','analytics','schedule_management'],
   true),
  (gen_random_uuid(), 'Агент підтримки', 'provider_agent',
   ARRAY['view_clients','service_desk','live_chat'],
   true);
");

            migrationBuilder.AddColumn<Guid>(
                name: "ProviderRoleId",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_users_provider_roles_ProviderRoleId",
                table: "users",
                column: "ProviderRoleId",
                principalTable: "provider_roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_users_provider_roles_ProviderRoleId",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ProviderRoleId",
                table: "users");

            migrationBuilder.DropTable(
                name: "provider_roles");
        }
    }
}
