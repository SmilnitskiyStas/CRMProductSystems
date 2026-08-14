using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBannersSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "banners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Eyebrow = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Terms = table.Column<string>(type: "text", nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    Icon = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BackgroundColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AccentColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DetailMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "internal"),
                    ExternalUrl = table.Column<string>(type: "text", nullable: true),
                    ValidFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    ValidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_banners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_banners_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_banners_users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "banner_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BannerId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConsumerAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_banner_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_banner_events_banners_BannerId",
                        column: x => x.BannerId,
                        principalTable: "banners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_banner_events_consumer_accounts_ConsumerAccountId",
                        column: x => x.ConsumerAccountId,
                        principalTable: "consumer_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "banner_locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BannerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_banner_locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_banner_locations_banners_BannerId",
                        column: x => x.BannerId,
                        principalTable: "banners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_banner_locations_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "banner_products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BannerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_banner_products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_banner_products_banners_BannerId",
                        column: x => x.BannerId,
                        principalTable: "banners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_banner_products_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_banner_events_tenant_banner_type",
                table: "banner_events",
                columns: new[] { "TenantId", "BannerId", "EventType" });

            migrationBuilder.CreateIndex(
                name: "IX_banner_events_BannerId",
                table: "banner_events",
                column: "BannerId");

            migrationBuilder.CreateIndex(
                name: "IX_banner_events_ConsumerAccountId",
                table: "banner_events",
                column: "ConsumerAccountId");

            migrationBuilder.CreateIndex(
                name: "idx_banner_locations_tenant_location",
                table: "banner_locations",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_banner_locations_LocationId",
                table: "banner_locations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "uq_banner_locations_banner_location",
                table: "banner_locations",
                columns: new[] { "BannerId", "LocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_banner_products_tenant_banner",
                table: "banner_products",
                columns: new[] { "TenantId", "BannerId" });

            migrationBuilder.CreateIndex(
                name: "IX_banner_products_ItemId",
                table: "banner_products",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "uq_banner_products_banner_item",
                table: "banner_products",
                columns: new[] { "BannerId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_banners_tenant_active_sort",
                table: "banners",
                columns: new[] { "TenantId", "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_banners_CreatedBy",
                table: "banners",
                column: "CreatedBy");

            // ── RLS: banners ────────────────────────────────────────────────
            // Canonical triad. provider_bypass covers both 'provider'/'provider_admin' —
            // current convention since 20260714150000_ExpandProviderBypassToProviderAdmin
            // (see database-schema.md, not the stale single-value template at the top of
            // that doc). worker_bypass included from day one per the TASK-343 lesson —
            // no BullMQ job writes here today, but the next one that does must not
            // silently no-op the way pre-TASK-343 tables did.
            migrationBuilder.Sql(@"
                ALTER TABLE banners ENABLE ROW LEVEL SECURITY;
                ALTER TABLE banners FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON banners
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON banners
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON banners
                  USING (current_setting('app.role', true) = 'worker');
            ");

            // ── RLS: banner_locations ───────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE banner_locations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE banner_locations FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON banner_locations
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON banner_locations
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON banner_locations
                  USING (current_setting('app.role', true) = 'worker');
            ");

            // ── RLS: banner_products ────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE banner_products ENABLE ROW LEVEL SECURITY;
                ALTER TABLE banner_products FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON banner_products
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON banner_products
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON banner_products
                  USING (current_setting('app.role', true) = 'worker');
            ");

            // ── RLS: banner_events ──────────────────────────────────────────
            // Append-only log — same triad as the other three. No consumer_self_access:
            // consumer sessions never read this table directly (only write via the
            // POST view/click endpoints, which run under the tenant's own app.tenant_id
            // context set by the consumer-content controller, not app.consumer_account_id).
            migrationBuilder.Sql(@"
                ALTER TABLE banner_events ENABLE ROW LEVEL SECURITY;
                ALTER TABLE banner_events FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON banner_events
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON banner_events
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON banner_events
                  USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "banner_events");

            migrationBuilder.DropTable(
                name: "banner_locations");

            migrationBuilder.DropTable(
                name: "banner_products");

            migrationBuilder.DropTable(
                name: "banners");
        }
    }
}
