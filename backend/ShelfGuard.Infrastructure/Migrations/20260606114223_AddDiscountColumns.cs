using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscountColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "discounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "discounts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()");

            migrationBuilder.CreateIndex(
                name: "IX_discounts_ApprovedBy",
                table: "discounts",
                column: "ApprovedBy");

            migrationBuilder.CreateIndex(
                name: "IX_discounts_CreatedBy",
                table: "discounts",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_discounts_TenantId",
                table: "discounts",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_discounts_tenants_TenantId",
                table: "discounts",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_discounts_users_ApprovedBy",
                table: "discounts",
                column: "ApprovedBy",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_discounts_users_CreatedBy",
                table: "discounts",
                column: "CreatedBy",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_discounts_tenants_TenantId",
                table: "discounts");

            migrationBuilder.DropForeignKey(
                name: "FK_discounts_users_ApprovedBy",
                table: "discounts");

            migrationBuilder.DropForeignKey(
                name: "FK_discounts_users_CreatedBy",
                table: "discounts");

            migrationBuilder.DropIndex(
                name: "IX_discounts_ApprovedBy",
                table: "discounts");

            migrationBuilder.DropIndex(
                name: "IX_discounts_CreatedBy",
                table: "discounts");

            migrationBuilder.DropIndex(
                name: "IX_discounts_TenantId",
                table: "discounts");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "discounts");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "discounts");
        }
    }
}
