using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2DataFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    QuantitySold = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    QuantityEndOfDay = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    IsPromoDay = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsAnomaly = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "manual"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_sales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_daily_sales_catalog_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "catalog_products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_daily_sales_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_adu",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Adu30d = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    Adu60d = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    Adu90d = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    AduEffective = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    ProductGroup = table.Column<short>(type: "smallint", nullable: true),
                    ValidDays30d = table.Column<int>(type: "integer", nullable: true),
                    ValidDays60d = table.Column<int>(type: "integer", nullable: true),
                    CalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_adu", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_adu_catalog_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "catalog_products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_product_adu_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supply_schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<List<int>>(type: "integer[]", nullable: false),
                    OrderLeadDays = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supply_schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supply_schedules_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supply_schedules_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_daily_sales_ProductId",
                table: "daily_sales",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_daily_sales_StoreId_ProductId_Date",
                table: "daily_sales",
                columns: new[] { "StoreId", "ProductId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_daily_sales_TenantId_Date",
                table: "daily_sales",
                columns: new[] { "TenantId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_product_adu_ProductId",
                table: "product_adu",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_product_adu_StoreId_ProductId",
                table: "product_adu",
                columns: new[] { "StoreId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supply_schedules_StoreId_SupplierId",
                table: "supply_schedules",
                columns: new[] { "StoreId", "SupplierId" });

            migrationBuilder.CreateIndex(
                name: "IX_supply_schedules_SupplierId",
                table: "supply_schedules",
                column: "SupplierId");

            // RLS — mandatory on every tenant table (tenant_isolation + provider_bypass).
            // current_setting('app.tenant_id', true) is set by TenantConnectionInterceptor.
            foreach (var tbl in new[] { "daily_sales", "product_adu", "supply_schedules" })
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {tbl} ENABLE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON {tbl}
                      USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                    CREATE POLICY provider_bypass ON {tbl}
                      USING (current_setting('app.role', true) = 'provider');
                ");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Policies drop automatically with their tables.
            migrationBuilder.DropTable(
                name: "daily_sales");

            migrationBuilder.DropTable(
                name: "product_adu");

            migrationBuilder.DropTable(
                name: "supply_schedules");
        }
    }
}
