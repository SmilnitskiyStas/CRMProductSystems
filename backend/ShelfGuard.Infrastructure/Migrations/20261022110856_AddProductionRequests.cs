using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "NeededBy",
                table: "production_orders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PlannedDate",
                table: "production_orders",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "production_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    NeededBy = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "draft"),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "manual"),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ReviewNote = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ShipmentTransferId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShippedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_production_requests_locations_ProductionLocationId",
                        column: x => x.ProductionLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_requests_locations_StoreLocationId",
                        column: x => x.StoreLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_requests_stock_transfers_ShipmentTransferId",
                        column: x => x.ShipmentTransferId,
                        principalTable: "stock_transfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_production_requests_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "production_request_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedQty = table.Column<decimal>(type: "numeric(10,3)", nullable: false),
                    AcceptedQty = table.Column<decimal>(type: "numeric(10,3)", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_request_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_production_request_lines_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_request_lines_production_requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "production_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "production_order_request_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Qty = table.Column<decimal>(type: "numeric(10,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_order_request_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_production_order_request_lines_production_orders_Production~",
                        column: x => x.ProductionOrderId,
                        principalTable: "production_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_production_order_request_lines_production_request_lines_Req~",
                        column: x => x.RequestLineId,
                        principalTable: "production_request_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_production_order_request_lines_line",
                table: "production_order_request_lines",
                column: "RequestLineId");

            migrationBuilder.CreateIndex(
                name: "idx_production_order_request_lines_tenant",
                table: "production_order_request_lines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "ux_production_order_request_lines_pair",
                table: "production_order_request_lines",
                columns: new[] { "ProductionOrderId", "RequestLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_production_request_lines_request",
                table: "production_request_lines",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "idx_production_request_lines_tenant",
                table: "production_request_lines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_production_request_lines_ItemId",
                table: "production_request_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "idx_production_requests_prod_status",
                table: "production_requests",
                columns: new[] { "ProductionLocationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "idx_production_requests_store_status",
                table: "production_requests",
                columns: new[] { "StoreLocationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "idx_production_requests_tenant",
                table: "production_requests",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_production_requests_ShipmentTransferId",
                table: "production_requests",
                column: "ShipmentTransferId");

            // RLS. All three tables: tenant_isolation + provider_bypass + worker_bypass (NULLIF guard).
            // production_requests additionally gets a RESTRICTIVE store_scope: visible to users assigned
            // to the requesting store OR the production location (so production staff see incoming requests).
            foreach (var table in new[] { "production_requests", "production_request_lines", "production_order_request_lines" })
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
                ");
            }

            migrationBuilder.Sql(@"
                CREATE POLICY store_scope ON production_requests AS RESTRICTIVE
                  USING (
                    current_setting('app.role', true) IN ('provider', 'provider_admin', 'worker', 'enterprise_admin')
                    OR EXISTS (
                         SELECT 1 FROM user_locations ul
                         WHERE ul.""UserId"" = NULLIF(current_setting('app.user_id', true), '')::uuid
                           AND ul.""LocationId"" IN (production_requests.""StoreLocationId"", production_requests.""ProductionLocationId"")
                       )
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "production_order_request_lines");

            migrationBuilder.DropTable(
                name: "production_request_lines");

            migrationBuilder.DropTable(
                name: "production_requests");

            migrationBuilder.DropColumn(
                name: "NeededBy",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "PlannedDate",
                table: "production_orders");
        }
    }
}
