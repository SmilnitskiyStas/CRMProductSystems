using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShelfGuard.Infrastructure.Data;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations;

/// <summary>Adds the secret used by the consumer's universal checkout code.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260809180000_AddConsumerLoyaltyCodeSecret")]
public partial class AddConsumerLoyaltyCodeSecret : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LoyaltyTotpSecret",
            table: "consumer_accounts",
            type: "text",
            nullable: false,
            defaultValue: "");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "LoyaltyTotpSecret", table: "consumer_accounts");
}
