using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceDesk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_support_tickets_Status",
                table: "support_tickets");

            migrationBuilder.DropIndex(
                name: "IX_support_tickets_TenantId",
                table: "support_tickets");

            migrationBuilder.DropColumn(
                name: "Subject",
                table: "support_tickets");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "support_tickets",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "support_tickets",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "open",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "Priority",
                table: "support_tickets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "medium",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "support_tickets",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "support_tickets",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "general");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "support_tickets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "support_tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "support_tickets",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResolvedAt",
                table: "support_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "support_tickets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ticket_comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    IsInternal = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ticket_comments_support_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "support_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ticket_comments_users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_tickets_assigned",
                table: "support_tickets",
                columns: new[] { "TenantId", "AssignedTo" },
                filter: "\"AssignedTo\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_tickets_created_by",
                table: "support_tickets",
                columns: new[] { "TenantId", "CreatedBy" });

            migrationBuilder.CreateIndex(
                name: "idx_tickets_tenant_status",
                table: "support_tickets",
                columns: new[] { "TenantId", "Status", "CreatedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_CreatedBy",
                table: "support_tickets",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_LocationId",
                table: "support_tickets",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "idx_ticket_comments_ticket",
                table: "ticket_comments",
                columns: new[] { "TicketId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_comments_AuthorId",
                table: "ticket_comments",
                column: "AuthorId");

            migrationBuilder.AddForeignKey(
                name: "FK_support_tickets_locations_LocationId",
                table: "support_tickets",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_support_tickets_users_AssignedTo",
                table: "support_tickets",
                column: "AssignedTo",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_support_tickets_users_CreatedBy",
                table: "support_tickets",
                column: "CreatedBy",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // ── RLS: support_tickets ─────────────────────────────────────────
            migrationBuilder.Sql("ALTER TABLE support_tickets ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY support_tickets_tenant ON support_tickets
                    USING (tenant_id = current_setting('app.tenant_id')::uuid);
                """);

            // ── RLS: ticket_comments ─────────────────────────────────────────
            migrationBuilder.Sql("ALTER TABLE ticket_comments ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY ticket_comments_tenant ON ticket_comments
                    USING (tenant_id = current_setting('app.tenant_id')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_support_tickets_locations_LocationId",
                table: "support_tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_support_tickets_users_AssignedTo",
                table: "support_tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_support_tickets_users_CreatedBy",
                table: "support_tickets");

            migrationBuilder.DropTable(
                name: "ticket_comments");

            migrationBuilder.DropIndex(
                name: "idx_tickets_assigned",
                table: "support_tickets");

            migrationBuilder.DropIndex(
                name: "idx_tickets_created_by",
                table: "support_tickets");

            migrationBuilder.DropIndex(
                name: "idx_tickets_tenant_status",
                table: "support_tickets");

            migrationBuilder.DropIndex(
                name: "IX_support_tickets_CreatedBy",
                table: "support_tickets");

            migrationBuilder.DropIndex(
                name: "IX_support_tickets_LocationId",
                table: "support_tickets");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "support_tickets");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "support_tickets");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "support_tickets");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "support_tickets");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "support_tickets");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "support_tickets");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "support_tickets",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "NOW()");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "support_tickets",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "open");

            migrationBuilder.AlterColumn<string>(
                name: "Priority",
                table: "support_tickets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "medium");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "support_tickets",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "NOW()");

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                table: "support_tickets",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_Status",
                table: "support_tickets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_TenantId",
                table: "support_tickets",
                column: "TenantId");
        }
    }
}
