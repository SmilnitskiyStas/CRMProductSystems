using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// B1 (giggly-catmull plan, Частина B): replace the per-tenant <c>categories</c> table with a
    /// single global, provider-curated <c>platform_categories</c> table — no <c>TenantId</c>, no RLS.
    /// Every tenant now draws its category list from this one table (B2 adds the
    /// <c>Tenant.BusinessType</c> filter + provider CRUD).
    ///
    /// <para><b>Up() ordering matters</b> — data is preserved by name:</para>
    /// <list type="number">
    ///   <item>create <c>platform_categories</c> (+ self-FK + indexes, no ENABLE ROW LEVEL SECURITY);</item>
    ///   <item>lift FORCE ROW LEVEL SECURITY on <c>categories</c>/<c>items</c>/<c>product_segments</c>/
    ///         <c>weather_coefficients</c> — migrations run as the table-owning NOBYPASSRLS app role
    ///         with no <c>app.tenant_id</c>, so the data steps would otherwise see zero rows
    ///         (restored in step 8);</item>
    ///   <item>seed it from the union of every tenant's <c>categories.Name</c>, tagged with the
    ///         business type(s) of the tenants that used each name (names differing only by
    ///         case/whitespace collapse to one row);</item>
    ///   <item>drop the old FKs to <c>categories</c> (before the repoint — the repoint writes ids
    ///         the old FK would reject);</item>
    ///   <item>repoint <c>items."CategoryId"</c>, <c>product_segments."CategoryId"</c> and
    ///         <c>weather_coefficients."CategoryId"</c> by trimmed case-insensitive name match;</item>
    ///   <item>add new FKs to <c>platform_categories</c> (all <c>ON DELETE SET NULL</c> —
    ///         weather_coefficients was CASCADE, now SET NULL so a provider soft-deleting a global
    ///         category never cascade-deletes a tenant's rows);</item>
    ///   <item>drop <c>categories</c> (its RLS policies + <c>idx_categories_tenant_parent_active</c>
    ///         drop with it);</item>
    ///   <item>restore FORCE ROW LEVEL SECURITY on the three surviving tables.</item>
    /// </list>
    ///
    /// <para><b>Down()</b> recreates the <c>categories</c> table + its RLS triad + the FK swap-back,
    /// but does <b>not</b> restore row data — documented irreversible, same as
    /// <c>MigrateOrphanSuppliersToTenants</c>.</para>
    /// </summary>
    public partial class AddPlatformCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. New global table ─────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "platform_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    BusinessTypes = table.Column<List<string>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_platform_categories_platform_categories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "platform_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_platform_categories_active_sort",
                table: "platform_categories",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "idx_platform_categories_parent_active",
                table: "platform_categories",
                columns: new[] { "ParentId", "IsActive" });

            // NOTE: intentionally NO "ALTER TABLE platform_categories ENABLE ROW LEVEL SECURITY" —
            // this is global reference data. Reads are open to every authenticated tenant; writes
            // go only through the provider-only endpoints added in B2.

            // ── 2. Lift FORCE RLS for the data window ──────────────────────────
            // Migrations run as the table-owning app role (NOSUPERUSER NOBYPASSRLS, KI-027) with
            // no app.tenant_id / app.role set. `categories`, `items`, `product_segments` and
            // `weather_coefficients` all carry FORCE ROW LEVEL SECURITY (ForceRlsOnAllTenantTables),
            // so without this the seed SELECT and the repoint UPDATE below would see zero rows and
            // the new FK constraints would "validate" against nothing. ENABLE stays on; only the
            // owner-also-applies FORCE flag is toggled, entirely inside this migration's
            // transaction, and restored in step 7. (Under a superuser connection this is a
            // harmless no-op that still ends with FORCE on.)
            migrationBuilder.Sql(@"
                ALTER TABLE categories            NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE items                 NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE product_segments      NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE weather_coefficients  NO FORCE ROW LEVEL SECURITY;
            ");

            // ── 3. Seed: union of existing categories.Name across all tenants ────
            // BusinessTypes = the distinct BusinessType(s) of the tenants that used each name.
            // Names differing only by case/whitespace collapse via the lower(btrim()) group key;
            // the surviving "Name" is arbitrary among them (provider curates later). Flat tree.
            migrationBuilder.Sql(@"
                INSERT INTO platform_categories (""Id"", ""Name"", ""BusinessTypes"", ""SortOrder"", ""IsActive"", ""CreatedAt"")
                SELECT gen_random_uuid(),
                       c.""Name"",
                       COALESCE(jsonb_agg(DISTINCT t.""BusinessType"") FILTER (WHERE t.""BusinessType"" IS NOT NULL), '[]'::jsonb),
                       0, true, now()
                FROM categories c
                JOIN tenants t ON t.""Id"" = c.""TenantId""
                GROUP BY lower(btrim(c.""Name"")), c.""Name"";
            ");

            // ── 4. Drop the old FKs → categories ───────────────────────────────
            // Must precede the repoint: the repoint sets "CategoryId" to platform_categories
            // ids that do not exist in `categories`, which the old FK would reject.
            migrationBuilder.DropForeignKey(
                name: "FK_items_categories_CategoryId",
                table: "items");

            migrationBuilder.DropForeignKey(
                name: "FK_product_segments_categories_CategoryId",
                table: "product_segments");

            migrationBuilder.DropForeignKey(
                name: "FK_weather_coefficients_categories_CategoryId",
                table: "weather_coefficients");

            // ── 5. Repoint the 3 FK columns by name (idempotent — after this runs, the
            //       WHERE no longer matches any row because the ids now point at
            //       platform_categories, not categories). ─────────────────────────
            migrationBuilder.Sql(@"
                UPDATE items i
                   SET ""CategoryId"" = pc.""Id""
                  FROM categories c
                  JOIN platform_categories pc ON lower(btrim(pc.""Name"")) = lower(btrim(c.""Name""))
                 WHERE i.""CategoryId"" = c.""Id"";

                UPDATE product_segments s
                   SET ""CategoryId"" = pc.""Id""
                  FROM categories c
                  JOIN platform_categories pc ON lower(btrim(pc.""Name"")) = lower(btrim(c.""Name""))
                 WHERE s.""CategoryId"" = c.""Id"";

                UPDATE weather_coefficients w
                   SET ""CategoryId"" = pc.""Id""
                  FROM categories c
                  JOIN platform_categories pc ON lower(btrim(pc.""Name"")) = lower(btrim(c.""Name""))
                 WHERE w.""CategoryId"" = c.""Id"";
            ");

            // ── 6. Add new FKs → platform_categories (data already repointed and RLS FORCE
            //       lifted, so the constraint validation runs against real rows). All SET NULL. ─
            migrationBuilder.AddForeignKey(
                name: "FK_items_platform_categories_CategoryId",
                table: "items",
                column: "CategoryId",
                principalTable: "platform_categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_product_segments_platform_categories_CategoryId",
                table: "product_segments",
                column: "CategoryId",
                principalTable: "platform_categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_weather_coefficients_platform_categories_CategoryId",
                table: "weather_coefficients",
                column: "CategoryId",
                principalTable: "platform_categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── 7. Drop the old table (RLS policies + idx_categories_tenant_parent_active
            //       drop with it). ─────────────────────────────────────────────
            migrationBuilder.DropTable(
                name: "categories");

            // ── 8. Restore FORCE ROW LEVEL SECURITY on the tables that survive. ──
            migrationBuilder.Sql(@"
                ALTER TABLE items                 FORCE ROW LEVEL SECURITY;
                ALTER TABLE product_segments      FORCE ROW LEVEL SECURITY;
                ALTER TABLE weather_coefficients  FORCE ROW LEVEL SECURITY;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible for row data (matching MigrateOrphanSuppliersToTenants.Down): this
            // recreates the categories table structure + RLS triad + FK swap-back only. Any
            // items/product_segments/weather_coefficients."CategoryId" values are left pointing
            // at the now-dropped platform_categories rows and will fail the re-added FK's
            // validation unless first NULLed manually — acceptable for a Down that is documented
            // never to run against real data.
            migrationBuilder.DropForeignKey(
                name: "FK_items_platform_categories_CategoryId",
                table: "items");

            migrationBuilder.DropForeignKey(
                name: "FK_product_segments_platform_categories_CategoryId",
                table: "product_segments");

            migrationBuilder.DropForeignKey(
                name: "FK_weather_coefficients_platform_categories_CategoryId",
                table: "weather_coefficients");

            migrationBuilder.DropTable(
                name: "platform_categories");

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_categories_categories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_categories_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_categories_tenant_parent_active",
                table: "categories",
                columns: new[] { "TenantId", "ParentId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_categories_ParentId",
                table: "categories",
                column: "ParentId");

            // RLS triad — verbatim (adapted table name) from the live categories policies
            // (FullSchema + FixAllRlsPoliciesNullIfEmptyString + AddWorkerBypassRlsPolicy +
            // ExpandProviderBypassToProviderAdmin + FixFailOpenTenantIsolationOnReset), same
            // shape as AddSupplierMetricsHistory.
            migrationBuilder.Sql(@"
                ALTER TABLE categories ENABLE ROW LEVEL SECURITY;
                ALTER TABLE categories FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON categories
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON categories
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON categories
                  USING (current_setting('app.role', true) = 'worker');
            ");

            migrationBuilder.AddForeignKey(
                name: "FK_items_categories_CategoryId",
                table: "items",
                column: "CategoryId",
                principalTable: "categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_product_segments_categories_CategoryId",
                table: "product_segments",
                column: "CategoryId",
                principalTable: "categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_weather_coefficients_categories_CategoryId",
                table: "weather_coefficients",
                column: "CategoryId",
                principalTable: "categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
