using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoyaltyBonusExclusions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BonusExclusionsEnabled",
                table: "loyalty_program_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeDiscountedItems",
                table: "loyalty_program_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ExcludedCategoryIdsJson",
                table: "loyalty_program_settings",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "ExcludedProductIdsJson",
                table: "loyalty_program_settings",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "ExclusionsApplyToAccrual",
                table: "loyalty_program_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExclusionsApplyToRedemption",
                table: "loyalty_program_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BonusExclusionsEnabled",
                table: "loyalty_program_settings");

            migrationBuilder.DropColumn(
                name: "ExcludeDiscountedItems",
                table: "loyalty_program_settings");

            migrationBuilder.DropColumn(
                name: "ExcludedCategoryIdsJson",
                table: "loyalty_program_settings");

            migrationBuilder.DropColumn(
                name: "ExcludedProductIdsJson",
                table: "loyalty_program_settings");

            migrationBuilder.DropColumn(
                name: "ExclusionsApplyToAccrual",
                table: "loyalty_program_settings");

            migrationBuilder.DropColumn(
                name: "ExclusionsApplyToRedemption",
                table: "loyalty_program_settings");
        }
    }
}
