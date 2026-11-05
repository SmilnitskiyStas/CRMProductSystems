using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261023001000_AddProofBasedPasswordRecovery")]
public sealed class AddProofBasedPasswordRecovery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("PasswordResetTokenHash", "users", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<DateTime>("PasswordResetExpiresAt", "users", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<DateTime>("PasswordResetRequestedAt", "users", type: "timestamp with time zone", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Never roll back to the old public endpoint that overwrites passwords before proof.
        migrationBuilder.DropColumn("PasswordResetTokenHash", "users");
        migrationBuilder.DropColumn("PasswordResetExpiresAt", "users");
        migrationBuilder.DropColumn("PasswordResetRequestedAt", "users");
    }
}
