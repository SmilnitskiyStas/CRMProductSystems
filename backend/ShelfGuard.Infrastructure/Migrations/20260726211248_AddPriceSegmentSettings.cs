using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// Фаза 2 marketing-analytics plan (`deep-cooking-nygaard.md` §"Фази 2-4", TASK-419) —
    /// one new tenant-settings table, direct analogy to <c>loyalty_program_settings</c>
    /// (TASK-404): <c>price_segment_settings</c> holds per-tenant configuration for price
    /// segments (quantile customer-spend tiers) and frequency decline/reactivation. Exactly
    /// one row per tenant (unique TenantId), staff-only — segments/audiences/customer
    /// metrics themselves are computed live from existing POS/customer data on every
    /// request, never persisted here or anywhere else.
    ///
    /// Gets the canonical fail-closed RLS triad only (tenant_isolation NULLIF-guarded +
    /// provider_bypass + worker_bypass) — deliberately NO identity-based
    /// <c>consumer_self_access</c> policy, unlike <c>loyalty_memberships</c> /
    /// <c>loyalty_ledger_entries</c> (TASK-404): this table has no consumer-facing read
    /// path at all, same posture as <c>loyalty_program_settings</c> itself (also
    /// staff/enterprise_admin-only configuration, canonical triad only).
    ///
    /// <c>provider_bypass</c> is written as <c>IN ('provider', 'provider_admin')</c> from
    /// day one, matching every tenant-scoped table created since
    /// 20260714150000_ExpandProviderBypassToProviderAdmin (confirmed still the live
    /// convention — TASK-404's own <c>AddLoyaltyProgram</c> made the identical call for
    /// its three tenant-scoped tables, for the same reason: provider_admin has identical
    /// effective permissions to provider per <c>ProviderPermissions.SystemRoleDefaults</c>,
    /// so a new table written with only 'provider' would just recreate the exact gap that
    /// migration had to retroactively fix elsewhere).
    /// </summary>
    public partial class AddPriceSegmentSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "price_segment_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefaultFrequencyDeclineThresholdPercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 30.0m),
                    MinReceiptsForBoundaries = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_segment_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_price_segment_settings_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_price_segment_settings_tenant",
                table: "price_segment_settings",
                column: "TenantId",
                unique: true);

            // ── RLS: price_segment_settings ──────────────────────────────────
            // Canonical triad only — no consumer_self_access (staff-only configuration,
            // see class remarks above).
            migrationBuilder.Sql(@"
                ALTER TABLE price_segment_settings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE price_segment_settings FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON price_segment_settings
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON price_segment_settings
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON price_segment_settings
                  USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "price_segment_settings");
        }
    }
}
