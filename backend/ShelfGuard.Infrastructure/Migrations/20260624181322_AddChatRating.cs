using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_notification_queue_tenant_unread;");

            migrationBuilder.AlterColumn<bool>(
                name: "IsRead",
                table: "notification_queue",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAt",
                table: "chat_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "chat_sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RatingComment",
                table: "chat_sessions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "chat_sessions");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "chat_sessions");

            migrationBuilder.DropColumn(
                name: "RatingComment",
                table: "chat_sessions");

            migrationBuilder.AlterColumn<bool>(
                name: "IsRead",
                table: "notification_queue",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.CreateIndex(
                name: "idx_notification_queue_tenant_unread",
                table: "notification_queue",
                columns: new[] { "TenantId", "IsRead" });
        }
    }
}
