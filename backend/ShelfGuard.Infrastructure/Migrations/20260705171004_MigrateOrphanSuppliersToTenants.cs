using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MigrateOrphanSuppliersToTenants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data migration (calm-singing-marble plan, Part 2): suppliers created via the
            // legacy "Маркетплейс/Постачальники" path (MarketplaceAdminController.CreateSupplier)
            // were attached to the system "platform-marketplace" tenant, which is intentionally
            // IsActive = false and has no users — the supplier cabinet is unreachable for them.
            // This migration promotes each such orphaned supplier to its own real, active tenant
            // (BusinessType = 'supplier', Modules = ["marketplace_supplier"]) so the provider can
            // add a manager user via the existing AddTenantUserModal flow.
            //
            // Idempotent: guarded on the platform-marketplace tenant existing and having any
            // suppliers left pointing at it. Safe to re-run — once suppliers are re-pointed to
            // their own tenants, the WHERE clause matches zero rows.
            migrationBuilder.Sql(@"
DO $mig$
DECLARE
    platform_tenant_id uuid;
    sup RECORD;
    base_slug text;
    candidate_slug text;
    suffix int;
    new_tenant_id uuid;
BEGIN
    SELECT ""Id"" INTO platform_tenant_id FROM tenants WHERE ""Slug"" = 'platform-marketplace';

    IF platform_tenant_id IS NULL THEN
        RETURN; -- nothing to migrate, system tenant was never created / already removed
    END IF;

    FOR sup IN
        SELECT ""Id"", ""Name"" FROM suppliers WHERE ""TenantId"" = platform_tenant_id
    LOOP
        -- Slugify: lowercase, transliterate common Cyrillic letters to Latin,
        -- strip anything that isn't a-z0-9, collapse separators to single '-'.
        -- Multi-char digraphs first (ones with >1 Latin letter output), then
        -- single-char 1:1 translate() map for the rest.
        base_slug := lower(sup.""Name"");
        base_slug := replace(base_slug, 'щ', 'shch');
        base_slug := replace(base_slug, 'ю', 'yu');
        base_slug := replace(base_slug, 'я', 'ya');
        base_slug := replace(base_slug, 'є', 'ye');
        base_slug := replace(base_slug, 'ї', 'yi');
        base_slug := replace(base_slug, 'ж', 'zh');
        base_slug := replace(base_slug, 'х', 'kh');
        base_slug := replace(base_slug, 'ц', 'ts');
        base_slug := replace(base_slug, 'ч', 'ch');
        base_slug := replace(base_slug, 'ш', 'sh');
        base_slug := translate(base_slug,
            'абвгдезийклмнопрстуфъьэ',
            'abvgdeziyklmnoprstuf__e');
        base_slug := regexp_replace(base_slug, '[^a-z0-9]+', '-', 'g');
        base_slug := trim(both '-' from base_slug);
        IF base_slug = '' OR base_slug IS NULL THEN
            base_slug := 'supplier';
        END IF;

        candidate_slug := base_slug;
        suffix := 1;
        WHILE EXISTS (SELECT 1 FROM tenants WHERE ""Slug"" = candidate_slug) LOOP
            suffix := suffix + 1;
            candidate_slug := base_slug || '-' || suffix::text;
        END LOOP;

        new_tenant_id := gen_random_uuid();

        INSERT INTO tenants (""Id"", ""Name"", ""Slug"", ""Plan"", ""Modules"", ""BusinessType"", ""IsActive"", ""CreatedAt"")
        VALUES (new_tenant_id, sup.""Name"", candidate_slug, 'basic', '[""marketplace_supplier""]', 'supplier', true, NOW());

        UPDATE suppliers SET ""TenantId"" = new_tenant_id WHERE ""Id"" = sup.""Id"";

        UPDATE supplier_profiles
           SET ""TenantId"" = new_tenant_id, ""IsOwnerManaged"" = true
         WHERE ""SupplierId"" = sup.""Id"";

        -- supplier_reviews.TenantId is the reviewing client's tenant, not the
        -- supplier owner's — intentionally left untouched.
    END LOOP;
END $mig$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible data migration: re-attaching suppliers back to the platform-marketplace
            // tenant would silently re-break their cabinet access. No automatic rollback provided.
        }
    }
}
