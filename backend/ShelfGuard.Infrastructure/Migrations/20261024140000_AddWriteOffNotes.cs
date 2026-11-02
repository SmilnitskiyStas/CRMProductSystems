using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace ShelfGuard.Infrastructure.Migrations;

public partial class AddWriteOffNotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
        name: "Notes", table: "write_offs", type: "character varying(100)", maxLength: 100, nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(name: "Notes", table: "write_offs");
}
