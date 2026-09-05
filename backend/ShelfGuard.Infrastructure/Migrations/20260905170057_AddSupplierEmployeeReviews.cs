using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// Supplier-portal expansion — Phase 8 (TASK-695). <c>supplier_employee_reviews</c> — a buyer
    /// (client tenant) rating one supplier-side employee, from a delivered order (rates the
    /// responsible manager = <c>MarketplaceOrder.ConfirmedByUserId</c>) or from a chat thread
    /// (rates a staff member who replied in it). Supplier-internal only: NOT shown on the public
    /// supplier profile and NOT rolled into <c>SupplierMetrics.Rating</c>.
    ///
    /// RLS — the ADR-033 split (buyer writes, supplier reads):
    ///   • <c>tenant_isolation</c> (FOR ALL + WITH CHECK) on <c>ClientTenantId</c> — the buyer is
    ///     the party that authors the rating; its own WITH CHECK admits the write, no override.
    ///   • <c>supplier_read</c> (FOR SELECT only) on <c>SupplierTenantId</c> — the supplier's
    ///     team-performance cabinet reads its employees' ratings, and may never write them.
    ///   • <c>provider_bypass</c> IN ('provider','provider_admin') + <c>worker_bypass</c>, both
    ///     from day one (feedback-rls-worker-bypass-missing / TASK-343 lesson).
    /// Both readable policies are PERMISSIVE so they OR for SELECT; the write path stays
    /// buyer-only. Mirrors <c>marketplace_order_receipts</c> exactly (20260821151649).
    ///
    /// Partial unique indexes: one order-rating per (employee, buyer, order) and one chat-rating
    /// per (employee, buyer, session) — each guarded by <c>WHERE "&lt;fk&gt;" IS NOT NULL</c>.
    /// </summary>
    public partial class AddSupplierEmployeeReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_employee_reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SupplierTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierUserName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    RatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RatedByName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Rating = table.Column<short>(type: "smallint", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", nullable: true),
                    Source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChatSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_employee_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_employee_reviews_marketplace_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "marketplace_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_supplier_employee_reviews_supplier_chat_sessions_ChatSessio~",
                        column: x => x.ChatSessionId,
                        principalTable: "supplier_chat_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_employee_reviews_ChatSessionId",
                table: "supplier_employee_reviews",
                column: "ChatSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_employee_reviews_OrderId",
                table: "supplier_employee_reviews",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_employee_reviews_SupplierTenantId_SupplierUserId_C~",
                table: "supplier_employee_reviews",
                columns: new[] { "SupplierTenantId", "SupplierUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_employee_reviews_SupplierUserId_ClientTenantId_Cha~",
                table: "supplier_employee_reviews",
                columns: new[] { "SupplierUserId", "ClientTenantId", "ChatSessionId" },
                unique: true,
                filter: "\"ChatSessionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_employee_reviews_SupplierUserId_ClientTenantId_Ord~",
                table: "supplier_employee_reviews",
                columns: new[] { "SupplierUserId", "ClientTenantId", "OrderId" },
                unique: true,
                filter: "\"OrderId\" IS NOT NULL");

            // ── RLS: supplier_employee_reviews (ADR-033 split — buyer writes / supplier reads) ──
            // NULLIF-guarded and fail-closed (no `IS NULL OR` branch) on both scoped policies. The
            // triad audit (RlsCrossTenantIntegrationTests) requires the literal names
            // tenant_isolation / provider_bypass / worker_bypass — satisfied; supplier_read is the
            // additive extra, exactly as on marketplace_order_receipts.
            //
            // The extra `"ClientTenantId" <> "SupplierTenantId"` guard on tenant_isolation closes
            // the one write a supplier session could otherwise make here: a supplier could name
            // ITSELF as the buyer (ClientTenantId = its own tenant = app.tenant_id, satisfying the
            // WITH CHECK) and thereby fabricate a rating of its own employee that shows in its own
            // team-performance view. A real buyer↔supplier pair is always two distinct tenants
            // (the agreement model + CreateReviewAsync guarantee it), so this costs nothing
            // legitimate and removes the self-rating vector entirely rather than documenting it as
            // a residual.
            migrationBuilder.Sql(@"
                ALTER TABLE supplier_employee_reviews ENABLE ROW LEVEL SECURITY;
                ALTER TABLE supplier_employee_reviews FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON supplier_employee_reviews
                  USING      (""ClientTenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
                              AND ""ClientTenantId"" <> ""SupplierTenantId"")
                  WITH CHECK (""ClientTenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
                              AND ""ClientTenantId"" <> ""SupplierTenantId"");

                CREATE POLICY supplier_read ON supplier_employee_reviews
                  FOR SELECT
                  USING (""SupplierTenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);

                CREATE POLICY provider_bypass ON supplier_employee_reviews
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));

                CREATE POLICY worker_bypass ON supplier_employee_reviews
                  USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS worker_bypass ON supplier_employee_reviews;
                DROP POLICY IF EXISTS provider_bypass ON supplier_employee_reviews;
                DROP POLICY IF EXISTS supplier_read ON supplier_employee_reviews;
                DROP POLICY IF EXISTS tenant_isolation ON supplier_employee_reviews;
                ALTER TABLE supplier_employee_reviews DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "supplier_employee_reviews");
        }
    }
}
