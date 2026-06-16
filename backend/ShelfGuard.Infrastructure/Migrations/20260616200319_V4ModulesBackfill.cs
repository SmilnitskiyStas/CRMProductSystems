using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V4ModulesBackfill : Migration
    {
        /// <summary>
        /// Data-only migration (KI-012): tenants created before TASK-208 still carry the
        /// legacy module vocabulary (shelf_manager/crm/notifications/...) instead of the
        /// v4 keys (inventory/procurement/pos/auto_service/production/marketplace). The
        /// new Sidebar (TASK-210) gates nav groups on the v4 keys, so any tenant without
        /// at least one v4 key would lose access to Operations/Sales/Procurement. This
        /// only touches tenants where NO v4 key is already present — idempotent, and safe
        /// to re-run. Mirrors Tenant.DefaultModulesForBusinessType exactly.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE tenants SET ""Modules"" = (CASE ""BusinessType""
                    WHEN 'retail'       THEN '[""inventory"",""procurement"",""pos""]'
                    WHEN 'auto_service' THEN '[""auto_service"",""procurement""]'
                    WHEN 'restaurant'   THEN '[""inventory"",""pos"",""production""]'
                    WHEN 'warehouse'    THEN '[""inventory"",""procurement""]'
                    WHEN 'production'   THEN '[""inventory"",""procurement"",""production""]'
                    WHEN 'distribution' THEN '[""inventory"",""procurement"",""marketplace""]'
                    ELSE '[""inventory""]'
                END)::jsonb
                WHERE NOT (
                    ""Modules""::text LIKE '%inventory%' OR ""Modules""::text LIKE '%procurement%' OR
                    ""Modules""::text LIKE '%pos%' OR ""Modules""::text LIKE '%auto_service%' OR
                    ""Modules""::text LIKE '%production%' OR ""Modules""::text LIKE '%marketplace%'
                );
            ");
        }

        /// <summary>
        /// No-op: this is a one-time data backfill. Reverting would mean guessing which
        /// rows this migration touched vs. already had v4 keys — not worth the complexity
        /// for a feature-flag field with no destructive consequence either way.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
