using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerMessageContentReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_customer_message_campaigns_TenantId",
                table: "customer_message_campaigns");

            migrationBuilder.AddColumn<Guid>(
                name: "ContentId",
                table: "customer_message_campaigns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentImageUrl",
                table: "customer_message_campaigns",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentTitle",
                table: "customer_message_campaigns",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "customer_message_campaigns",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentId",
                table: "customer_message_campaigns");

            migrationBuilder.DropColumn(
                name: "ContentImageUrl",
                table: "customer_message_campaigns");

            migrationBuilder.DropColumn(
                name: "ContentTitle",
                table: "customer_message_campaigns");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "customer_message_campaigns");

            migrationBuilder.CreateIndex(
                name: "IX_customer_message_campaigns_TenantId",
                table: "customer_message_campaigns",
                column: "TenantId");
        }
    }
}
