using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRevocableIdentitySessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_UserId",
                table: "refresh_tokens");

            migrationBuilder.AddColumn<int>(
                name: "AuthVersion",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AuthVersion",
                table: "refresh_tokens",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "refresh_tokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "AuthVersion",
                table: "consumer_accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Preserve each existing staff refresh proof as its own migratable session.
            // New APIs can exchange the old cookie for a sid-bound access token.
            migrationBuilder.Sql("UPDATE refresh_tokens SET \"SessionId\" = \"Id\" WHERE \"SessionId\" = '00000000-0000-0000-0000-000000000000'::uuid;");

            migrationBuilder.CreateTable(
                name: "consumer_refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplacedByTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AuthVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumer_refresh_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_consumer_refresh_tokens_consumer_accounts_ConsumerAccountId",
                        column: x => x.ConsumerAccountId,
                        principalTable: "consumer_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_UserId_SessionId",
                table: "refresh_tokens",
                columns: new[] { "UserId", "SessionId" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_consumer_refresh_tokens_ConsumerAccountId_SessionId",
                table: "consumer_refresh_tokens",
                columns: new[] { "ConsumerAccountId", "SessionId" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_consumer_refresh_tokens_TokenHash",
                table: "consumer_refresh_tokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumer_refresh_tokens");

            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_UserId_SessionId",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "AuthVersion",
                table: "users");

            migrationBuilder.DropColumn(
                name: "AuthVersion",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "AuthVersion",
                table: "consumer_accounts");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_UserId",
                table: "refresh_tokens",
                column: "UserId");
        }
    }
}
