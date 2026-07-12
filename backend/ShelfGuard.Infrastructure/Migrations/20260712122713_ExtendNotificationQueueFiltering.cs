using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExtendNotificationQueueFiltering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Required for the trigram GIN index below (keyword search on Title). Checked:
            // no prior migration enables it (grep for "CREATE EXTENSION" across Migrations/
            // returned zero hits) — this is the first trigram usage in the schema.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "notification_queue",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "notification_queue",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_notification_queue_tenant_createdat",
                table: "notification_queue",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "idx_notification_queue_tenant_eventtype",
                table: "notification_queue",
                columns: new[] { "TenantId", "EventType" });

            migrationBuilder.CreateIndex(
                name: "idx_notification_queue_tenant_store",
                table: "notification_queue",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "idx_notification_queue_tenant_user",
                table: "notification_queue",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "idx_notification_queue_title_trgm",
                table: "notification_queue",
                column: "Title")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // pg_trgm is intentionally not dropped here — a shared, low-risk-to-leave
            // extension; other future features may also want it, and DROP EXTENSION
            // would fail if anything else ends up depending on it.

            migrationBuilder.DropIndex(
                name: "idx_notification_queue_tenant_createdat",
                table: "notification_queue");

            migrationBuilder.DropIndex(
                name: "idx_notification_queue_tenant_eventtype",
                table: "notification_queue");

            migrationBuilder.DropIndex(
                name: "idx_notification_queue_tenant_store",
                table: "notification_queue");

            migrationBuilder.DropIndex(
                name: "idx_notification_queue_tenant_user",
                table: "notification_queue");

            migrationBuilder.DropIndex(
                name: "idx_notification_queue_title_trgm",
                table: "notification_queue");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "notification_queue");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "notification_queue");
        }
    }
}
