using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoyaltyRewardsAndBonusLifetime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BonusLifetimeDays",
                table: "loyalty_program_settings",
                type: "integer",
                nullable: false,
                defaultValue: 365);

            migrationBuilder.AddColumn<bool>(
                name: "BonusLifetimeEnabled",
                table: "loyalty_program_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "FirstPurchaseRewardAmount",
                table: "loyalty_program_settings",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "FirstPurchaseRewardEnabled",
                table: "loyalty_program_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ProfileCompletionRewardAmount",
                table: "loyalty_program_settings",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "ProfileCompletionRewardEnabled",
                table: "loyalty_program_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ReviewRewardAmount",
                table: "loyalty_program_settings",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "ReviewRewardEnabled",
                table: "loyalty_program_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "WelcomeRewardAmount",
                table: "loyalty_program_settings",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "WelcomeRewardEnabled",
                table: "loyalty_program_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "loyalty_bonus_lots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    RemainingAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loyalty_bonus_lots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_loyalty_bonus_lots_loyalty_ledger_entries_SourceLedgerEntry~",
                        column: x => x.SourceLedgerEntryId,
                        principalTable: "loyalty_ledger_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_loyalty_bonus_lots_loyalty_memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "loyalty_memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_bonus_lots_MembershipId",
                table: "loyalty_bonus_lots",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_bonus_lots_SourceLedgerEntryId",
                table: "loyalty_bonus_lots",
                column: "SourceLedgerEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_bonus_lots_TenantId_MembershipId_ExpiresAt",
                table: "loyalty_bonus_lots",
                columns: new[] { "TenantId", "MembershipId", "ExpiresAt" });

            migrationBuilder.Sql(@"
                ALTER TABLE loyalty_bonus_lots ENABLE ROW LEVEL SECURITY;
                ALTER TABLE loyalty_bonus_lots FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON loyalty_bonus_lots
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON loyalty_bonus_lots
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON loyalty_bonus_lots
                  USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loyalty_bonus_lots");

            migrationBuilder.DropColumn(
                name: "BonusLifetimeDays",
                table: "loyalty_program_settings");

            migrationBuilder.DropColumn(
                name: "BonusLifetimeEnabled",
                table: "loyalty_program_settings");

            migrationBuilder.DropColumn(
                name: "FirstPurchaseRewardAmount",
                table: "loyalty_program_settings");

            migrationBuilder.DropColumn(
                name: "FirstPurchaseRewardEnabled",
                table: "loyalty_program_settings");

            migrationBuilder.DropColumn(
                name: "ProfileCompletionRewardAmount",
                table: "loyalty_program_settings");

            migrationBuilder.DropColumn(
                name: "ProfileCompletionRewardEnabled",
                table: "loyalty_program_settings");

            migrationBuilder.DropColumn(
                name: "ReviewRewardAmount",
                table: "loyalty_program_settings");

            migrationBuilder.DropColumn(
                name: "ReviewRewardEnabled",
                table: "loyalty_program_settings");

            migrationBuilder.DropColumn(
                name: "WelcomeRewardAmount",
                table: "loyalty_program_settings");

            migrationBuilder.DropColumn(
                name: "WelcomeRewardEnabled",
                table: "loyalty_program_settings");
        }
    }
}
