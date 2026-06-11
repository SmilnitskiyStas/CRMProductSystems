using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2EventsWeather : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "demand_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "custom"),
                    Scope = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "network"),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartsAt = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsAt = table.Column<DateOnly>(type: "date", nullable: false),
                    IsRecurring = table.Column<bool>(type: "boolean", nullable: false),
                    RecurrenceRule = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demand_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_demand_events_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "weather_coefficients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SegmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    TempAbove = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    TempBelow = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    WeatherCode = table.Column<int>(type: "integer", nullable: true),
                    Coefficient = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "manual")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_weather_coefficients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_weather_coefficients_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_weather_coefficients_product_segments_SegmentId",
                        column: x => x.SegmentId,
                        principalTable: "product_segments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "weather_data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    TempMin = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    TempMax = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    TempAvg = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    Precipitation = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    WeatherCode = table.Column<int>(type: "integer", nullable: true),
                    IsForecast = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_weather_data", x => x.Id);
                    table.ForeignKey(
                        name: "FK_weather_data_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "demand_event_coefficients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Coefficient = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 1.00m),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "manual")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demand_event_coefficients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_demand_event_coefficients_demand_events_EventId",
                        column: x => x.EventId,
                        principalTable: "demand_events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_demand_event_coefficients_EventId",
                table: "demand_event_coefficients",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_demand_events_StoreId",
                table: "demand_events",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_demand_events_TenantId_StartsAt_EndsAt",
                table: "demand_events",
                columns: new[] { "TenantId", "StartsAt", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_weather_coefficients_CategoryId",
                table: "weather_coefficients",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_weather_coefficients_SegmentId",
                table: "weather_coefficients",
                column: "SegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_weather_coefficients_TenantId",
                table: "weather_coefficients",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_weather_data_StoreId_Date",
                table: "weather_data",
                columns: new[] { "StoreId", "Date" },
                unique: true);

            // RLS. demand_events / weather_coefficients carry TenantId directly.
            foreach (var tbl in new[] { "demand_events", "weather_coefficients" })
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {tbl} ENABLE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON {tbl}
                      USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                    CREATE POLICY provider_bypass ON {tbl}
                      USING (current_setting('app.role', true) = 'provider');
                ");
            }

            // demand_event_coefficients: tenant derived through the owning event.
            migrationBuilder.Sql(@"
                ALTER TABLE demand_event_coefficients ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON demand_event_coefficients
                  USING (EXISTS (
                    SELECT 1 FROM demand_events ev
                    WHERE ev.""Id"" = ""EventId""
                      AND ev.""TenantId"" = current_setting('app.tenant_id', true)::uuid));
                CREATE POLICY provider_bypass ON demand_event_coefficients
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // weather_data: no TenantId column (per spec) — tenant derived through the store.
            migrationBuilder.Sql(@"
                ALTER TABLE weather_data ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON weather_data
                  USING (EXISTS (
                    SELECT 1 FROM stores s
                    WHERE s.""Id"" = ""StoreId""
                      AND s.""TenantId"" = current_setting('app.tenant_id', true)::uuid));
                CREATE POLICY provider_bypass ON weather_data
                  USING (current_setting('app.role', true) = 'provider');
            ");
            // Note: the worker writes weather_data as the table owner and is not
            // constrained by RLS — no extra policy needed.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "demand_event_coefficients");

            migrationBuilder.DropTable(
                name: "weather_coefficients");

            migrationBuilder.DropTable(
                name: "weather_data");

            migrationBuilder.DropTable(
                name: "demand_events");
        }
    }
}
