using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// TASK-613 (customer/loyalty domain expansion, approved plan `goofy-bubbling-naur.md`).
    /// Schema-ready placeholder for future register-hardware integration. No FK (no
    /// register entity exists yet) and intentionally unwired — no business logic reads or
    /// writes this column in this task.
    /// </summary>
    public partial class AddPosTransactionCashRegisterId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CashRegisterId",
                table: "pos_transactions",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CashRegisterId",
                table: "pos_transactions");
        }
    }
}
