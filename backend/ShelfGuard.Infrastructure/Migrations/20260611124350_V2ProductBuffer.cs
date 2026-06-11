using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2ProductBuffer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_buffer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BufferTotal = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    BufferGreen = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    BufferYellow = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    BufferRed = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    LeadTimeDays = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    OrderCycleDays = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_buffer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_buffer_catalog_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "catalog_products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_product_buffer_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_buffer_ProductId",
                table: "product_buffer",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_product_buffer_StoreId_ProductId",
                table: "product_buffer",
                columns: new[] { "StoreId", "ProductId" },
                unique: true);

            // RLS — mandatory on every tenant table.
            migrationBuilder.Sql(@"
                ALTER TABLE product_buffer ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON product_buffer
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON product_buffer
                  USING (current_setting('app.role', true) = 'provider');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_buffer");
        }
    }
}
