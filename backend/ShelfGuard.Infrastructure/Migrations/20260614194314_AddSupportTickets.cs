using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "support_tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedTo = table.Column<Guid>(type: "uuid", nullable: true),
                    Subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_tickets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "support_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsProviderMessage = table.Column<bool>(type: "boolean", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_support_messages_support_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "support_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_support_messages_TicketId",
                table: "support_messages",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_AssignedTo",
                table: "support_tickets",
                column: "AssignedTo");

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_Status",
                table: "support_tickets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_TenantId",
                table: "support_tickets",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "support_messages");

            migrationBuilder.DropTable(
                name: "support_tickets");
        }
    }
}
