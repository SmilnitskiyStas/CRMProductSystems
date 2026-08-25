using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// TASK-613 (customer/loyalty domain expansion, approved plan `goofy-bubbling-naur.md`).
    /// Consumer review of a specific purchase — mirrors <c>supplier_reviews</c>' shape
    /// (rating + comment + one staff reply) but keyed to a PosTransaction instead of a
    /// Supplier. Unique index on PosTransactionId: one review per purchase — confirmed
    /// product decision (plan §1d), not a defensive guess. Restrict on PosTransactionId —
    /// a sale is never cascade-deleted by a review.
    ///
    /// Canonical RLS triad + direct-column consumer_self_access (ConsumerAccountId).
    /// </summary>
    public partial class AddPurchaseReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PosTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<short>(type: "smallint", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    ReplyText = table.Column<string>(type: "text", nullable: true),
                    RepliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RepliedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_purchase_reviews_consumer_accounts_ConsumerAccountId",
                        column: x => x.ConsumerAccountId,
                        principalTable: "consumer_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_reviews_pos_transactions_PosTransactionId",
                        column: x => x.PosTransactionId,
                        principalTable: "pos_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_reviews_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_reviews_users_RepliedByUserId",
                        column: x => x.RepliedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_reviews_ConsumerAccountId",
                table: "purchase_reviews",
                column: "ConsumerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_reviews_RepliedByUserId",
                table: "purchase_reviews",
                column: "RepliedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_reviews_TenantId",
                table: "purchase_reviews",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "uq_purchase_reviews_pos_transaction",
                table: "purchase_reviews",
                column: "PosTransactionId",
                unique: true);

            // ── RLS: purchase_reviews ─────────────────────────────────────────
            // Canonical triad + direct-column consumer_self_access.
            migrationBuilder.Sql(@"
                ALTER TABLE purchase_reviews ENABLE ROW LEVEL SECURITY;
                ALTER TABLE purchase_reviews FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON purchase_reviews
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON purchase_reviews
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON purchase_reviews
                  USING (current_setting('app.role', true) = 'worker');
                CREATE POLICY consumer_self_access ON purchase_reviews
                  USING (""ConsumerAccountId"" = (NULLIF(current_setting('app.consumer_account_id', true), ''))::uuid);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS consumer_self_access ON purchase_reviews;
                DROP POLICY IF EXISTS worker_bypass ON purchase_reviews;
                DROP POLICY IF EXISTS provider_bypass ON purchase_reviews;
                DROP POLICY IF EXISTS tenant_isolation ON purchase_reviews;
                ALTER TABLE purchase_reviews DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "purchase_reviews");
        }
    }
}
