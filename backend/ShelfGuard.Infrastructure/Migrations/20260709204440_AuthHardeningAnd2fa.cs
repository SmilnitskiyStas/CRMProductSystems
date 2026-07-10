using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuthHardeningAnd2fa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedLoginAttempts",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutUntil",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TotpEnabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "TotpLastTimestep",
                table: "users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TotpRecoveryCodes",
                table: "users",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TotpSecret",
                table: "users",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailedLoginAttempts",
                table: "users");

            migrationBuilder.DropColumn(
                name: "LockoutUntil",
                table: "users");

            migrationBuilder.DropColumn(
                name: "TotpEnabled",
                table: "users");

            migrationBuilder.DropColumn(
                name: "TotpLastTimestep",
                table: "users");

            migrationBuilder.DropColumn(
                name: "TotpRecoveryCodes",
                table: "users");

            migrationBuilder.DropColumn(
                name: "TotpSecret",
                table: "users");
        }
    }
}
