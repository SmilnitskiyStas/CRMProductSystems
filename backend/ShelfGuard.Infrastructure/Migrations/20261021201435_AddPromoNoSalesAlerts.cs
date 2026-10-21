using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPromoNoSalesAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "promo_no_sales_alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromoKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    FirstDetectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    LastNotifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    NotifyCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promo_no_sales_alerts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_promo_no_sales_alerts_tenant",
                table: "promo_no_sales_alerts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "ux_promo_no_sales_alerts_key",
                table: "promo_no_sales_alerts",
                columns: new[] { "LocationId", "ProductId", "PromoKey" },
                unique: true);

            // RLS: tenant_isolation + provider/worker bypass + RESTRICTIVE store_scope (same shape as shelf_scans).
            migrationBuilder.Sql(@"
                ALTER TABLE promo_no_sales_alerts ENABLE ROW LEVEL SECURITY;
                ALTER TABLE promo_no_sales_alerts FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON promo_no_sales_alerts
                    USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON promo_no_sales_alerts
                    USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON promo_no_sales_alerts
                    USING (current_setting('app.role', true) = 'worker');
                CREATE POLICY store_scope ON promo_no_sales_alerts AS RESTRICTIVE
                  USING (
                    current_setting('app.role', true) IN ('provider', 'provider_admin', 'worker', 'enterprise_admin')
                    OR EXISTS (
                         SELECT 1 FROM user_locations ul
                         WHERE ul.""UserId"" = NULLIF(current_setting('app.user_id', true), '')::uuid
                           AND ul.""LocationId"" = promo_no_sales_alerts.""LocationId""
                       )
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "promo_no_sales_alerts");
        }
    }
}
