using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// TASK-464 — redesigns the forgot/reset-password flow from a one-time link/token
    /// (TASK-455/456, live 2026-07-30 as commit 647bde4c) to a temporary password the user can
    /// log in with directly. No more link, no more separate "click link, enter new password"
    /// step — the temp password itself becomes the user's real, immediately-usable password.
    ///
    /// Drops <c>password_reset_tokens</c> entirely (table + its RLS policies — Postgres drops a
    /// table's policies automatically along with the table, no separate DROP POLICY needed).
    /// The corresponding <c>PasswordResetToken</c> entity, <c>IPasswordResetTokenRepository</c>,
    /// and its EF Core repository were deleted in the same commit as this migration — this was
    /// the third documented fail-open <c>tenant_isolation</c> exception (see
    /// database-schema.md's "Documented exceptions" table); with the table gone there is
    /// nothing left to except, back to two (users/refresh_tokens).
    ///
    /// Adds <c>users.TempPasswordExpiresAt</c> (nullable timestamptz) — while set and in the
    /// future, <c>users.PasswordHash</c> holds a temp password rather than one the user chose.
    /// See <see cref="ShelfGuard.Domain.Entities.User.HasActiveTempPassword"/>,
    /// <see cref="ShelfGuard.Domain.Entities.User.SetTempPasswordExpiry"/>,
    /// <see cref="ShelfGuard.Domain.Entities.User.ClearTempPasswordExpiry"/>. No background job
    /// expires it — it's a lazily-checked timestamp, same pattern as the pre-existing
    /// <see cref="ShelfGuard.Domain.Entities.User.LockoutUntil"/>/<see cref="ShelfGuard.Domain.Entities.User.IsLockedOut"/>
    /// (TASK-329). Generating/hashing/sending the actual temporary password and enforcing the
    /// 3-hour expiry at login are TASK-465 (backend-developer) — out of scope here, schema only.
    /// </summary>
    public partial class DropPasswordResetTokensAddTempPasswordExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "password_reset_tokens");

            migrationBuilder.AddColumn<DateTime>(
                name: "TempPasswordExpiresAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <summary>
        /// Rollback recreates the table/columns/indexes but NOT the RLS policies the original
        /// `AddPasswordResetTokens` migration applied via raw SQL (EF's auto-scaffolding only
        /// diffs the entity model, it doesn't know about that earlier migration's `Sql(...)`
        /// calls) — a rolled-back table would come back without RLS. Not hardened: this
        /// redesign is one-way by intent (the C# entity/repository backing this table are
        /// deleted, not just the schema), so a real rollback would need the code reverted too,
        /// at which point the missing RLS would need re-adding by hand regardless.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TempPasswordExpiresAt",
                table: "users");

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_password_reset_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_password_reset_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_password_reset_tokens_user",
                table: "password_reset_tokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_tokens_TokenHash",
                table: "password_reset_tokens",
                column: "TokenHash",
                unique: true);
        }
    }
}
