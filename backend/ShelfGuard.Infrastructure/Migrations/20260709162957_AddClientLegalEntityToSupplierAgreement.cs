using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClientLegalEntityToSupplierAgreement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientLegalEntityId",
                table: "supplier_agreements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_agreements_ClientLegalEntityId",
                table: "supplier_agreements",
                column: "ClientLegalEntityId");

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_agreements_legal_entities_ClientLegalEntityId",
                table: "supplier_agreements",
                column: "ClientLegalEntityId",
                principalTable: "legal_entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_supplier_agreements_legal_entities_ClientLegalEntityId",
                table: "supplier_agreements");

            migrationBuilder.DropIndex(
                name: "IX_supplier_agreements_ClientLegalEntityId",
                table: "supplier_agreements");

            migrationBuilder.DropColumn(
                name: "ClientLegalEntityId",
                table: "supplier_agreements");
        }
    }
}
