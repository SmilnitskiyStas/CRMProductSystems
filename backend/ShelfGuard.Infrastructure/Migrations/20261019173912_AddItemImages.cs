using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "item_images",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_item_images_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_item_images_tenant_item",
                table: "item_images",
                columns: new[] { "TenantId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "ux_item_images_one_primary",
                table: "item_images",
                column: "ItemId",
                unique: true,
                filter: "\"IsPrimary\" = true");

            // Existing single images become the primary row (before RLS is enabled, so the
            // INSERT..SELECT is not subject to the policies).
            migrationBuilder.Sql(@"
                INSERT INTO item_images (""Id"", ""TenantId"", ""ItemId"", ""ImageUrl"", ""Label"", ""SortOrder"", ""IsPrimary"", ""CreatedAt"")
                SELECT gen_random_uuid(), i.""TenantId"", i.""Id"", i.""ImageUrl"", NULL, 0, TRUE, NOW()
                FROM items i
                WHERE i.""ImageUrl"" IS NOT NULL AND btrim(i.""ImageUrl"") <> '' AND length(i.""ImageUrl"") <= 500;
            ");

            // Catalog metadata (product photos), same RLS category as items — tenant-isolated,
            // no store_scope restrictive policy needed.
            migrationBuilder.Sql(@"
                ALTER TABLE item_images ENABLE ROW LEVEL SECURITY;
                ALTER TABLE item_images FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON item_images
                    USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON item_images
                    USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON item_images
                    USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_images");
        }
    }
}
