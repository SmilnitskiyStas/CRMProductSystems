using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V4LocationsRename : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ai_order_suggestions_stores_StoreId",
                table: "ai_order_suggestions");

            migrationBuilder.DropForeignKey(
                name: "FK_daily_sales_stores_StoreId",
                table: "daily_sales");

            migrationBuilder.DropForeignKey(
                name: "FK_demand_events_stores_StoreId",
                table: "demand_events");

            migrationBuilder.DropForeignKey(
                name: "FK_iot_devices_store_zones_ZoneId",
                table: "iot_devices");

            migrationBuilder.DropForeignKey(
                name: "FK_iot_devices_stores_StoreId",
                table: "iot_devices");

            migrationBuilder.DropForeignKey(
                name: "FK_pos_shifts_stores_StoreId",
                table: "pos_shifts");

            migrationBuilder.DropForeignKey(
                name: "FK_pos_transactions_stores_StoreId",
                table: "pos_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_product_adu_stores_StoreId",
                table: "product_adu");

            migrationBuilder.DropForeignKey(
                name: "FK_product_buffer_stores_StoreId",
                table: "product_buffer");

            migrationBuilder.DropForeignKey(
                name: "FK_product_stock_store_zones_ZoneId",
                table: "product_stock");

            migrationBuilder.DropForeignKey(
                name: "FK_product_stock_stores_StoreId",
                table: "product_stock");

            migrationBuilder.DropForeignKey(
                name: "FK_discounts_stores_StoreId",
                table: "discounts");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_movements_stores_FromStoreId",
                table: "stock_movements");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_movements_stores_ToStoreId",
                table: "stock_movements");

            migrationBuilder.DropForeignKey(
                name: "FK_store_zones_stores_StoreId",
                table: "store_zones");

            migrationBuilder.DropForeignKey(
                name: "FK_stores_tenants_TenantId",
                table: "stores");

            migrationBuilder.DropForeignKey(
                name: "FK_supply_schedules_stores_StoreId",
                table: "supply_schedules");

            migrationBuilder.DropForeignKey(
                name: "FK_weather_data_stores_StoreId",
                table: "weather_data");

            migrationBuilder.DropForeignKey(
                name: "FK_write_offs_stores_StoreId",
                table: "write_offs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_stores",
                table: "stores");

            migrationBuilder.DropPrimaryKey(
                name: "PK_store_zones",
                table: "store_zones");

            migrationBuilder.RenameTable(
                name: "stores",
                newName: "locations");

            migrationBuilder.RenameTable(
                name: "store_zones",
                newName: "location_zones");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                table: "write_offs",
                newName: "LocationId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                table: "weather_data",
                newName: "LocationId");

            migrationBuilder.RenameIndex(
                name: "IX_weather_data_StoreId_Date",
                table: "weather_data",
                newName: "IX_weather_data_LocationId_Date");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                table: "temperature_readings",
                newName: "LocationId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                table: "supply_schedules",
                newName: "LocationId");

            migrationBuilder.RenameIndex(
                name: "IX_supply_schedules_StoreId_SupplierId",
                table: "supply_schedules",
                newName: "IX_supply_schedules_LocationId_SupplierId");

            migrationBuilder.RenameColumn(
                name: "ToStoreId",
                table: "stock_transfers",
                newName: "ToLocationId");

            migrationBuilder.RenameColumn(
                name: "FromStoreId",
                table: "stock_transfers",
                newName: "FromLocationId");

            migrationBuilder.RenameColumn(
                name: "DestinationStoreId",
                table: "stock_receipts",
                newName: "DestinationLocationId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                table: "product_stock",
                newName: "LocationId");

            migrationBuilder.RenameIndex(
                name: "IX_product_stock_StoreId",
                table: "product_stock",
                newName: "IX_product_stock_LocationId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                table: "product_buffer",
                newName: "LocationId");

            migrationBuilder.RenameIndex(
                name: "IX_product_buffer_StoreId_ProductId",
                table: "product_buffer",
                newName: "IX_product_buffer_LocationId_ProductId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                table: "product_adu",
                newName: "LocationId");

            migrationBuilder.RenameIndex(
                name: "IX_product_adu_StoreId_ProductId",
                table: "product_adu",
                newName: "IX_product_adu_LocationId_ProductId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                table: "pos_transactions",
                newName: "LocationId");

            migrationBuilder.RenameIndex(
                name: "IX_pos_transactions_TenantId_StoreId_CreatedAt",
                table: "pos_transactions",
                newName: "IX_pos_transactions_TenantId_LocationId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_pos_transactions_StoreId",
                table: "pos_transactions",
                newName: "IX_pos_transactions_LocationId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                table: "pos_shifts",
                newName: "LocationId");

            migrationBuilder.RenameIndex(
                name: "IX_pos_shifts_TenantId_StoreId_OpenedAt",
                table: "pos_shifts",
                newName: "IX_pos_shifts_TenantId_LocationId_OpenedAt");

            migrationBuilder.RenameIndex(
                name: "IX_pos_shifts_StoreId",
                table: "pos_shifts",
                newName: "IX_pos_shifts_LocationId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                table: "iot_devices",
                newName: "LocationId");

            migrationBuilder.RenameIndex(
                name: "IX_iot_devices_TenantId_StoreId",
                table: "iot_devices",
                newName: "IX_iot_devices_TenantId_LocationId");

            migrationBuilder.RenameIndex(
                name: "IX_iot_devices_StoreId",
                table: "iot_devices",
                newName: "IX_iot_devices_LocationId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                table: "discounts",
                newName: "LocationId");

            migrationBuilder.RenameColumn(
                name: "FromStoreId",
                table: "stock_movements",
                newName: "FromLocationId");

            migrationBuilder.RenameColumn(
                name: "ToStoreId",
                table: "stock_movements",
                newName: "ToLocationId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                table: "demand_events",
                newName: "LocationId");

            migrationBuilder.RenameIndex(
                name: "IX_demand_events_StoreId",
                table: "demand_events",
                newName: "IX_demand_events_LocationId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                table: "daily_sales",
                newName: "LocationId");

            migrationBuilder.RenameIndex(
                name: "IX_daily_sales_StoreId_ProductId_Date",
                table: "daily_sales",
                newName: "IX_daily_sales_LocationId_ProductId_Date");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                table: "ai_order_suggestions",
                newName: "LocationId");

            migrationBuilder.RenameIndex(
                name: "IX_ai_order_suggestions_TenantId_StoreId_OrderDate",
                table: "ai_order_suggestions",
                newName: "IX_ai_order_suggestions_TenantId_LocationId_OrderDate");

            migrationBuilder.RenameIndex(
                name: "IX_ai_order_suggestions_StoreId",
                table: "ai_order_suggestions",
                newName: "IX_ai_order_suggestions_LocationId");

            migrationBuilder.RenameIndex(
                name: "IX_stores_TenantId",
                table: "locations",
                newName: "IX_locations_TenantId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                table: "location_zones",
                newName: "LocationId");

            migrationBuilder.RenameIndex(
                name: "IX_store_zones_StoreId",
                table: "location_zones",
                newName: "IX_location_zones_LocationId");

            migrationBuilder.AddColumn<string>(
                name: "BusinessType",
                table: "tenants",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "retail");

            migrationBuilder.AddColumn<string>(
                name: "LocationType",
                table: "locations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "retail_store");

            migrationBuilder.AddPrimaryKey(
                name: "PK_locations",
                table: "locations",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_location_zones",
                table: "location_zones",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ai_order_suggestions_locations_LocationId",
                table: "ai_order_suggestions",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_daily_sales_locations_LocationId",
                table: "daily_sales",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_demand_events_locations_LocationId",
                table: "demand_events",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_iot_devices_location_zones_ZoneId",
                table: "iot_devices",
                column: "ZoneId",
                principalTable: "location_zones",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_iot_devices_locations_LocationId",
                table: "iot_devices",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_location_zones_locations_LocationId",
                table: "location_zones",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_locations_tenants_TenantId",
                table: "locations",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_pos_shifts_locations_LocationId",
                table: "pos_shifts",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_pos_transactions_locations_LocationId",
                table: "pos_transactions",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_product_adu_locations_LocationId",
                table: "product_adu",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_product_buffer_locations_LocationId",
                table: "product_buffer",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_product_stock_location_zones_ZoneId",
                table: "product_stock",
                column: "ZoneId",
                principalTable: "location_zones",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_product_stock_locations_LocationId",
                table: "product_stock",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_transfers_locations_FromLocationId",
                table: "stock_transfers",
                column: "FromLocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_transfers_locations_ToLocationId",
                table: "stock_transfers",
                column: "ToLocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_supply_schedules_locations_LocationId",
                table: "supply_schedules",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_weather_data_locations_LocationId",
                table: "weather_data",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_write_offs_locations_LocationId",
                table: "write_offs",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_discounts_locations_LocationId",
                table: "discounts",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_movements_locations_FromLocationId",
                table: "stock_movements",
                column: "FromLocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_movements_locations_ToLocationId",
                table: "stock_movements",
                column: "ToLocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ── RLS: update location_zones policy (was store_zones, referenced stores table) ──
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS tenant_isolation ON location_zones;
                CREATE POLICY tenant_isolation ON location_zones
                  USING (EXISTS (
                    SELECT 1 FROM locations l
                    WHERE l.""Id"" = ""LocationId""
                      AND l.""TenantId"" = current_setting('app.tenant_id', true)::uuid
                  ));
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_discounts_locations_LocationId",
                table: "discounts");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_movements_locations_FromLocationId",
                table: "stock_movements");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_movements_locations_ToLocationId",
                table: "stock_movements");

            migrationBuilder.DropForeignKey(
                name: "FK_ai_order_suggestions_locations_LocationId",
                table: "ai_order_suggestions");

            migrationBuilder.DropForeignKey(
                name: "FK_daily_sales_locations_LocationId",
                table: "daily_sales");

            migrationBuilder.DropForeignKey(
                name: "FK_demand_events_locations_LocationId",
                table: "demand_events");

            migrationBuilder.DropForeignKey(
                name: "FK_iot_devices_location_zones_ZoneId",
                table: "iot_devices");

            migrationBuilder.DropForeignKey(
                name: "FK_iot_devices_locations_LocationId",
                table: "iot_devices");

            migrationBuilder.DropForeignKey(
                name: "FK_location_zones_locations_LocationId",
                table: "location_zones");

            migrationBuilder.DropForeignKey(
                name: "FK_locations_tenants_TenantId",
                table: "locations");

            migrationBuilder.DropForeignKey(
                name: "FK_pos_shifts_locations_LocationId",
                table: "pos_shifts");

            migrationBuilder.DropForeignKey(
                name: "FK_pos_transactions_locations_LocationId",
                table: "pos_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_product_adu_locations_LocationId",
                table: "product_adu");

            migrationBuilder.DropForeignKey(
                name: "FK_product_buffer_locations_LocationId",
                table: "product_buffer");

            migrationBuilder.DropForeignKey(
                name: "FK_product_stock_location_zones_ZoneId",
                table: "product_stock");

            migrationBuilder.DropForeignKey(
                name: "FK_product_stock_locations_LocationId",
                table: "product_stock");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_transfers_locations_FromLocationId",
                table: "stock_transfers");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_transfers_locations_ToLocationId",
                table: "stock_transfers");

            migrationBuilder.DropForeignKey(
                name: "FK_supply_schedules_locations_LocationId",
                table: "supply_schedules");

            migrationBuilder.DropForeignKey(
                name: "FK_weather_data_locations_LocationId",
                table: "weather_data");

            migrationBuilder.DropForeignKey(
                name: "FK_write_offs_locations_LocationId",
                table: "write_offs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_locations",
                table: "locations");

            migrationBuilder.DropPrimaryKey(
                name: "PK_location_zones",
                table: "location_zones");

            migrationBuilder.DropColumn(
                name: "BusinessType",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "LocationType",
                table: "locations");

            migrationBuilder.RenameTable(
                name: "locations",
                newName: "stores");

            migrationBuilder.RenameTable(
                name: "location_zones",
                newName: "store_zones");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "write_offs",
                newName: "StoreId");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "weather_data",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_weather_data_LocationId_Date",
                table: "weather_data",
                newName: "IX_weather_data_StoreId_Date");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "temperature_readings",
                newName: "StoreId");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "supply_schedules",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_supply_schedules_LocationId_SupplierId",
                table: "supply_schedules",
                newName: "IX_supply_schedules_StoreId_SupplierId");

            migrationBuilder.RenameColumn(
                name: "ToLocationId",
                table: "stock_transfers",
                newName: "ToStoreId");

            migrationBuilder.RenameColumn(
                name: "FromLocationId",
                table: "stock_transfers",
                newName: "FromStoreId");

            migrationBuilder.RenameColumn(
                name: "DestinationLocationId",
                table: "stock_receipts",
                newName: "DestinationStoreId");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "product_stock",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_product_stock_LocationId",
                table: "product_stock",
                newName: "IX_product_stock_StoreId");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "product_buffer",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_product_buffer_LocationId_ProductId",
                table: "product_buffer",
                newName: "IX_product_buffer_StoreId_ProductId");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "product_adu",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_product_adu_LocationId_ProductId",
                table: "product_adu",
                newName: "IX_product_adu_StoreId_ProductId");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "pos_transactions",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_pos_transactions_TenantId_LocationId_CreatedAt",
                table: "pos_transactions",
                newName: "IX_pos_transactions_TenantId_StoreId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_pos_transactions_LocationId",
                table: "pos_transactions",
                newName: "IX_pos_transactions_StoreId");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "pos_shifts",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_pos_shifts_TenantId_LocationId_OpenedAt",
                table: "pos_shifts",
                newName: "IX_pos_shifts_TenantId_StoreId_OpenedAt");

            migrationBuilder.RenameIndex(
                name: "IX_pos_shifts_LocationId",
                table: "pos_shifts",
                newName: "IX_pos_shifts_StoreId");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "iot_devices",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_iot_devices_TenantId_LocationId",
                table: "iot_devices",
                newName: "IX_iot_devices_TenantId_StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_iot_devices_LocationId",
                table: "iot_devices",
                newName: "IX_iot_devices_StoreId");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "discounts",
                newName: "StoreId");

            migrationBuilder.RenameColumn(
                name: "FromLocationId",
                table: "stock_movements",
                newName: "FromStoreId");

            migrationBuilder.RenameColumn(
                name: "ToLocationId",
                table: "stock_movements",
                newName: "ToStoreId");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "demand_events",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_demand_events_LocationId",
                table: "demand_events",
                newName: "IX_demand_events_StoreId");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "daily_sales",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_daily_sales_LocationId_ProductId_Date",
                table: "daily_sales",
                newName: "IX_daily_sales_StoreId_ProductId_Date");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "ai_order_suggestions",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_ai_order_suggestions_TenantId_LocationId_OrderDate",
                table: "ai_order_suggestions",
                newName: "IX_ai_order_suggestions_TenantId_StoreId_OrderDate");

            migrationBuilder.RenameIndex(
                name: "IX_ai_order_suggestions_LocationId",
                table: "ai_order_suggestions",
                newName: "IX_ai_order_suggestions_StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_locations_TenantId",
                table: "stores",
                newName: "IX_stores_TenantId");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "store_zones",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_location_zones_LocationId",
                table: "store_zones",
                newName: "IX_store_zones_StoreId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_stores",
                table: "stores",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_store_zones",
                table: "store_zones",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ai_order_suggestions_stores_StoreId",
                table: "ai_order_suggestions",
                column: "StoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_daily_sales_stores_StoreId",
                table: "daily_sales",
                column: "StoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_demand_events_stores_StoreId",
                table: "demand_events",
                column: "StoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_iot_devices_store_zones_ZoneId",
                table: "iot_devices",
                column: "ZoneId",
                principalTable: "store_zones",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_iot_devices_stores_StoreId",
                table: "iot_devices",
                column: "StoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_pos_shifts_stores_StoreId",
                table: "pos_shifts",
                column: "StoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_pos_transactions_stores_StoreId",
                table: "pos_transactions",
                column: "StoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_product_adu_stores_StoreId",
                table: "product_adu",
                column: "StoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_product_buffer_stores_StoreId",
                table: "product_buffer",
                column: "StoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_product_stock_store_zones_ZoneId",
                table: "product_stock",
                column: "ZoneId",
                principalTable: "store_zones",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_product_stock_stores_StoreId",
                table: "product_stock",
                column: "StoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_discounts_stores_StoreId",
                table: "discounts",
                column: "StoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_movements_stores_FromStoreId",
                table: "stock_movements",
                column: "FromStoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_movements_stores_ToStoreId",
                table: "stock_movements",
                column: "ToStoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_store_zones_stores_StoreId",
                table: "store_zones",
                column: "StoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_stores_tenants_TenantId",
                table: "stores",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_supply_schedules_stores_StoreId",
                table: "supply_schedules",
                column: "StoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_weather_data_stores_StoreId",
                table: "weather_data",
                column: "StoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_write_offs_stores_StoreId",
                table: "write_offs",
                column: "StoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
