using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V3IotFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "iot_devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    MqttTopic = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Config = table.Column<string>(type: "jsonb", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BatteryLevel = table.Column<int>(type: "integer", nullable: true),
                    FirmwareVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iot_devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_iot_devices_store_zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "store_zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_iot_devices_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "temperature_readings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    Temperature = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    Humidity = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    IsAlert = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_temperature_readings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_temperature_readings_iot_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "iot_devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "weight_readings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    WeightBefore = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    WeightAfter = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    DeltaWeight = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    Processed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Confidence = table.Column<int>(type: "integer", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_weight_readings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_weight_readings_iot_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "iot_devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_iot_devices_StoreId",
                table: "iot_devices",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_iot_devices_TenantId_DeviceId",
                table: "iot_devices",
                columns: new[] { "TenantId", "DeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_iot_devices_TenantId_StoreId",
                table: "iot_devices",
                columns: new[] { "TenantId", "StoreId" });

            migrationBuilder.CreateIndex(
                name: "IX_iot_devices_ZoneId",
                table: "iot_devices",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_temperature_readings_DeviceId_RecordedAt",
                table: "temperature_readings",
                columns: new[] { "DeviceId", "RecordedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_weight_readings_DeviceId_RecordedAt",
                table: "weight_readings",
                columns: new[] { "DeviceId", "RecordedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_weight_readings_Processed",
                table: "weight_readings",
                column: "Processed",
                filter: "\"Processed\" = false");

            // RLS — mandatory on every tenant table (tenant_isolation + provider_bypass).
            // iot_devices carries TenantId directly; readings inherit tenant via the device row.
            migrationBuilder.Sql(@"
                ALTER TABLE iot_devices ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON iot_devices
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON iot_devices
                  USING (current_setting('app.role', true) = 'provider');
            ");

            foreach (var tbl in new[] { "temperature_readings", "weight_readings" })
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {tbl} ENABLE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON {tbl}
                      USING (EXISTS (
                        SELECT 1 FROM iot_devices d
                        WHERE d.""Id"" = {tbl}.""DeviceId""
                          AND d.""TenantId"" = current_setting('app.tenant_id', true)::uuid));
                    CREATE POLICY provider_bypass ON {tbl}
                      USING (current_setting('app.role', true) = 'provider');
                ");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "temperature_readings");

            migrationBuilder.DropTable(
                name: "weight_readings");

            migrationBuilder.DropTable(
                name: "iot_devices");
        }
    }
}
