using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V4SupplierMarketplace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomName = table.Column<string>(type: "text", nullable: true),
                    Price = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    MinQty = table.Column<int>(type: "integer", nullable: true),
                    Unit = table.Column<string>(type: "text", nullable: true),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_items_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_supplier_items_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supplier_items_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_metrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AvgDeliveryDays = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    OrderAccuracy = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    QualityScore = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    Rating = table.Column<decimal>(type: "numeric(3,2)", nullable: true),
                    CancellationRate = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    ResponseTimeHours = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_metrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_metrics_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supplier_metrics_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Region = table.Column<string>(type: "text", nullable: true),
                    Categories = table.Column<string>(type: "jsonb", nullable: true),
                    Website = table.Column<string>(type: "text", nullable: true),
                    DeliveryRegions = table.Column<string>(type: "jsonb", nullable: true),
                    WorkingHours = table.Column<string>(type: "text", nullable: true),
                    PaymentTerms = table.Column<string>(type: "text", nullable: true),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Plan = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "free"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_profiles_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supplier_profiles_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<short>(type: "smallint", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_reviews_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supplier_reviews_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_items_ItemId",
                table: "supplier_items",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_items_SupplierId_TenantId",
                table: "supplier_items",
                columns: new[] { "SupplierId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_items_TenantId",
                table: "supplier_items",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_metrics_SupplierId",
                table: "supplier_metrics",
                column: "SupplierId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_metrics_TenantId",
                table: "supplier_metrics",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_profiles_SupplierId",
                table: "supplier_profiles",
                column: "SupplierId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_profiles_TenantId",
                table: "supplier_profiles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_reviews_SupplierId_TenantId",
                table: "supplier_reviews",
                columns: new[] { "SupplierId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_reviews_TenantId",
                table: "supplier_reviews",
                column: "TenantId");

            // ── CHECK: supplier_items — item_id OR custom_name must be set ──────
            migrationBuilder.Sql(@"
                ALTER TABLE supplier_items
                  ADD CONSTRAINT CK_supplier_items_item_or_name
                  CHECK (""ItemId"" IS NOT NULL OR ""CustomName"" IS NOT NULL);
            ");

            // ── CHECK: supplier_reviews — rating 1–5 ───────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE supplier_reviews
                  ADD CONSTRAINT CK_supplier_reviews_rating
                  CHECK (""Rating"" >= 1 AND ""Rating"" <= 5);
            ");

            // ── RLS: supplier_profiles ─────────────────────────────────────────
            // Public marketplace listing (GET /api/marketplace/suppliers) bypasses
            // RLS at the API level by using a provider-level DB connection context
            // (app.role = 'provider'), so standard tenant_isolation is sufficient here.
            // See: TASK-221 backend spec and task log 220_2026-06-17_...md for decision.
            migrationBuilder.Sql(@"
                ALTER TABLE supplier_profiles ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON supplier_profiles
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON supplier_profiles
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: supplier_items ────────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE supplier_items ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON supplier_items
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON supplier_items
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: supplier_metrics ──────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE supplier_metrics ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON supplier_metrics
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON supplier_metrics
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: supplier_reviews ──────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE supplier_reviews ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON supplier_reviews
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON supplier_reviews
                  USING (current_setting('app.role', true) = 'provider');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_items");

            migrationBuilder.DropTable(
                name: "supplier_metrics");

            migrationBuilder.DropTable(
                name: "supplier_profiles");

            migrationBuilder.DropTable(
                name: "supplier_reviews");
        }
    }
}
