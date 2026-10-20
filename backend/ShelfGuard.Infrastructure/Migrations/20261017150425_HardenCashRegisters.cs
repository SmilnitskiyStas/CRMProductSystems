using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenCashRegisters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PinLockoutUntil",
                table: "cash_registers",
                newName: "PinFailWindowStart");

            migrationBuilder.RenameColumn(
                name: "FailedPinAttempts",
                table: "cash_registers",
                newName: "PinFailDailyCount");

            migrationBuilder.AddColumn<int>(
                name: "PosPinFailedAttempts",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PosPinLockoutUntil",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PinFailCount",
                table: "cash_registers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PinFailDay",
                table: "cash_registers",
                type: "date",
                nullable: true);

            // EF mapped the old FailedPinAttempts/PinLockoutUntil onto PinFailDailyCount/PinFailWindowStart
            // as renames; the meanings differ (windowed + daily counters), so start from a clean slate.
            migrationBuilder.Sql(@"UPDATE cash_registers SET ""PinFailWindowStart"" = NULL, ""PinFailDailyCount"" = 0, ""PinFailCount"" = 0;");

            // NOW() is the transaction START time: a long writer transaction would stamp rows older
            // than a cursor a register already holds. clock_timestamp() is wall-clock at the moment of
            // the UPDATE, narrowing (not eliminating - commit time can still lag) that window; clients
            // also resume with an overlap (see SyncCatalogResponse docs).
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION items_set_updated_at() RETURNS trigger AS $$
                BEGIN
                  NEW.""UpdatedAt"" := clock_timestamp();
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION items_set_updated_at() RETURNS trigger AS $$
                BEGIN
                  NEW.""UpdatedAt"" := NOW();
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
            ");

            migrationBuilder.DropColumn(
                name: "PosPinFailedAttempts",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PosPinLockoutUntil",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PinFailCount",
                table: "cash_registers");

            migrationBuilder.DropColumn(
                name: "PinFailDay",
                table: "cash_registers");

            migrationBuilder.RenameColumn(
                name: "PinFailWindowStart",
                table: "cash_registers",
                newName: "PinLockoutUntil");

            migrationBuilder.RenameColumn(
                name: "PinFailDailyCount",
                table: "cash_registers",
                newName: "FailedPinAttempts");
        }
    }
}
