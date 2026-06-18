using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "pos_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    TotalOrders = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TotalSpent = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customers_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_pos_tx_customer",
                table: "pos_transactions",
                column: "CustomerId",
                filter: "\"CustomerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_customers_email",
                table: "customers",
                columns: new[] { "TenantId", "Email" },
                filter: "\"Email\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_customers_phone",
                table: "customers",
                columns: new[] { "TenantId", "Phone" },
                filter: "\"Phone\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_customers_tenant",
                table: "customers",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_pos_transactions_customers_CustomerId",
                table: "pos_transactions",
                column: "CustomerId",
                principalTable: "customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql("ALTER TABLE customers ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY customers_tenant_isolation ON customers
                    USING ("TenantId" = current_setting('app.tenant_id')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_pos_transactions_customers_CustomerId",
                table: "pos_transactions");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropIndex(
                name: "idx_pos_tx_customer",
                table: "pos_transactions");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "pos_transactions");
        }
    }
}
