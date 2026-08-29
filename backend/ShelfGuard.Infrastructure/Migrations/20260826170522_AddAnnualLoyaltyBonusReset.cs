using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnualLoyaltyBonusReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AnnualBonusResetDay",
                table: "loyalty_program_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "AnnualBonusResetEnabled",
                table: "loyalty_program_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AnnualBonusResetMonth",
                table: "loyalty_program_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "LastAnnualBonusResetYear",
                table: "loyalty_program_settings",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnnualBonusResetDay",
                table: "loyalty_program_settings");

            migrationBuilder.DropColumn(
                name: "AnnualBonusResetEnabled",
                table: "loyalty_program_settings");

            migrationBuilder.DropColumn(
                name: "AnnualBonusResetMonth",
                table: "loyalty_program_settings");

            migrationBuilder.DropColumn(
                name: "LastAnnualBonusResetYear",
                table: "loyalty_program_settings");
        }
    }
}
