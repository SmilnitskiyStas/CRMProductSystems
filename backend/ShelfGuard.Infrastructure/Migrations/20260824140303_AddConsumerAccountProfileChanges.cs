using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// TASK-613 (customer/loyalty domain expansion, approved plan `goofy-bubbling-naur.md`).
    /// Append-only audit trail of ConsumerAccount profile edits. Deliberately carries NO
    /// RLS and NO TenantId column at all — same precedent as consumer_accounts itself
    /// (globally readable, protected only by application code; see AddLoyaltyProgram
    /// migration for the full rationale).
    /// </summary>
    public partial class AddConsumerAccountProfileChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumer_account_profile_changes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ConsumerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldName = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OldValue = table.Column<string>(type: "text", nullable: true),
                    NewValue = table.Column<string>(type: "text", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumer_account_profile_changes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_consumer_account_profile_changes_consumer_accounts_Consumer~",
                        column: x => x.ConsumerAccountId,
                        principalTable: "consumer_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_consumer_account_profile_changes_account",
                table: "consumer_account_profile_changes",
                column: "ConsumerAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumer_account_profile_changes");
        }
    }
}
