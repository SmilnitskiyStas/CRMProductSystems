using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RestorePosLoyaltyCheckoutAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LoyaltyMembershipId",
                table: "pos_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StoreId",
                table: "banner_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_pos_tx_loyalty_membership",
                table: "pos_transactions",
                column: "LoyaltyMembershipId",
                filter: "\"LoyaltyMembershipId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_banner_events_StoreId",
                table: "banner_events",
                column: "StoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_banner_events_locations_StoreId",
                table: "banner_events",
                column: "StoreId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_pos_transactions_loyalty_memberships_LoyaltyMembershipId",
                table: "pos_transactions",
                column: "LoyaltyMembershipId",
                principalTable: "loyalty_memberships",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_banner_events_locations_StoreId",
                table: "banner_events");

            migrationBuilder.DropForeignKey(
                name: "FK_pos_transactions_loyalty_memberships_LoyaltyMembershipId",
                table: "pos_transactions");

            migrationBuilder.DropIndex(
                name: "idx_pos_tx_loyalty_membership",
                table: "pos_transactions");

            migrationBuilder.DropIndex(
                name: "IX_banner_events_StoreId",
                table: "banner_events");

            migrationBuilder.DropColumn(
                name: "LoyaltyMembershipId",
                table: "pos_transactions");

            migrationBuilder.DropColumn(
                name: "StoreId",
                table: "banner_events");
        }
    }
}
