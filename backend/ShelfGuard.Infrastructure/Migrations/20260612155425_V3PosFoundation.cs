using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V3PosFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pos_shifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShiftNumber = table.Column<int>(type: "integer", nullable: false),
                    FiscalShiftNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OpeningCash = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    ClosingCash = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    TotalSales = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 0m),
                    ZReportUrl = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pos_shifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pos_shifts_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pos_shifts_users_CashierId",
                        column: x => x.CashierId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "pos_transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShiftId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceiptNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FiscalNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PaymentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "cash"),
                    TotalAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "pending_fiscalization"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pos_transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pos_transactions_pos_shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "pos_shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_pos_transactions_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pos_transaction_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductStockId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    PriceRetail = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 0m),
                    PriceFinal = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CvConfidence = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pos_transaction_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pos_transaction_items_catalog_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "catalog_products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pos_transaction_items_pos_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "pos_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pos_transaction_items_product_stock_ProductStockId",
                        column: x => x.ProductStockId,
                        principalTable: "product_stock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pos_shifts_CashierId",
                table: "pos_shifts",
                column: "CashierId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_shifts_StoreId",
                table: "pos_shifts",
                column: "StoreId",
                unique: true,
                filter: "\"ClosedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_pos_shifts_TenantId_StoreId_OpenedAt",
                table: "pos_shifts",
                columns: new[] { "TenantId", "StoreId", "OpenedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_pos_transaction_items_ProductId",
                table: "pos_transaction_items",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_transaction_items_ProductStockId",
                table: "pos_transaction_items",
                column: "ProductStockId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_transaction_items_TransactionId",
                table: "pos_transaction_items",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_transactions_ShiftId",
                table: "pos_transactions",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_transactions_Status",
                table: "pos_transactions",
                column: "Status",
                filter: "\"Status\" = 'pending_fiscalization'");

            migrationBuilder.CreateIndex(
                name: "IX_pos_transactions_StoreId",
                table: "pos_transactions",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_transactions_TenantId_ReceiptNumber",
                table: "pos_transactions",
                columns: new[] { "TenantId", "ReceiptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pos_transactions_TenantId_StoreId_CreatedAt",
                table: "pos_transactions",
                columns: new[] { "TenantId", "StoreId", "CreatedAt" });

            // RLS — mandatory on every tenant table (tenant_isolation + provider_bypass).
            foreach (var tbl in new[] { "pos_shifts", "pos_transactions" })
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {tbl} ENABLE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON {tbl}
                      USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                    CREATE POLICY provider_bypass ON {tbl}
                      USING (current_setting('app.role', true) = 'provider');
                ");
            }

            // Items inherit tenant via parent transaction
            migrationBuilder.Sql(@"
                ALTER TABLE pos_transaction_items ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON pos_transaction_items
                  USING (EXISTS (
                    SELECT 1 FROM pos_transactions t
                    WHERE t.""Id"" = pos_transaction_items.""TransactionId""
                      AND t.""TenantId"" = current_setting('app.tenant_id', true)::uuid));
                CREATE POLICY provider_bypass ON pos_transaction_items
                  USING (current_setting('app.role', true) = 'provider');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pos_transaction_items");

            migrationBuilder.DropTable(
                name: "pos_transactions");

            migrationBuilder.DropTable(
                name: "pos_shifts");
        }
    }
}
