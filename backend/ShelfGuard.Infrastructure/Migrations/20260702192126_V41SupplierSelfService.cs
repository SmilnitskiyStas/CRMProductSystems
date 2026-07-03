using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V41SupplierSelfService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOwnerManaged",
                table: "supplier_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // NOTE: PerishabilityClass AddColumn scaffolded here was removed manually —
            // the column was already created by the hand-written migration
            // 20260627120000_AddItemPerishabilityClass (which never updated the snapshot).
            // This migration only syncs the model snapshot; no DB change needed for items.

            migrationBuilder.CreateIndex(
                name: "UX_supplier_profiles_owner_tenant",
                table: "supplier_profiles",
                column: "TenantId",
                unique: true,
                filter: "\"IsOwnerManaged\"");

            // ── RLS hardening for supplier* tables (ADR-016 / TASK-282) ────────
            // On databases where V4SupplierMarketplace was applied AFTER
            // ForceRlsOnAllTenantTables + FixAllRlsPoliciesNullIfEmptyString
            // (out-of-order apply), the supplier* policies miss the NULLIF
            // empty-string guard and FORCE RLS. Re-apply both idempotently —
            // same pattern as FixAllRlsPoliciesNullIfEmptyString (Group 1).
            migrationBuilder.Sql(@"
DO $supfix$
DECLARE t text;
BEGIN
  FOR t IN
    SELECT DISTINCT tablename FROM pg_policies
    WHERE policyname = 'tenant_isolation'
      AND tablename IN ('suppliers','supplier_profiles','supplier_items',
                        'supplier_metrics','supplier_reviews')
  LOOP
    EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', t);
    EXECUTE format(
      'ALTER POLICY tenant_isolation ON %I USING (
         NULLIF(current_setting(''app.tenant_id'', true), '''') IS NULL
         OR ""TenantId"" = (NULLIF(current_setting(''app.tenant_id'', true), ''''))::uuid
       )', t);
  END LOOP;
END $supfix$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_supplier_profiles_owner_tenant",
                table: "supplier_profiles");

            migrationBuilder.DropColumn(
                name: "IsOwnerManaged",
                table: "supplier_profiles");
            // PerishabilityClass intentionally NOT dropped here — owned by
            // 20260627120000_AddItemPerishabilityClass (see Up note).
            // RLS hardening (NULLIF guard + FORCE RLS) is intentionally not
            // reverted — purely additive safety fix (same as FixAllRlsPolicies).
        }
    }
}
