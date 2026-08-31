using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerMessageDeliveryLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryMode",
                table: "customer_message_campaigns",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "draft");

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledAt",
                table: "customer_message_campaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "customer_message_campaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE customer_message_campaigns
                SET "DeliveryMode" = 'send_now', "SubmittedAt" = "CreatedAt"
                WHERE "Status" = 'integration_pending';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryMode",
                table: "customer_message_campaigns");

            migrationBuilder.DropColumn(
                name: "ScheduledAt",
                table: "customer_message_campaigns");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "customer_message_campaigns");
        }
    }
}
