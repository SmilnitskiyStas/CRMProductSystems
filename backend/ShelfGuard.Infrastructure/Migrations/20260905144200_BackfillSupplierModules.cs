using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillSupplierModules : Migration
    {
        /// <summary>
        /// Data-only migration (TASK-693, Phase 7). The 2026-09-02 supplier-portal expansion
        /// shipped <c>supplier_inventory</c> and <c>supplier_workforce</c> provider-granted and
        /// default-OFF; the user has since reversed that decision — both modules are now part of
        /// the default set for every supplier tenant (see
        /// <c>Tenant.DefaultModulesForBusinessType("supplier")</c>). This merges the two keys into
        /// the <c>Modules</c> jsonb array of existing <c>business_type = 'supplier'</c> tenants
        /// that don't already have both. Idempotent and safe to re-run: <c>jsonb_agg(DISTINCT …)</c>
        /// dedupes and the WHERE guard skips rows already carrying both keys. Mirrors the style of
        /// 20260616200319_V4ModulesBackfill.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE tenants
                SET ""Modules"" = (
                    SELECT jsonb_agg(DISTINCT m)
                    FROM jsonb_array_elements_text(
                        ""Modules"" || '[""supplier_inventory"", ""supplier_workforce""]'::jsonb
                    ) AS m
                )
                WHERE ""BusinessType"" = 'supplier'
                  AND NOT (
                        ""Modules"" ? 'supplier_inventory'
                    AND ""Modules"" ? 'supplier_workforce'
                  );
            ");
        }

        /// <summary>
        /// No-op: one-time data backfill of a feature-flag field. Reverting would mean guessing
        /// which rows this migration touched vs. which already had the keys — no destructive
        /// consequence either way (same rationale as V4ModulesBackfill).
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
