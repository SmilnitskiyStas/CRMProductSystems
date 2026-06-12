using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TelegramLinkCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "telegram_link_codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_link_codes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_telegram_link_codes_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_telegram_link_codes_Code",
                table: "telegram_link_codes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_link_codes_UserId",
                table: "telegram_link_codes",
                column: "UserId");

            // RLS: tenant derived through the owning user (worker bypasses as table owner).
            migrationBuilder.Sql(@"
                ALTER TABLE telegram_link_codes ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON telegram_link_codes
                  USING (EXISTS (
                    SELECT 1 FROM users u
                    WHERE u.""Id"" = ""UserId""
                      AND u.""TenantId"" = current_setting('app.tenant_id', true)::uuid));
                CREATE POLICY provider_bypass ON telegram_link_codes
                  USING (current_setting('app.role', true) = 'provider');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telegram_link_codes");
        }
    }
}
