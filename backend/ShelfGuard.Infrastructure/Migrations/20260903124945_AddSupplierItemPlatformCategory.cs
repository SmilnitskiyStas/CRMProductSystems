using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// Supplier-portal expansion #8 (plan <c>1-partitioned-book.md</c>, Phase 6e): adds a real
    /// browse-taxonomy link <c>supplier_items.PlatformCategoryId</c> → <c>platform_categories("Id")</c>
    /// (<c>ON DELETE SET NULL</c> — the provider may soft-delete a global category). The legacy
    /// <c>supplier_items.Category</c> string key stays untouched; it drives the attribute-schema
    /// registry, which is a separate concern.
    ///
    /// <para>NO RLS change: <c>supplier_items</c> already carries the
    /// <c>tenant_isolation</c> + <c>provider_bypass</c> + <c>worker_bypass</c> triad (FORCE) and the
    /// new column inherits it. <c>store_scope</c> is deliberately absent (as for every supplier
    /// table — a <c>supplier_admin</c> has no <c>user_locations</c>).</para>
    ///
    /// <para>The standalone <c>IX_supplier_items_TenantId</c> is folded into the new composite
    /// <c>(TenantId, PlatformCategoryId)</c> index — its leading column still serves the TenantId FK
    /// and the RLS <c>tenant_isolation</c> predicate.</para>
    /// </summary>
    public partial class AddSupplierItemPlatformCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_supplier_items_TenantId",
                table: "supplier_items");

            migrationBuilder.AddColumn<Guid>(
                name: "PlatformCategoryId",
                table: "supplier_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_items_PlatformCategoryId",
                table: "supplier_items",
                column: "PlatformCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_items_TenantId_PlatformCategoryId",
                table: "supplier_items",
                columns: new[] { "TenantId", "PlatformCategoryId" });

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_items_platform_categories_PlatformCategoryId",
                table: "supplier_items",
                column: "PlatformCategoryId",
                principalTable: "platform_categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_supplier_items_platform_categories_PlatformCategoryId",
                table: "supplier_items");

            migrationBuilder.DropIndex(
                name: "IX_supplier_items_PlatformCategoryId",
                table: "supplier_items");

            migrationBuilder.DropIndex(
                name: "IX_supplier_items_TenantId_PlatformCategoryId",
                table: "supplier_items");

            migrationBuilder.DropColumn(
                name: "PlatformCategoryId",
                table: "supplier_items");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_items_TenantId",
                table: "supplier_items",
                column: "TenantId");
        }
    }
}
