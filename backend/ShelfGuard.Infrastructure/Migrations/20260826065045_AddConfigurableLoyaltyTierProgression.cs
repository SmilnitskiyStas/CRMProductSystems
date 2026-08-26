using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigurableLoyaltyTierProgression : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "loyalty_tier_definitions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "loyalty_tier_definitions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinBonusSpend",
                table: "loyalty_tier_definitions",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinCashSpend",
                table: "loyalty_tier_definitions",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinEarnedBonuses",
                table: "loyalty_tier_definitions",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinMembershipDays",
                table: "loyalty_tier_definitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinPurchaseCount",
                table: "loyalty_tier_definitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinReviewCount",
                table: "loyalty_tier_definitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireCompletedProfile",
                table: "loyalty_tier_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "TierBonusSpend",
                table: "loyalty_memberships",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TierCashSpend",
                table: "loyalty_memberships",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TierEarnedBonuses",
                table: "loyalty_memberships",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "TierMembershipDays",
                table: "loyalty_memberships",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "TierProfileCompleted",
                table: "loyalty_memberships",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TierPurchaseCount",
                table: "loyalty_memberships",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TierReviewCount",
                table: "loyalty_memberships",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "loyalty_tier_definitions");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "loyalty_tier_definitions");

            migrationBuilder.DropColumn(
                name: "MinBonusSpend",
                table: "loyalty_tier_definitions");

            migrationBuilder.DropColumn(
                name: "MinCashSpend",
                table: "loyalty_tier_definitions");

            migrationBuilder.DropColumn(
                name: "MinEarnedBonuses",
                table: "loyalty_tier_definitions");

            migrationBuilder.DropColumn(
                name: "MinMembershipDays",
                table: "loyalty_tier_definitions");

            migrationBuilder.DropColumn(
                name: "MinPurchaseCount",
                table: "loyalty_tier_definitions");

            migrationBuilder.DropColumn(
                name: "MinReviewCount",
                table: "loyalty_tier_definitions");

            migrationBuilder.DropColumn(
                name: "RequireCompletedProfile",
                table: "loyalty_tier_definitions");

            migrationBuilder.DropColumn(
                name: "TierBonusSpend",
                table: "loyalty_memberships");

            migrationBuilder.DropColumn(
                name: "TierCashSpend",
                table: "loyalty_memberships");

            migrationBuilder.DropColumn(
                name: "TierEarnedBonuses",
                table: "loyalty_memberships");

            migrationBuilder.DropColumn(
                name: "TierMembershipDays",
                table: "loyalty_memberships");

            migrationBuilder.DropColumn(
                name: "TierProfileCompleted",
                table: "loyalty_memberships");

            migrationBuilder.DropColumn(
                name: "TierPurchaseCount",
                table: "loyalty_memberships");

            migrationBuilder.DropColumn(
                name: "TierReviewCount",
                table: "loyalty_memberships");
        }
    }
}
