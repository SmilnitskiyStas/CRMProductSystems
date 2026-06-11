using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2AiOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_order_suggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ContextSnapshot = table.Column<string>(type: "jsonb", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "pending"),
                    AcceptedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AiModel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TokensUsed = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_order_suggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ai_order_suggestions_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_order_suggestion_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SuggestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityBase = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    QuantitySuggested = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    QuantityFinal = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    Reasoning = table.Column<string>(type: "text", nullable: true),
                    Confidence = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Factors = table.Column<string>(type: "jsonb", nullable: true),
                    WasEdited = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    EditReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_order_suggestion_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ai_order_suggestion_items_ai_order_suggestions_SuggestionId",
                        column: x => x.SuggestionId,
                        principalTable: "ai_order_suggestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ai_order_suggestion_items_catalog_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "catalog_products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_order_suggestion_items_ProductId",
                table: "ai_order_suggestion_items",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ai_order_suggestion_items_SuggestionId",
                table: "ai_order_suggestion_items",
                column: "SuggestionId");

            migrationBuilder.CreateIndex(
                name: "IX_ai_order_suggestions_StoreId",
                table: "ai_order_suggestions",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_ai_order_suggestions_TenantId_StoreId_OrderDate",
                table: "ai_order_suggestions",
                columns: new[] { "TenantId", "StoreId", "OrderDate" });

            migrationBuilder.Sql(@"
                ALTER TABLE ai_order_suggestions ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON ai_order_suggestions
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON ai_order_suggestions
                  USING (current_setting('app.role', true) = 'provider');

                ALTER TABLE ai_order_suggestion_items ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON ai_order_suggestion_items
                  USING (EXISTS (
                    SELECT 1 FROM ai_order_suggestions s
                    WHERE s.""Id"" = ""SuggestionId""
                      AND s.""TenantId"" = current_setting('app.tenant_id', true)::uuid));
                CREATE POLICY provider_bypass ON ai_order_suggestion_items
                  USING (current_setting('app.role', true) = 'provider');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_order_suggestion_items");

            migrationBuilder.DropTable(
                name: "ai_order_suggestions");
        }
    }
}
