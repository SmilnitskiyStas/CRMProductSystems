using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddErpPosReturnPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pos_return_fiscalizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PaymentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    RequestJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    FiscalNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastError = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pos_return_fiscalizations", x => x.Id);
                    table.CheckConstraint("CK_pos_return_amount", "\"Amount\">=0 AND \"TaxAmount\">=0 AND \"TaxAmount\"<=\"Amount\" AND \"Attempts\">=0");
                    table.CheckConstraint("CK_pos_return_status", "\"Status\" IN ('pending','done','failed') AND (\"Status\"<>'done' OR \"FiscalNumber\" IS NOT NULL AND length(btrim(\"FiscalNumber\"))>0)");
                    table.ForeignKey(
                        name: "FK_pos_return_fiscalizations_business_documents_TenantId_Id",
                        columns: x => new { x.TenantId, x.Id },
                        principalTable: "business_documents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pos_return_fiscalizations_pos_shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "pos_shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pos_return_fiscalizations_pos_transactions_SaleId",
                        column: x => x.SaleId,
                        principalTable: "pos_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pos_return_fiscalizations_SaleId",
                table: "pos_return_fiscalizations",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_return_fiscalizations_ShiftId",
                table: "pos_return_fiscalizations",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_pos_return_fiscalizations_TenantId_ClientRequestId",
                table: "pos_return_fiscalizations",
                columns: new[] { "TenantId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pos_return_fiscalizations_TenantId_Id",
                table: "pos_return_fiscalizations",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_pos_return_fiscalizations_TenantId_SaleId",
                table: "pos_return_fiscalizations",
                columns: new[] { "TenantId", "SaleId" },
                unique: true);
            migrationBuilder.Sql(InventoryPosReturnFenceSql.Upgrade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Retain POS return and fiscal compensation audit; pause writers and reconcile before restore.");
        }
    }
}
