using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// TASK-613 (customer/loyalty domain expansion, approved plan `goofy-bubbling-naur.md`).
    /// Loyalty tier ladder: <c>loyalty_tier_definitions</c> (per-tenant rungs — name,
    /// threshold, accrual multiplier, discount%) + <c>LoyaltyMembership.CurrentTierId</c>/
    /// <c>CompositeScore</c>/<c>TierScoreUpdatedAt</c> (set only by the nightly
    /// tier-recompute worker job, never at request time) + append-only
    /// <c>loyalty_tier_change_history</c> for progression audit.
    ///
    /// <c>loyalty_tier_definitions</c> gets the canonical fail-closed triad only (staff
    /// config, no consumer read path — same posture as <c>loyalty_program_settings</c>).
    /// <c>loyalty_tier_change_history</c> gets the same triad plus <c>consumer_self_access</c>
    /// (EXISTS through the owning membership, same shape as <c>loyalty_ledger_entries</c>'
    /// own policy from AddLoyaltyProgram) — a consumer must be able to see their own tier
    /// progression.
    /// </summary>
    public partial class AddLoyaltyTierLadder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CompositeScore",
                table: "loyalty_memberships",
                type: "numeric(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentTierId",
                table: "loyalty_memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TierScoreUpdatedAt",
                table: "loyalty_memberships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "loyalty_tier_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    MinCompositeScore = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    AccrualMultiplier = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 1.0m),
                    DiscountPercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loyalty_tier_definitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_loyalty_tier_definitions_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "loyalty_tier_change_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromTierId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToTierId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromScore = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ToScore = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loyalty_tier_change_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_loyalty_tier_change_history_loyalty_memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "loyalty_memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_loyalty_tier_change_history_loyalty_tier_definitions_FromTi~",
                        column: x => x.FromTierId,
                        principalTable: "loyalty_tier_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_loyalty_tier_change_history_loyalty_tier_definitions_ToTier~",
                        column: x => x.ToTierId,
                        principalTable: "loyalty_tier_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_memberships_CurrentTierId",
                table: "loyalty_memberships",
                column: "CurrentTierId");

            migrationBuilder.CreateIndex(
                name: "idx_loyalty_tier_change_history_membership_changed",
                table: "loyalty_tier_change_history",
                columns: new[] { "TenantId", "MembershipId", "ChangedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_tier_change_history_FromTierId",
                table: "loyalty_tier_change_history",
                column: "FromTierId");

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_tier_change_history_MembershipId",
                table: "loyalty_tier_change_history",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_tier_change_history_ToTierId",
                table: "loyalty_tier_change_history",
                column: "ToTierId");

            migrationBuilder.CreateIndex(
                name: "uq_loyalty_tier_definitions_tenant_sort_order",
                table: "loyalty_tier_definitions",
                columns: new[] { "TenantId", "SortOrder" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_loyalty_memberships_loyalty_tier_definitions_CurrentTierId",
                table: "loyalty_memberships",
                column: "CurrentTierId",
                principalTable: "loyalty_tier_definitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── RLS: loyalty_tier_definitions ────────────────────────────────
            // Canonical triad only — staff config, no consumer_self_access (matches
            // loyalty_program_settings' posture).
            migrationBuilder.Sql(@"
                ALTER TABLE loyalty_tier_definitions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE loyalty_tier_definitions FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON loyalty_tier_definitions
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON loyalty_tier_definitions
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON loyalty_tier_definitions
                  USING (current_setting('app.role', true) = 'worker');
            ");

            // ── RLS: loyalty_tier_change_history ─────────────────────────────
            // Canonical triad (worker_bypass required — the nightly tier-recompute job
            // writes here) + consumer_self_access via EXISTS through the owning membership,
            // same shape as loyalty_ledger_entries' own policy (AddLoyaltyProgram).
            migrationBuilder.Sql(@"
                ALTER TABLE loyalty_tier_change_history ENABLE ROW LEVEL SECURITY;
                ALTER TABLE loyalty_tier_change_history FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON loyalty_tier_change_history
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON loyalty_tier_change_history
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON loyalty_tier_change_history
                  USING (current_setting('app.role', true) = 'worker');
                CREATE POLICY consumer_self_access ON loyalty_tier_change_history
                  USING (
                    EXISTS (
                      SELECT 1 FROM loyalty_memberships m
                      WHERE m.""Id"" = loyalty_tier_change_history.""MembershipId""
                        AND m.""ConsumerAccountId"" = (NULLIF(current_setting('app.consumer_account_id', true), ''))::uuid
                    )
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS consumer_self_access ON loyalty_tier_change_history;
                DROP POLICY IF EXISTS worker_bypass ON loyalty_tier_change_history;
                DROP POLICY IF EXISTS provider_bypass ON loyalty_tier_change_history;
                DROP POLICY IF EXISTS tenant_isolation ON loyalty_tier_change_history;
                ALTER TABLE loyalty_tier_change_history DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS worker_bypass ON loyalty_tier_definitions;
                DROP POLICY IF EXISTS provider_bypass ON loyalty_tier_definitions;
                DROP POLICY IF EXISTS tenant_isolation ON loyalty_tier_definitions;
                ALTER TABLE loyalty_tier_definitions DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_loyalty_memberships_loyalty_tier_definitions_CurrentTierId",
                table: "loyalty_memberships");

            migrationBuilder.DropTable(
                name: "loyalty_tier_change_history");

            migrationBuilder.DropTable(
                name: "loyalty_tier_definitions");

            migrationBuilder.DropIndex(
                name: "IX_loyalty_memberships_CurrentTierId",
                table: "loyalty_memberships");

            migrationBuilder.DropColumn(
                name: "CompositeScore",
                table: "loyalty_memberships");

            migrationBuilder.DropColumn(
                name: "CurrentTierId",
                table: "loyalty_memberships");

            migrationBuilder.DropColumn(
                name: "TierScoreUpdatedAt",
                table: "loyalty_memberships");
        }
    }
}
