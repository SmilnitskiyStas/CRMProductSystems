using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V4AutoServiceSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "as_customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_as_customers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_as_customers_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "as_service_catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultPrice = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    DurationHours = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_as_service_catalog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_as_service_catalog_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_as_service_catalog_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "as_vehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Brand = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: false),
                    Year = table.Column<short>(type: "smallint", nullable: true),
                    Vin = table.Column<string>(type: "text", nullable: true),
                    LicensePlate = table.Column<string>(type: "text", nullable: true),
                    Mileage = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_as_vehicles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_as_vehicles_as_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "as_customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_as_vehicles_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "as_work_orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    MechanicUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "New"),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_as_work_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_as_work_orders_as_vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "as_vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_as_work_orders_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_as_work_orders_users_MechanicUserId",
                        column: x => x.MechanicUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "as_work_order_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ServiceCatalogId = table.Column<Guid>(type: "uuid", nullable: true),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Qty = table.Column<decimal>(type: "numeric(10,3)", nullable: false, defaultValue: 1m),
                    Price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 0m)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_as_work_order_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_as_work_order_lines_as_service_catalog_ServiceCatalogId",
                        column: x => x.ServiceCatalogId,
                        principalTable: "as_service_catalog",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_as_work_order_lines_as_work_orders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "as_work_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_as_work_order_lines_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_as_customers_TenantId",
                table: "as_customers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_as_service_catalog_ItemId",
                table: "as_service_catalog",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_as_service_catalog_TenantId",
                table: "as_service_catalog",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_as_vehicles_CustomerId",
                table: "as_vehicles",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_as_vehicles_TenantId",
                table: "as_vehicles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_as_work_order_lines_ItemId",
                table: "as_work_order_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_as_work_order_lines_ServiceCatalogId",
                table: "as_work_order_lines",
                column: "ServiceCatalogId");

            migrationBuilder.CreateIndex(
                name: "IX_as_work_order_lines_WorkOrderId",
                table: "as_work_order_lines",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_as_work_orders_MechanicUserId",
                table: "as_work_orders",
                column: "MechanicUserId");

            migrationBuilder.CreateIndex(
                name: "IX_as_work_orders_Status",
                table: "as_work_orders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_as_work_orders_TenantId",
                table: "as_work_orders",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_as_work_orders_VehicleId",
                table: "as_work_orders",
                column: "VehicleId");

            // ── CHECK: as_work_orders — status values ────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE as_work_orders
                  ADD CONSTRAINT CK_as_work_orders_status
                  CHECK (""Status"" IN ('New','InProgress','WaitingParts','Done','Invoiced'));
            ");

            // ── CHECK: as_work_order_lines — type values + integrity ─────────
            migrationBuilder.Sql(@"
                ALTER TABLE as_work_order_lines
                  ADD CONSTRAINT CK_as_work_order_lines_type
                  CHECK (""Type"" IN ('service','part'));
                ALTER TABLE as_work_order_lines
                  ADD CONSTRAINT CK_as_work_order_lines_ref
                  CHECK (
                    (""Type"" = 'service' AND ""ServiceCatalogId"" IS NOT NULL) OR
                    (""Type"" = 'part'    AND ""ItemId""           IS NOT NULL)
                  );
            ");

            // ── RLS: as_customers ────────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE as_customers ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON as_customers
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON as_customers
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: as_vehicles ─────────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE as_vehicles ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON as_vehicles
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON as_vehicles
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: as_service_catalog ──────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE as_service_catalog ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON as_service_catalog
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON as_service_catalog
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: as_work_orders ──────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE as_work_orders ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON as_work_orders
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON as_work_orders
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: as_work_order_lines ─────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE as_work_order_lines ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON as_work_order_lines
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON as_work_order_lines
                  USING (current_setting('app.role', true) = 'provider');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "as_work_order_lines");

            migrationBuilder.DropTable(
                name: "as_service_catalog");

            migrationBuilder.DropTable(
                name: "as_work_orders");

            migrationBuilder.DropTable(
                name: "as_vehicles");

            migrationBuilder.DropTable(
                name: "as_customers");
        }
    }
}
