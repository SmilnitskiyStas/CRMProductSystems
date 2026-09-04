using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformCategoryItemDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultItemType",
                table: "platform_categories",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultManagementType",
                table: "platform_categories",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultPerishabilityClass",
                table: "platform_categories",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultShelfLifeDays",
                table: "platform_categories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultVatRate",
                table: "platform_categories",
                type: "numeric(5,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultItemType",
                table: "platform_categories");

            migrationBuilder.DropColumn(
                name: "DefaultManagementType",
                table: "platform_categories");

            migrationBuilder.DropColumn(
                name: "DefaultPerishabilityClass",
                table: "platform_categories");

            migrationBuilder.DropColumn(
                name: "DefaultShelfLifeDays",
                table: "platform_categories");

            migrationBuilder.DropColumn(
                name: "DefaultVatRate",
                table: "platform_categories");
        }
    }
}
