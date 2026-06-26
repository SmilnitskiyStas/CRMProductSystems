using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemBarcodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_items_barcode_active",
                table: "items");

            migrationBuilder.AddColumn<List<string>>(
                name: "Barcodes",
                table: "items",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "Manufacturer",
                table: "items",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CountryOrigin",
                table: "items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Migrate existing Barcode values into the new Barcodes JSONB array
            migrationBuilder.Sql(
                "UPDATE items SET \"Barcodes\" = jsonb_build_array(\"Barcode\") WHERE \"Barcode\" IS NOT NULL AND \"Barcode\" != ''");

            migrationBuilder.DropColumn(
                name: "Barcode",
                table: "items");

            migrationBuilder.CreateIndex(
                name: "idx_items_barcodes_gin",
                table: "items",
                column: "Barcodes")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_items_barcodes_gin",
                table: "items");

            migrationBuilder.DropColumn(
                name: "Barcodes",
                table: "items");

            migrationBuilder.DropColumn(
                name: "Manufacturer",
                table: "items");

            migrationBuilder.DropColumn(
                name: "CountryOrigin",
                table: "items");

            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                table: "items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_items_barcode_active",
                table: "items",
                column: "Barcode",
                filter: "\"IsActive\" = true");
        }
    }
}
