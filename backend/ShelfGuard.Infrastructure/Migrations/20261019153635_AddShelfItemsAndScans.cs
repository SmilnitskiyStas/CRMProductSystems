using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShelfItemsAndScans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shelf_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShelfId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedFacings = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shelf_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shelf_items_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_shelf_items_location_zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "location_zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shelf_scans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShelfId = table.Column<Guid>(type: "uuid", nullable: true),
                    ImagePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "completed"),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    MissingCount = table.Column<int>(type: "integer", nullable: false),
                    LowCount = table.Column<int>(type: "integer", nullable: false),
                    AlertPending = table.Column<bool>(type: "boolean", nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TokensUsed = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shelf_scans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shelf_scans_location_zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "location_zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_shelf_items_tenant_zone",
                table: "shelf_items",
                columns: new[] { "TenantId", "ZoneId" });

            migrationBuilder.CreateIndex(
                name: "IX_shelf_items_ItemId",
                table: "shelf_items",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_shelf_items_ZoneId_ShelfId_ItemId",
                table: "shelf_items",
                columns: new[] { "ZoneId", "ShelfId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_shelf_scans_alert_pending",
                table: "shelf_scans",
                column: "AlertPending",
                filter: "\"AlertPending\" = true");

            migrationBuilder.CreateIndex(
                name: "idx_shelf_scans_tenant_zone_created",
                table: "shelf_scans",
                columns: new[] { "TenantId", "ZoneId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_shelf_scans_ZoneId",
                table: "shelf_scans",
                column: "ZoneId");

            // ── Data migration: planogram rows from the legacy location_zones."Position" JSON ──
            // Each section carries at most one `itemId`. Runs BEFORE RLS is enabled on the new
            // tables and with the provider role set (transaction-local) so the source tables'
            // own RLS cannot hide rows from the migration session. Dangling itemIds (item deleted
            // or never existed), malformed uuids and non-array `items` are skipped.
            migrationBuilder.Sql(@"
                SELECT set_config('app.role', 'provider', true);

                INSERT INTO shelf_items (""Id"", ""TenantId"", ""LocationId"", ""ZoneId"", ""ShelfId"", ""ItemId"", ""ExpectedFacings"", ""CreatedAt"")
                SELECT DISTINCT ON (q.zone_id, q.shelf_id, q.item_id)
                       gen_random_uuid(), q.tenant_id, q.location_id, q.zone_id, q.shelf_id, q.item_id, 1, NOW()
                FROM (
                    SELECT z.""Id"" AS zone_id, l.""Id"" AS location_id, l.""TenantId"" AS tenant_id,
                           CASE WHEN s.sec->>'shelfId' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                                THEN (s.sec->>'shelfId')::uuid END AS shelf_id,
                           CASE WHEN s.sec->>'itemId' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                                THEN (s.sec->>'itemId')::uuid END AS item_id
                    FROM location_zones z
                    JOIN locations l ON l.""Id"" = z.""LocationId""
                    CROSS JOIN LATERAL jsonb_array_elements(
                        CASE WHEN jsonb_typeof(z.""Position""->'items') = 'array' THEN z.""Position""->'items' ELSE '[]'::jsonb END
                    ) AS s(sec)
                    WHERE jsonb_typeof(z.""Position"") = 'object'
                ) q
                JOIN items i ON i.""Id"" = q.item_id AND i.""TenantId"" = q.tenant_id
                WHERE q.shelf_id IS NOT NULL AND q.item_id IS NOT NULL
                ON CONFLICT DO NOTHING;

                INSERT INTO item_zone_assignments (""Id"", ""TenantId"", ""ItemId"", ""ZoneId"", ""CreatedAt"")
                SELECT DISTINCT ON (si.""ItemId"", si.""ZoneId"")
                       gen_random_uuid(), si.""TenantId"", si.""ItemId"", si.""ZoneId"", NOW()
                FROM shelf_items si
                ON CONFLICT DO NOTHING;
            ");

            // ── RLS: tenant_isolation + provider/worker bypass + RESTRICTIVE store_scope ──
            // Both tables carry LocationId, so they follow the one-sided store_scope shape of
            // 20260719193545_AddLocationStoreScopeRlsPolicies (bypass roles OR user_locations match).
            foreach (var table in new[] { "shelf_items", "shelf_scans" })
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON {table}
                        USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                    CREATE POLICY provider_bypass ON {table}
                        USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                    CREATE POLICY worker_bypass ON {table}
                        USING (current_setting('app.role', true) = 'worker');
                    CREATE POLICY store_scope ON {table} AS RESTRICTIVE
                      USING (
                        current_setting('app.role', true) IN ('provider', 'provider_admin', 'worker', 'enterprise_admin')
                        OR EXISTS (
                             SELECT 1 FROM user_locations ul
                             WHERE ul.""UserId"" = NULLIF(current_setting('app.user_id', true), '')::uuid
                               AND ul.""LocationId"" = {table}.""LocationId""
                           )
                      );
                ");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shelf_items");

            migrationBuilder.DropTable(
                name: "shelf_scans");
        }
    }
}
