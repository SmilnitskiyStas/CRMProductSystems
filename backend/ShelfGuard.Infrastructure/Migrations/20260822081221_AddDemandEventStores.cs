using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDemandEventStores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "demand_event_stores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demand_event_stores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_demand_event_stores_demand_events_EventId",
                        column: x => x.EventId,
                        principalTable: "demand_events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_demand_event_stores_locations_StoreId",
                        column: x => x.StoreId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_demand_event_stores_EventId",
                table: "demand_event_stores",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_demand_event_stores_EventId_StoreId",
                table: "demand_event_stores",
                columns: new[] { "EventId", "StoreId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_demand_event_stores_StoreId",
                table: "demand_event_stores",
                column: "StoreId");

            // ── RLS: demand_event_stores ─────────────────────────────────────
            // No own TenantId column — same shape as demand_event_coefficients
            // (20260611154601_V2EventsWeather): tenant is derived through the owning
            // event via EXISTS. Current canonical triad (FORCE RLS, NULLIF guard,
            // provider_bypass IN ('provider','provider_admin') since
            // 20260714150000_ExpandProviderBypassToProviderAdmin, worker_bypass since
            // 20260712175141_AddWorkerBypassRlsPolicy) — see database-schema.md "RLS
            // Template". demand_event_coefficients predates this triad and was never
            // backfilled; not revisited here (out of scope for TASK-592).
            migrationBuilder.Sql(@"
                ALTER TABLE demand_event_stores ENABLE ROW LEVEL SECURITY;
                ALTER TABLE demand_event_stores FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON demand_event_stores
                  USING (EXISTS (
                    SELECT 1 FROM demand_events ev
                    WHERE ev.""Id"" = ""EventId""
                      AND ev.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid))
                  WITH CHECK (EXISTS (
                    SELECT 1 FROM demand_events ev
                    WHERE ev.""Id"" = ""EventId""
                      AND ev.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid));

                CREATE POLICY provider_bypass ON demand_event_stores
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));

                CREATE POLICY worker_bypass ON demand_event_stores
                  USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS worker_bypass ON demand_event_stores;
                DROP POLICY IF EXISTS provider_bypass ON demand_event_stores;
                DROP POLICY IF EXISTS tenant_isolation ON demand_event_stores;
                ALTER TABLE demand_event_stores DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "demand_event_stores");
        }
    }
}
