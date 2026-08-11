using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoyaltyMembershipPreferredStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PreferredStoreId",
                table: "loyalty_memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_memberships_PreferredStoreId",
                table: "loyalty_memberships",
                column: "PreferredStoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_loyalty_memberships_locations_PreferredStoreId",
                table: "loyalty_memberships",
                column: "PreferredStoreId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_loyalty_memberships_locations_PreferredStoreId",
                table: "loyalty_memberships");

            migrationBuilder.DropIndex(
                name: "IX_loyalty_memberships_PreferredStoreId",
                table: "loyalty_memberships");

            migrationBuilder.DropColumn(
                name: "PreferredStoreId",
                table: "loyalty_memberships");
        }
    }
}
